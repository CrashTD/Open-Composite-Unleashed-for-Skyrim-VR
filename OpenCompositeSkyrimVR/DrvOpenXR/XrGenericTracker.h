#pragma once

#include "XrTrackedDevice.h"

#include "../OpenOVR/Misc/BodyTrackerRoles.h"

/**
 * A body tracker exposed to the game as TrackedDeviceClass_GenericTracker.
 *
 * Poses come from XR_HTCX_vive_tracker_interaction role paths, which both
 * Virtual Desktop (VDXR body tracking, no hardware needed) and SteamVR's
 * OpenXR runtime (real Vive/Tundra/SlimeVR trackers) provide. Serials are
 * stable and role-named so mods like SkyrimVR-FBT can auto-assign or pin
 * roles by serial. Role set is ini-configurable (bodyTrackerRoles).
 */
class XrGenericTracker : public XrTrackedDevice {
public:
	// roleIndex indexes OCU_TRACKER_ROLES; deviceIndex is the OpenVR slot (3+)
	XrGenericTracker(int roleIndex, vr::TrackedDeviceIndex_t deviceIndex);

	void GetPose(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
	    ETrackingStateType trackingState) override;

	uint32_t GetStringTrackedDeviceProperty(vr::ETrackedDeviceProperty prop,
	    char* value, uint32_t bufferSize, vr::ETrackedPropertyError* pErrorL) override;

	vr::ETrackedDeviceClass GetTrackedDeviceClass() override;

private:
	int roleIndex;
};
