#include "OpenOVR/Compositor/VRSGaze.h"
#include "OpenOVR/Compositor/VRSPattern.h"
#include "OpenOVR/Misc/EyeGaze.h"

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

	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 0),
	    "a valid pose with runtime sample time unavailable is accepted");
	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 850000000),
	    "a gaze sample exactly 150 ms old is accepted");
	Check(!ocu_eye_gaze::IsSampleTimeUsable(1000000000, 849999999),
	    "a gaze sample older than 150 ms is rejected");
	Check(ocu_eye_gaze::IsSampleTimeUsable(1000000000, 1050000000),
	    "a predicted gaze sample exactly 50 ms ahead is accepted");
	Check(!ocu_eye_gaze::IsSampleTimeUsable(1000000000, 1050000001),
	    "a gaze sample more than 50 ms ahead is rejected");

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

	if (failures == 0)
		std::puts("OCU VRS gaze tests PASS");
	return failures == 0 ? 0 : 1;
}
