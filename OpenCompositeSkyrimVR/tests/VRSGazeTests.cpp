#include "OpenOVR/Compositor/VRSGaze.h"
#include "OpenOVR/Compositor/VRSPattern.h"
#include "OpenOVR/Misc/EyeGaze.h"
#include "OpenOVR/Misc/FoveationProfiles.h"
#include "OpenOVR/Misc/FoveationRates.h"

#include <cmath>
#include <cstdio>

static int failures = 0;

static void Check(bool condition, const char* message)
{
	if (!condition) {
		std::fprintf(stderr, "FAIL: %s\n", message);
		++failures;
	}
}

static bool Near(float a, float b, float tolerance = 0.0001f)
{
	return std::fabs(a - b) <= tolerance;
}

int main()
{
	using namespace ocu_vrs_gaze;
	Center center{};

	const auto eyeDefault = ocu_foveation::Resolve(true, -1, -1, -1, -1, -1, -1);
	const auto fixedDefault = ocu_foveation::Resolve(false, -1, -1, -1, -1, -1, -1);
	Check(Near(eyeDefault.inner, 0.5f) && Near(fixedDefault.inner, 0.7f),
	    "new installations have separate eye-tracked and fixed defaults");
	for (bool tracked : {false, true}) {
	    const auto legacy = ocu_foveation::Resolve(tracked, 0.63f, 0.83f, -1, -1, -1, -1);
	    Check(Near(legacy.inner, 0.63f) && Near(legacy.mid, 0.83f),
	        "legacy explicit sizes are preserved for both modes");
	}
	for (bool gazeValid : {false, true}) {
	    const auto selectedMode = SelectMode(true, true, gazeValid, false);
	    const auto selected = ocu_foveation::Resolve(selectedMode == Mode::EyeTracked,
	        0.63f, 0.83f, 0.75f, 0.9f, 0.45f, 0.65f);
	    Check(Near(selected.inner, gazeValid ? 0.45f : 0.75f),
	        "valid gaze selects smaller explicit profile; gaze loss selects explicit fixed profile");
	}
	const auto repaired = ocu_foveation::Resolve(true, -1, -1, -1, -1, 4.0f, 0.2f);
	Check(Near(repaired.inner, 1.0f) && Near(repaired.mid, 1.0f),
	    "profile bounds and ring ordering are sanitized");

	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 0),
	    "a valid pose with runtime sample time unavailable is accepted");
	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 850000000),
	    "a runtime-clamped gaze sample is accepted");
	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 100000000),
	    "sample time metadata does not invalidate an otherwise valid pose");
	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 1050000000),
	    "a runtime-predicted gaze sample is accepted");
	Check(!ocu_eye_gaze::IsSampleTimeUsable(0, 0),
	    "an invalid requested display time is rejected");

	Check(SelectMode(true, false, false, false) == Mode::Off,
	    "Auto without valid gaze stays off instead of falling back to Fixed");
	Check(SelectMode(true, false, true, false) == Mode::EyeTracked,
	    "Auto with valid gaze selects eye tracking");
	Check(SelectMode(false, true, false, false) == Mode::Fixed,
	    "explicit Fixed works without eye tracking");
	Check(SelectMode(true, true, false, false) == Mode::Fixed,
	    "explicit Fixed is the fallback only when both choices are enabled");
	Check(SelectMode(true, true, true, false) == Mode::EyeTracked,
	    "eye tracking takes priority when both choices are enabled");

	using ocu_vrs_pattern::Level;
	using ocu_vrs_pattern::SelectLevel;
	Check(ocu_vrs_pattern::TileCount(8448, 16) == 528 &&
	        ocu_vrs_pattern::TileCount(4608, 16) == 288,
	    "Galaxy XR stereo target produces the required 528x288 VRS atlas");
	Check(ocu_vrs_pattern::TileCount(8449, 16) == 529,
	    "partial edge tiles are rounded up instead of left uncovered");
	float eyeU = 0.0f;
	float eyeV = 0.0f;
	Check(ocu_vrs_pattern::NormalizeInEyeRegion(2112.0f, 2304.0f,
	          0, 0, 4224, 4608, eyeU, eyeV) && Near(eyeU, 0.5f) && Near(eyeV, 0.5f),
	    "left atlas half maps to left-eye normalized coordinates");
	Check(ocu_vrs_pattern::NormalizeInEyeRegion(6336.0f, 2304.0f,
	          4224, 0, 4224, 4608, eyeU, eyeV) && Near(eyeU, 0.5f) && Near(eyeV, 0.5f),
	    "right atlas half maps independently to right-eye normalized coordinates");
	Check(!ocu_vrs_pattern::NormalizeInEyeRegion(5000.0f, 2304.0f,
	          0, 0, 4224, 4608, eyeU, eyeV),
	    "a right-eye atlas pixel cannot contaminate the left-eye pattern");
	Check(SelectLevel(0.50f, 0.60f, 0.80f, true) == Level::Full,
	    "compatibility pattern keeps the fovea full-rate");
	Check(SelectLevel(0.70f, 0.60f, 0.80f, true) == Level::Half,
	    "compatibility pattern uses half-rate outside the fovea");
	Check(SelectLevel(1.20f, 0.60f, 0.80f, true) == Level::Half,
	    "compatibility pattern never reaches 2x2");
	Check(SelectLevel(0.70f, 0.60f, 0.80f, false) == Level::Half,
	    "performance pattern retains its middle half-rate ring");
	Check(SelectLevel(0.90f, 0.60f, 0.80f, false) == Level::Quarter,
	    "performance pattern retains opt-in 2x2 shading");
	Check(SelectMode(true, true, true, true) == Mode::Off,
	    "menus force VRS off");

	Check(Project(0.0f, 0.0f, -1.0f, -1.0f, 1.0f, 1.0f, -1.0f, center),
	    "forward gaze projects");
	Check(Near(center.x, 0.5f) && Near(center.y, 0.5f),
	    "forward gaze maps to optical center");

	Check(Project(0.5f, 0.25f, -1.0f, -1.0f, 1.0f, 1.0f, -1.0f, center),
	    "offset gaze projects");
	Check(Near(center.x, 0.75f) && Near(center.y, 0.375f),
	    "OpenXR +X/+Y maps right/up in texture coordinates");

	Check(!Project(0.0f, 0.0f, 0.1f, -1.0f, 1.0f, 1.0f, -1.0f, center),
	    "backward gaze rejected");
	Check(!Project(NAN, 0.0f, -1.0f, -1.0f, 1.0f, 1.0f, -1.0f, center),
	    "non-finite gaze rejected");

	Direction eyeLocal{};
	Check(ToEyeLocal(0.0f, 0.0f, -1.0f, 0.0f, 0.0f, 0.0f, 1.0f, eyeLocal),
	    "parallel eye orientation transforms");
	Check(Near(eyeLocal.x, 0.0f) && Near(eyeLocal.y, 0.0f) && Near(eyeLocal.z, -1.0f),
	    "parallel eye orientation leaves shared gaze unchanged");

	const float cantHalfAngle = 5.0f * 3.14159265358979323846f / 180.0f;
	const float cantSin = std::sin(cantHalfAngle);
	const float cantCos = std::cos(cantHalfAngle);
	Center leftCanted{};
	Center rightCanted{};
	Direction leftLocal{};
	Direction rightLocal{};
	Check(ProjectViewSpace(0.0f, 0.0f, -1.0f,
	          0.0f, cantSin, 0.0f, cantCos,
	          -1.0f, 1.0f, 1.0f, -1.0f, leftCanted, &leftLocal),
	    "left canted eye projects shared forward gaze");
	Check(ProjectViewSpace(0.0f, 0.0f, -1.0f,
	          0.0f, -cantSin, 0.0f, cantCos,
	          -1.0f, 1.0f, 1.0f, -1.0f, rightCanted, &rightLocal),
	    "right canted eye projects shared forward gaze");
	Check(leftLocal.x > 0.0f && rightLocal.x < 0.0f,
	    "inverse per-eye cant moves the shared gaze in opposite local directions");
	Check(leftCanted.x > 0.5f && rightCanted.x < 0.5f,
	    "canted eyes receive distinct per-eye foveation centers");
	Check(!ToEyeLocal(0.0f, 0.0f, -1.0f, 0.0f, 0.0f, 0.0f, 0.0f, eyeLocal),
	    "degenerate eye orientation rejected");

	Center leftFixation{};
	Center rightFixation{};
	Check(ProjectViewSpacePoint(0.0f, 0.0f, -2.0f,
	          -0.032f, 0.0f, 0.0f, 0.0f, cantSin, 0.0f, cantCos,
	          -1.0f, 1.0f, 1.0f, -1.0f, leftFixation),
	    "VIEW-space fixation point projects through the left eye pose");
	Check(ProjectViewSpacePoint(0.0f, 0.0f, -2.0f,
	          0.032f, 0.0f, 0.0f, 0.0f, -cantSin, 0.0f, cantCos,
	          -1.0f, 1.0f, 1.0f, -1.0f, rightFixation),
	    "VIEW-space fixation point projects through the right eye pose");
	Check(leftFixation.x > 0.5f && rightFixation.x < 0.5f,
	    "fixation projection includes both eye cant and IPD");
	Check(!ProjectViewSpacePoint(NAN, 0.0f, -2.0f,
	          0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f,
	          -1.0f, 1.0f, 1.0f, -1.0f, center),
	    "non-finite fixation point rejected");

	Center target{ 0.8f, 0.2f };
	Center first = Smooth({}, target, 1.0f / 90.0f, false);
	Check(Near(first.x, target.x) && Near(first.y, target.y),
	    "first sample is not delayed");
	Center next = Smooth({ 0.5f, 0.5f }, target, 1.0f / 90.0f, true);
	Check(next.x > 0.5f && next.x < target.x && next.y < 0.5f && next.y > target.y,
	    "steady sample receives bounded smoothing");

	{
		using namespace ocu_foveation;
		for (unsigned r = 0; r < 7; ++r) {
			const auto rate = static_cast<Rate>(r);
			Check(ParseRate(RateName(rate)) == rate, "rate name roundtrip");
			for (bool horizontal : {false, true}) {
				const auto size = Dimensions(CapHalf(rate, horizontal));
				Check(size.x * size.y <= 2, "compatibility caps all rates to half density");
				const RingRates requested{rate, rate, rate};
				Check(ResolveRates(true, true, false, horizontal, requested) == requested,
				    "uncapped eye profile preserves every explicit rate");
				Check(ResolveRates(false, true, false, horizontal, requested) ==
				    ResolveRates(false, false, false, horizontal, {}), "custom rates never leak to fixed fallback");
			}
		}
		Check(ParseRate("8x8") == Rate::X1x1 && ParseRate("") == Rate::X1x1, "invalid rate defaults full detail");
		Check(CapHalf(Rate::X4x2, false) == Rate::X2x1 &&
		    CapHalf(Rate::X2x4, true) == Rate::X1x2, "anisotropic cap preserves requested axis");
	}
	if (failures == 0)
		std::puts("OCU VRS gaze tests PASS");
	return failures == 0 ? 0 : 1;
}
