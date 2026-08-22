#pragma once

#include <cstdint>

namespace ocu_eye_gaze {

// XR_EXT_eye_gaze_interaction defines zero as "sample time unavailable";
// that is not an invalid pose. When a runtime does provide timing, reject
// samples more than 150 ms old or more than 50 ms beyond the requested frame.
inline bool IsSampleTimeUsable(std::int64_t displayTime, std::int64_t sampleTime)
{
	if (displayTime <= 0)
		return false;
	if (sampleTime == 0)
		return true;
	const std::int64_t age = displayTime - sampleTime;
	return age <= 150000000 && age >= -50000000;
}

} // namespace ocu_eye_gaze
