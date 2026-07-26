#include "XrGenericTracker.h"

// Poses come from BaseInput's tracker action spaces, same pattern as XrController
#include "../OpenOVR/Misc/xrmoreutils.h"
#include "../OpenOVR/Reimpl/BaseInput.h"
#include "generated/static_bases.gen.h"

XrGenericTracker::XrGenericTracker(int roleIndex, vr::TrackedDeviceIndex_t deviceIndex)
    : roleIndex(roleIndex)
{
	InitialiseDevice(deviceIndex);
}

void XrGenericTracker::GetPose(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
    ETrackingStateType trackingState)
{
	// Default to an invalid pose; FBT-style consumers ignore invalid poses
	// during calibration, so an inactive tracker is harmless.
	ZeroMemory(pose, sizeof(*pose));
	pose->bDeviceIsConnected = true;
	pose->bPoseIsValid = false;
	pose->eTrackingResult = vr::TrackingResult_Running_OutOfRange;

	BaseInput* input = GetUnsafeBaseInput();
	if (input == nullptr)
		return;

	XrSpace space = XR_NULL_HANDLE;
	input->GetTrackerSpace(roleIndex, space);
	if (!space)
		return;

	xr_utils::PoseFromSpace(pose, space, origin, glm::mat4(1.0f), ITrackedDevice::HAND_NONE);
}

uint32_t XrGenericTracker::GetStringTrackedDeviceProperty(vr::ETrackedDeviceProperty prop,
    char* value, uint32_t bufferSize, vr::ETrackedPropertyError* pErrorL)
{
	if (pErrorL)
		*pErrorL = vr::TrackedProp_Success;

#define PROP(in, out)                                                	if (prop == in) {                                                		if (value != NULL && bufferSize > 0) {                       			strcpy_s(value, bufferSize, out);                        		}                                                            		return (uint32_t)strlen(out) + 1;                            	}

	// Stable, role-named serials: FBT mods auto-assign roles by pose height,
	// and users can pin roles by these serials in their mod's ini.
	PROP(vr::Prop_SerialNumber_String, OCU_TRACKER_ROLES[roleIndex].serial);

	// Identify as a Vive-tracker-alike for apps that sniff the type
	PROP(vr::Prop_ControllerType_String, "vive_tracker");
	PROP(vr::Prop_ModelNumber_String, "OCU Body Tracker");
	PROP(vr::Prop_RenderModelName_String, "{htc}vr_tracker_vive_1_0");

#undef PROP

	return XrTrackedDevice::GetStringTrackedDeviceProperty(prop, value, bufferSize, pErrorL);
}

vr::ETrackedDeviceClass XrGenericTracker::GetTrackedDeviceClass()
{
	return vr::TrackedDeviceClass_GenericTracker;
}
