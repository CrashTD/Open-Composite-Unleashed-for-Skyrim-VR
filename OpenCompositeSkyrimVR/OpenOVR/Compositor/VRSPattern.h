#pragma once

#include <cmath>
#include <cstdint>

namespace ocu_vrs_pattern {

enum class Level : uint8_t {
	Full = 0,    // 1x1
	Half = 1,    // 2x1 or 1x2
	Quarter = 2 // 2x2
};

// Skyrim's terrain and alpha-tested foliage shaders can produce severe
// derivative/mip artifacts under 2x2 coarse shading. Compatibility mode keeps
// foveation active but caps every peripheral tile at half rate.
inline Level SelectLevel(float distance, float innerRadius, float midRadius,
    bool compatibilityMode)
{
	if (!std::isfinite(distance) || !std::isfinite(innerRadius) ||
	    !std::isfinite(midRadius))
		return Level::Full;

	const float inner = innerRadius < 0.0f ? 0.0f : innerRadius;
	const float mid = midRadius < inner ? inner : midRadius;

	if (distance < inner)
		return Level::Full;
	if (compatibilityMode || distance < mid)
		return Level::Half;
	return Level::Quarter;
}

} // namespace ocu_vrs_pattern
