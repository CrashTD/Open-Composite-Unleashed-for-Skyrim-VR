#include "XrNetworkTracker.h"

#include "../OpenOVR/Misc/NetworkTrackers.h"
#include "../OpenOVR/Misc/xrmoreutils.h"
#include "../OpenOVR/convert.h"

#include <glm/gtc/matrix_transform.hpp>
#include <glm/gtc/quaternion.hpp>

#include <cstdio>

XrNetworkTracker::XrNetworkTracker(int trackerIdx, vr::TrackedDeviceIndex_t deviceIndex)
    : trackerIdx(trackerIdx)
{
	snprintf(serial, sizeof(serial), "OCU-NET%d", trackerIdx + 1);
	InitialiseDevice(deviceIndex);
}

void XrNetworkTracker::GetPose(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
    ETrackingStateType trackingState)
{
	// Invalid until fresh data exists; FBT-style consumers ignore invalid
	// poses, so idle slots are harmless.
	ZeroMemory(pose, sizeof(*pose));
	pose->bDeviceIsConnected = true;
	pose->bPoseIsValid = false;
	pose->eTrackingResult = vr::TrackingResult_Running_OutOfRange;

	NetTrackerSample s;
	if (!NetworkTrackerReceiver::Instance().GetTracker(trackerIdx, s))
		return;
	if (NetworkTrackerReceiver::NowMs() - s.lastUpdateMs > 1500)
		return; // sender went quiet — report untracked rather than freeze

	// Sender convention is Unity: left-handed, y-up, +z forward, meters.
	// OpenXR is right-handed, y-up, -z forward: flip z on vectors.
	glm::vec3 pos(s.pos[0], s.pos[1], -s.pos[2]);
	glm::vec3 vel(s.vel[0], s.vel[1], -s.vel[2]);

	// Unity Quaternion.Euler(x,y,z) = Qy(y)*Qx(x)*Qz(z), degrees. Reproduce
	// that quaternion numerically, then reinterpret it in the z-flipped
	// right-handed frame, which negates the x and y components.
	float rx = glm::radians(s.eulerDeg[0]);
	float ry = glm::radians(s.eulerDeg[1]);
	float rz = glm::radians(s.eulerDeg[2]);
	glm::quat qU = glm::angleAxis(ry, glm::vec3(0, 1, 0))
	    * glm::angleAxis(rx, glm::vec3(1, 0, 0))
	    * glm::angleAxis(rz, glm::vec3(0, 0, 1));
	glm::quat q(qU.w, -qU.x, -qU.y, qU.z);

	// Shift sender space into our playspace (EMA of real-HMD minus sent-head;
	// zero when the sender never reports a head, i.e. assumed pre-aligned).
	float off[3];
	NetworkTrackerReceiver::Instance().GetAlignmentOffset(off);
	pos += glm::vec3(off[0], off[1], off[2]);

	glm::mat4 inFloor = glm::translate(glm::mat4(1.0f), pos) * glm::mat4_cast(q);

	// Network poses live in floor (standing) space; rebase for other origins
	glm::mat4 mat = inFloor;
	XrSpace base = xr_space_from_tracking_origin(origin);
	if (base != xr_gbl->floorSpace) {
		XrSpaceLocation loc = { XR_TYPE_SPACE_LOCATION };
		if (XR_SUCCEEDED(xrLocateSpace(xr_gbl->floorSpace, base, xr_gbl->GetBestTime(), &loc))
		    && (loc.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT))
			mat = X2G_om34_pose(loc.pose) * inFloor;
	}

	pose->mDeviceToAbsoluteTracking = G2S_m34(mat);
	pose->vVelocity.v[0] = vel.x; // world-space, needed for FBT kick physics
	pose->vVelocity.v[1] = vel.y;
	pose->vVelocity.v[2] = vel.z;
	pose->bPoseIsValid = true;
	pose->eTrackingResult = vr::TrackingResult_Running_OK;
}

uint32_t XrNetworkTracker::GetStringTrackedDeviceProperty(vr::ETrackedDeviceProperty prop,
    char* value, uint32_t bufferSize, vr::ETrackedPropertyError* pErrorL)
{
	if (pErrorL)
		*pErrorL = vr::TrackedProp_Success;

#define PROP(in, out)                                                \
	if (prop == in) {                                                \
		if (value != NULL && bufferSize > 0) {                       \
			strcpy_s(value, bufferSize, out);                        \
		}                                                            \
		return (uint32_t)strlen(out) + 1;                            \
	}

	PROP(vr::Prop_SerialNumber_String, serial);

	// Identify as a Vive-tracker-alike for apps that sniff the type
	PROP(vr::Prop_ControllerType_String, "vive_tracker");
	PROP(vr::Prop_ModelNumber_String, "OCU Network Tracker");
	PROP(vr::Prop_RenderModelName_String, "{htc}vr_tracker_vive_1_0");

#undef PROP

	return XrTrackedDevice::GetStringTrackedDeviceProperty(prop, value, bufferSize, pErrorL);
}

vr::ETrackedDeviceClass XrNetworkTracker::GetTrackedDeviceClass()
{
	return vr::TrackedDeviceClass_GenericTracker;
}
