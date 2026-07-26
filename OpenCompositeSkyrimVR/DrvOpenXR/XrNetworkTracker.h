#pragma once

#include "XrTrackedDevice.h"

/**
 * A network-fed body tracker exposed as TrackedDeviceClass_GenericTracker.
 *
 * Poses come from NetworkTrackerReceiver (VRChat-style OSC over UDP: SlimeVR
 * "OSC Trackers" output, Standable, phone IMU apps), giving SteamVR-driver
 * tracker ecosystems a path into OCU where no vrserver exists. Serials are
 * OCU-NET1..OCU-NET8; FBT consumers auto-assign roles by pose height, so
 * slot order does not matter. Devices sit after the HTCX body trackers.
 */
class XrNetworkTracker : public XrTrackedDevice {
public:
	// trackerIdx is the 0-based OSC tracker slot; deviceIndex the OpenVR slot
	XrNetworkTracker(int trackerIdx, vr::TrackedDeviceIndex_t deviceIndex);

	void GetPose(vr::ETrackingUniverseOrigin origin, vr::TrackedDevicePose_t* pose,
	    ETrackingStateType trackingState) override;

	uint32_t GetStringTrackedDeviceProperty(vr::ETrackedDeviceProperty prop,
	    char* value, uint32_t bufferSize, vr::ETrackedPropertyError* pErrorL) override;

	vr::ETrackedDeviceClass GetTrackedDeviceClass() override;

private:
	int trackerIdx;
	char serial[16];
};
