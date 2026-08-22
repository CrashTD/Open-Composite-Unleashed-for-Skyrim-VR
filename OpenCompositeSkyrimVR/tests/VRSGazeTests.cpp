#include "OpenOVR/Compositor/VRSGaze.h"
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
