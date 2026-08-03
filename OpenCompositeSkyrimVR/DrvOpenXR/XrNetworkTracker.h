#pragma once

#include "XrTrackedDevice.h"

#include <cstdint>

/**
 * A canonical body tracker exposed as TrackedDeviceClass_GenericTracker.
 *
 * Slots 1..3 represent waist/left-foot/right-foot. A matching runtime HTCX
 * pose (Vive/Tundra/VDXR) wins automatically and OSC camera data is the
 * fallback. In full eight-slot mode knees, elbows and chest use the same mux.
 * Stable OCU-NET identities let FBT retain one calibration while the source
 * changes underneath it.
 */
class XrNetworkTracker : public XrTrackedDevice {
public:
	// trackerIdx is the 0-based OSC tracker slot; deviceIndex the OpenVR slot
	XrNetworkTracker(int trackerIdx, vr::TrackedDeviceIndex_t deviceIndex,
	    ITrackedDevice* htcxRoleSource = nullptr);

	void GetPose(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
	    ETrackingStateType trackingState) override;
	// The gait detector still needs raw foot motion while public foot poses are
	// temporarily yielded to VRIK's locomotion animation.
	void GetPoseForLocomotion(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
	    ETrackingStateType trackingState);

	uint32_t GetStringTrackedDeviceProperty(vr::ETrackedDeviceProperty prop,
	    char* value, uint32_t bufferSize, vr::ETrackedPropertyError* pErrorL) override;

	vr::ETrackedDeviceClass GetTrackedDeviceClass() override;

private:
	void GetPoseImpl(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
	    ETrackingStateType trackingState, bool allowLocomotionRelease);
	int trackerIdx;
	ITrackedDevice* htcxRoleSource;
	bool loggedHtcxSource = false;
	uint64_t kickPassthroughUntilMs = 0;
	uint64_t kickRecoveryDeadlineMs = 0;
	uint32_t lastFrameEpoch = 0;
	bool lastFrameEpochValid = false;
	char serial[16];
};
