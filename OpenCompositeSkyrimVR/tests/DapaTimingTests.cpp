#include "DrvOpenXR/DapaTiming.h"
#include <cstdio>
#include <stdexcept>
#include <limits>

static void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
static int acquireCalls, waitCalls, releaseCalls;
static XrResult waitResult = XR_SUCCESS, releaseResult = XR_SUCCESS;
static XrDuration observedBudget;
static XrResult XRAPI_CALL Acquire(XrSwapchain, const XrSwapchainImageAcquireInfo*, uint32_t* index)
{ ++acquireCalls; *index = 2; return XR_SUCCESS; }
static XrResult XRAPI_CALL Wait(XrSwapchain, const XrSwapchainImageWaitInfo* info)
{ ++waitCalls; observedBudget = info->timeout; return waitResult; }
static XrResult XRAPI_CALL Release(XrSwapchain, const XrSwapchainImageReleaseInfo*)
{ ++releaseCalls; return releaseResult; }

int main()
{
	try {
		for (double hz : {60., 72., 80., 90., 96., 100., 120., 144.}) {
			const double period = 1000.0 / hz;
			Check(std::abs(DapaTiming::StallLimitMs(period) / period - 2.7) < 1e-9, "refresh-relative stall limit");
			Check(DapaTiming::ImageWaitBudget(period) > 0 && DapaTiming::ImageWaitBudget(period) <= 2000000, "bounded wait budget");
			const double engageInterval = std::max(1000.0 / DapaTiming::AutoEngageFps(50, period), 1.15 * period);
			Check(engageInterval < 2.05 * period, "adaptive mode must have an engagement band at every rate");
			DapaTiming::PacingGuard pacing;
			Check(DapaTiming::EndPressureLimitMs(12, period) >= 1.25 * period, "pressure threshold tolerates one slot");
			for (int i = 0; i < 100; ++i) {
				Check(!pacing.Observe(period, period, 12, period), "routine slot pacing is not failure");
				Check(!pacing.Observe(period, 1.5 * period, 12, period), "isolated moderate spike must not park DAPA");
			}
			pacing = {};
			Check(!pacing.Observe(period, 1.5 * period, 12, period), "first slow end tolerated");
			Check(!pacing.Observe(period, 1.5 * period, 12, period), "second slow end tolerated");
			Check(pacing.Observe(period, 1.5 * period, 12, period), "sustained pressure yields");
			Check(std::abs(pacing.backoffMs - 2 * period) < 1e-9, "initial yield is two display slots");
			for (int i = 0; i < 100; ++i) {
				pacing.Advance(300);
				Check(pacing.Observe(period, 8 * period, 12, period), "multi-slot end stall yields immediately");
				Check(pacing.backoffMs > 0 && pacing.backoffMs <= 250, "accepted submissions never trigger seconds-long backoff");
			}
			pacing.Advance(300);
			Check(pacing.backoffMs == 0, "pacing cooldown expires in wall time");
			for (int i = 0; i < 100; ++i) pacing.Observe(period, 0.1 * period, 12, period);
			Check(pacing.level == 0, "healthy attempts restore short recovery");
			Check(pacing.Observe(3 * period, 0, 0, period), "wait stall protection survives disabled end threshold");
			pacing = {};
			Check(!pacing.Observe(period, 100, 0, period), "explicit zero disables end pressure policy");
			DapaTiming::Recovery recovery;
			recovery.Trouble();
			double elapsed = 0;
			while (recovery.backoffMs > 0) { recovery.Advance(2 * period); elapsed += 2 * period; }
			Check(elapsed >= 180.0 && elapsed < 180.0 + 2 * period + 1e-8, "cooldown must expire by wall time");
			for (int i = 0; i < 20; ++i) recovery.Trouble();
			Check(recovery.level == 3 && recovery.backoffMs == 11380.0, "bounded backoff escalation");
			recovery.Advance(12000.0);
			Check(recovery.backoffMs == 0, "slow frame must not prolong cooldown");
			recovery.CleanInjection(10000.0);
			Check(recovery.level == 2, "clean time de-escalates");
			recovery.engageHoldMs = 120000;
			recovery.Advance(121000);
			Check(recovery.dwellMs > recovery.engageHoldMs, "maximum dwell must remain recoverable");
			std::printf("DAPA %g Hz timing PASS\n", hz);
		}
		// Regression: the observed 12-19ms successful ends must not produce an
		// 11.38-second error cooldown. Truly failed API calls retain that safeguard.
		DapaTiming::PacingGuard observed;
		DapaTiming::Recovery errors;
		for (double endMs : {19.2, 64.7, 50.3, 16.0, 12.6, 13.1, 15.7, 1.0}) {
			observed.Advance(300);
			observed.Observe(9.6, endMs, 12, 1000.0 / 90.0);
			Check(observed.backoffMs <= 250 && errors.backoffMs == 0, "accepted log replay never trips API-error recovery");
		}
		Check(DapaTiming::Accepted(XR_SUCCESS), "successful submission counted");
		Check(!DapaTiming::Accepted(XR_ERROR_RUNTIME_FAILURE), "failed submission not counted");
		Check(!DapaTiming::Accepted(XR_SESSION_LOSS_PENDING), "loss pending not counted as healthy");
		Check(std::isfinite(DapaTiming::PeriodMs(std::numeric_limits<double>::quiet_NaN())), "invalid period fallback");
		DapaTiming::ImageLease lease;
		waitResult = XR_TIMEOUT_EXPIRED;
		Check(lease.Wait(XR_NULL_HANDLE, 2000000, Acquire, Wait) == XR_TIMEOUT_EXPIRED, "timeout detection");
		Check(lease.acquired && !lease.writable && acquireCalls == 1, "timeout retains ownership");
		Check(lease.Release(XR_NULL_HANDLE, Release) == XR_ERROR_CALL_ORDER_INVALID && releaseCalls == 0, "no early release");
		for (int i = 0; i < 1000; ++i)
			Check(lease.Wait(XR_NULL_HANDLE, 0, Acquire, Wait) == XR_TIMEOUT_EXPIRED, "repeated timeout");
		Check(acquireCalls == 1 && observedBudget == 0, "retry same image without queue growth");
		waitResult = XR_SUCCESS;
		Check(lease.Wait(XR_NULL_HANDLE, 1000, Acquire, Wait) == XR_SUCCESS && lease.index == 2, "recovery preserves image index");
		Check(lease.Release(XR_NULL_HANDLE, Release) == XR_SUCCESS && !lease.acquired, "release after successful wait");
		Check(lease.Wait(XR_NULL_HANDLE, 1000, Acquire, Wait) == XR_SUCCESS && acquireCalls == 2, "next acquire after release");
		releaseResult = XR_ERROR_RUNTIME_FAILURE;
		Check(lease.Release(XR_NULL_HANDLE, Release) == XR_ERROR_RUNTIME_FAILURE, "release error");
		const int waitsBefore = waitCalls;
		Check(lease.Wait(XR_NULL_HANDLE, 0, Acquire, Wait) == XR_ERROR_RUNTIME_FAILURE && waitCalls == waitsBefore, "no reuse after failed release");
		lease = {};
		Check(!lease.acquired && !lease.writable && lease.failure == XR_SUCCESS, "resize/shutdown reset");
		waitResult = XR_SESSION_LOSS_PENDING;
		Check(lease.Wait(XR_NULL_HANDLE, 1000, Acquire, Wait) == XR_SESSION_LOSS_PENDING && !lease.writable, "positive session loss status is not a ready image");
		lease = {};
		waitResult = XR_ERROR_SESSION_LOST;
		Check(lease.Wait(XR_NULL_HANDLE, 1000, Acquire, Wait) == XR_ERROR_SESSION_LOST && !lease.writable, "session loss cannot publish an image");
		std::puts("DAPA ownership/recovery PASS");
	} catch (const std::exception& e) { std::fprintf(stderr, "FAIL: %s\n", e.what()); return 1; }
}
