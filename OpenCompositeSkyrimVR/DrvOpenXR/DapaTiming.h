#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <openxr/openxr.h>

namespace DapaTiming {
inline double PeriodMs(double reported)
{
	return std::isfinite(reported) && reported > 0.0 ? reported : 1000.0 / 90.0;
}
inline double StallLimitMs(double period) { return 2.7 * PeriodMs(period); }
inline double AutoEngageFps(double requested, double period)
{
	if (!std::isfinite(requested)) requested = 50.0;
	// Keep a nonempty engagement band above half-rate at high headset refresh.
	return std::max(std::max(20.0, requested), 550.0 / PeriodMs(period));
}
inline XrDuration ImageWaitBudget(double period)
{
	return static_cast<XrDuration>(std::min(2.0, PeriodMs(period) * 0.25) * 1000000.0);
}

// Accepted submissions can block without being API failures. Require repeated
// moderate pressure, or one multi-slot stall, before briefly yielding to real
// frames. Never feed these samples into the API-error exponential backoff.
inline double EndPressureLimitMs(double configuredAt90Hz, double period)
{
	if (!std::isfinite(configuredAt90Hz) || configuredAt90Hz <= 0.0) return 0.0;
	return std::max(1.25 * PeriodMs(period), configuredAt90Hz * PeriodMs(period) / (1000.0 / 90.0));
}
struct PacingGuard {
	double backoffMs = 0.0;
	double cleanMs = 0.0;
	int consecutiveSlow = 0;
	int level = 0;
	void Advance(double elapsedMs) { backoffMs = std::max(0.0, backoffMs - std::max(0.0, elapsedMs)); }
	bool Observe(double waitMs, double endMs, double configuredAt90Hz, double period)
	{
		period = PeriodMs(period);
		const double limit = EndPressureLimitMs(configuredAt90Hz, period);
		const bool severe = waitMs > StallLimitMs(period) || (limit > 0 && endMs > StallLimitMs(period));
		const bool slow = limit > 0 && endMs > limit;
		consecutiveSlow = slow ? consecutiveSlow + 1 : 0;
		if (severe || consecutiveSlow >= 3) {
			backoffMs = std::min(250.0, period * (2u << level));
			level = std::min(3, level + 1);
			consecutiveSlow = 0;
			cleanMs = 0.0;
			return true;
		}
		if (!slow) {
			// Credit actual successful attempts, not time spent in a cooldown.
			cleanMs += 2.0 * period;
			if (cleanMs >= 500.0) { level = 0; cleanMs = 500.0; }
		} else cleanMs = 0.0;
		return false;
	}
};

// Positive loss-pending statuses are not evidence of a healthy submission.
inline bool Accepted(XrResult result) { return result == XR_SUCCESS; }

// Recovery uses wall time, so a falling frame rate cannot lengthen a cooldown.
struct Recovery {
	double backoffMs = 0.0;
	double cleanMs = 0.0;
	double engagedCleanMs = 0.0;
	double engageHoldMs = 3000.0;
	double realCleanMs = 90.0;
	double dwellMs = 0.0;
	int level = 0;
	bool forceRelease = false;
	void Advance(double elapsedMs)
	{
		backoffMs = std::max(0.0, backoffMs - elapsedMs);
		dwellMs = std::min(600000.0, dwellMs + elapsedMs);
	}
	void Trouble()
	{
		constexpr double delays[] = { 180.0, 710.0, 2840.0, 11380.0 };
		cleanMs = engagedCleanMs = 0.0;
		backoffMs = delays[level];
		level = std::min(3, level + 1);
		if (level >= 2) forceRelease = true;
	}
	void CleanInjection(double elapsedMs)
	{
		cleanMs = std::min(10000.0, cleanMs + elapsedMs);
		if (cleanMs >= 10000.0 && level > 0) {
			--level;
			cleanMs = 0.0;
		}
	}
};

// A timed-out image remains acquired. Retrying must wait on that same image,
// never reacquire or release it before a successful wait (OpenXR ownership rule).
struct ImageLease {
	uint32_t index = 0;
	bool acquired = false;
	bool writable = false;
	XrResult failure = XR_SUCCESS;
	XrResult Wait(XrSwapchain chain, XrDuration budget,
	    PFN_xrAcquireSwapchainImage acquire, PFN_xrWaitSwapchainImage wait)
	{
		if (failure != XR_SUCCESS) return failure;
		if (!acquired) {
			XrSwapchainImageAcquireInfo info{ XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
			XrResult result = acquire(chain, &info, &index);
			if (result != XR_SUCCESS) return result;
			acquired = true;
		}
		if (writable) return XR_SUCCESS;
		XrSwapchainImageWaitInfo info{ XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
		info.timeout = std::max<XrDuration>(0, budget);
		XrResult result = wait(chain, &info);
		if (result == XR_SUCCESS) writable = true;
		else if (result != XR_TIMEOUT_EXPIRED) failure = result;
		return result;
	}
	XrResult Release(XrSwapchain chain, PFN_xrReleaseSwapchainImage release)
	{
		if (!writable) return XR_ERROR_CALL_ORDER_INVALID;
		XrSwapchainImageReleaseInfo info{ XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
		XrResult result = release(chain, &info);
		if (result == XR_SUCCESS) { acquired = writable = false; }
		else failure = result;
		return result;
	}
};
}
