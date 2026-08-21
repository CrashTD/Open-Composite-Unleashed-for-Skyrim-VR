#pragma once

#include <algorithm>
#include <cmath>

namespace ocu_vrs_gaze {

struct Center {
	float x = 0.5f;
	float y = 0.5f;
};

enum class Mode {
	Off,
	Fixed,
	EyeTracked
};

// Auto uses gaze only when a valid sample exists. Fixed is never selected
// implicitly; it must be enabled independently by the user.
inline Mode SelectMode(bool eyeTrackingAuto, bool fixedEnabled, bool gazeValid, bool menuOpen)
{
	if (menuOpen)
		return Mode::Off;
	if (eyeTrackingAuto && gazeValid)
		return Mode::EyeTracked;
	if (fixedEnabled)
		return Mode::Fixed;
	return Mode::Off;
}

// Project a head/view-space gaze direction into one submitted eye using that
// eye's OpenXR FOV tangents. OpenXR looks down -Z and texture V grows down.
inline bool Project(float dirX, float dirY, float dirZ,
    float tanLeft, float tanRight, float tanUp, float tanDown,
    Center& out)
{
	if (!std::isfinite(dirX) || !std::isfinite(dirY) || !std::isfinite(dirZ) ||
	    !std::isfinite(tanLeft) || !std::isfinite(tanRight) ||
	    !std::isfinite(tanUp) || !std::isfinite(tanDown) || dirZ >= -0.01f)
		return false;

	const float spanX = tanRight - tanLeft;
	const float spanY = tanUp - tanDown;
	if (spanX <= 0.001f || spanY <= 0.001f)
		return false;

	const float tanX = dirX / -dirZ;
	const float tanY = dirY / -dirZ;
	const float x = (tanX - tanLeft) / spanX;
	const float y = (tanUp - tanY) / spanY;
	if (!std::isfinite(x) || !std::isfinite(y))
		return false;

	// Looking beyond the visible eye still needs a useful edge fovea. A small
	// inset keeps the full-rate ring on the shading-rate image instead of mostly
	// outside it, while normal in-FOV samples remain untouched.
	out.x = std::clamp(x, 0.02f, 0.98f);
	out.y = std::clamp(y, 0.02f, 0.98f);
	return true;
}

inline Center Smooth(const Center& previous, const Center& target, float dtSeconds,
    bool hasPrevious, float cutoffHz = 30.0f)
{
	if (!hasPrevious || !std::isfinite(dtSeconds) || dtSeconds <= 0.0f || dtSeconds > 0.1f)
		return target;
	const float alpha = 1.0f - std::exp(-2.0f * 3.14159265358979323846f * cutoffHz * dtSeconds);
	return {
		previous.x + alpha * (target.x - previous.x),
		previous.y + alpha * (target.y - previous.y)
	};
}

} // namespace ocu_vrs_gaze
