#pragma once

// VRChat-style OSC tracker receiver.
//
// Listens on UDP (default port 9000) for the de-facto standard OSC tracker
// feed: /tracking/trackers/{1..8}/position + /rotation, plus
// /tracking/trackers/head/{position,rotation} for playspace alignment.
// SlimeVR ("OSC Trackers" output), Standable and various phone IMU apps all
// emit this, which gives SteamVR-driver-only tracker ecosystems a way into
// OCU where there is no vrserver for their driver to live in.
//
// Samples are stored raw in the sender's Unity convention (left-handed, y-up,
// meters, euler degrees); XrNetworkTracker converts to OpenXR space at read
// time. A background thread owns the socket; readers take a short mutex.
//
// Ini: [input] networkTrackers=true / networkTrackerPort=9000

#include <cstdint>
#include <mutex>
#include <thread>

struct NetTrackerSample {
	bool everSeen = false;
	// Raw as sent (Unity space): meters / euler degrees / meters-per-second
	float pos[3] = {};
	float eulerDeg[3] = {};
	float vel[3] = {}; // finite-difference between position packets
	uint64_t lastUpdateMs = 0; // NowMs() stamp of the last position packet
};

class NetworkTrackerReceiver {
public:
	static NetworkTrackerReceiver& Instance();

	static constexpr int MAX_TRACKERS = 8;

	// Idempotent; returns false if the socket could not be created/bound.
	bool Start(int port);
	// Signals the thread, closes the socket and joins (bounded wait).
	void Stop();

	// idx is 0-based (OSC addresses are 1-based). Returns false if never seen.
	bool GetTracker(int idx, NetTrackerSample& out);
	bool GetHead(NetTrackerSample& out);

	// Playspace alignment: called once per frame pump with the real HMD
	// position (OpenXR standing space). Keeps a slow EMA of
	// (real head - sender head) so sender-space tracker positions can be
	// translated into our playspace. No-op until head packets arrive.
	void UpdateAlignment(const float hmdPosStanding[3]);
	// Offset to ADD to a z-flipped (already right-handed) sender position.
	// Returns false (and zeros) when no head data has been received, in which
	// case positions are used as-is and the sender is assumed pre-aligned.
	bool GetAlignmentOffset(float out[3]);

	static uint64_t NowMs();

private:
	NetworkTrackerReceiver() = default;
	~NetworkTrackerReceiver();

	void ThreadLoop();
	void ParsePacket(const char* data, int len, int depth);
	void ParseMessage(const char* data, int len);
	void StoreSample(int slot, bool isRotation, const float xyz[3]);

	std::mutex mutex; // guards all sample/alignment state below
	std::thread thread;

	NetTrackerSample trackers[MAX_TRACKERS];
	NetTrackerSample head;

	bool alignValid = false;
	float alignOffset[3] = {};

	uintptr_t sock = ~(uintptr_t)0; // INVALID_SOCKET without pulling winsock into the header
	int boundPort = 0;
	bool running = false;
	bool loggedFirstPacket = false;
};
