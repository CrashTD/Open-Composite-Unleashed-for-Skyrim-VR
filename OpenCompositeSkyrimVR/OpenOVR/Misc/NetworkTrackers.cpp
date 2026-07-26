#include "stdafx.h"

#include "NetworkTrackers.h"

#include <chrono>
#include <cstring>

#ifdef WIN32
#include <winsock2.h>
#include <ws2tcpip.h>
#pragma comment(lib, "ws2_32.lib")
#endif

NetworkTrackerReceiver& NetworkTrackerReceiver::Instance()
{
	static NetworkTrackerReceiver instance;
	return instance;
}

NetworkTrackerReceiver::~NetworkTrackerReceiver()
{
	Stop();
}

uint64_t NetworkTrackerReceiver::NowMs()
{
	using namespace std::chrono;
	return (uint64_t)duration_cast<milliseconds>(steady_clock::now().time_since_epoch()).count();
}

bool NetworkTrackerReceiver::Start(int port)
{
#ifdef WIN32
	if (running)
		return true;

	WSADATA wsaData;
	if (WSAStartup(MAKEWORD(2, 2), &wsaData) != 0) {
		OOVR_LOG("Network trackers: WSAStartup failed");
		return false;
	}

	SOCKET s = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP);
	if (s == INVALID_SOCKET) {
		OOVR_LOGF("Network trackers: socket() failed (%d)", WSAGetLastError());
		return false;
	}

	sockaddr_in addr = {};
	addr.sin_family = AF_INET;
	addr.sin_addr.s_addr = INADDR_ANY;
	addr.sin_port = htons((u_short)port);
	if (bind(s, (sockaddr*)&addr, sizeof(addr)) == SOCKET_ERROR) {
		OOVR_LOGF("Network trackers: bind() on UDP %d failed (%d) — port in use?", port, WSAGetLastError());
		closesocket(s);
		return false;
	}

	// Timed recv so the thread notices the stop flag without needing a
	// socket close to unblock it (avoids teardown races).
	DWORD timeoutMs = 500;
	setsockopt(s, SOL_SOCKET, SO_RCVTIMEO, (const char*)&timeoutMs, sizeof(timeoutMs));

	sock = (uintptr_t)s;
	boundPort = port;
	running = true;
	thread = std::thread(&NetworkTrackerReceiver::ThreadLoop, this);
	return true;
#else
	return false;
#endif
}

void NetworkTrackerReceiver::Stop()
{
#ifdef WIN32
	if (!running)
		return;
	running = false;
	if ((SOCKET)sock != INVALID_SOCKET) {
		closesocket((SOCKET)sock);
		sock = (uintptr_t)INVALID_SOCKET;
	}
	if (thread.joinable())
		thread.join();
#endif
}

void NetworkTrackerReceiver::ThreadLoop()
{
#ifdef WIN32
	char buf[2048];
	while (running) {
		sockaddr_in from = {};
		int fromLen = sizeof(from);
		int len = recvfrom((SOCKET)sock, buf, sizeof(buf), 0, (sockaddr*)&from, &fromLen);
		if (len <= 0)
			continue; // timeout, socket closed, or malformed — just re-check running

		if (!loggedFirstPacket) {
			loggedFirstPacket = true;
			char ip[64] = "?";
			inet_ntop(AF_INET, &from.sin_addr, ip, sizeof(ip));
			OOVR_LOGF("Network trackers: first OSC packet received from %s (UDP %d)", ip, boundPort);
		}

		ParsePacket(buf, len, 0);
	}
#endif
}

// ---- OSC parsing -----------------------------------------------------------

static uint32_t ReadBigU32(const char* p)
{
	return ((uint32_t)(uint8_t)p[0] << 24) | ((uint32_t)(uint8_t)p[1] << 16)
	    | ((uint32_t)(uint8_t)p[2] << 8) | (uint32_t)(uint8_t)p[3];
}

static float ReadBigF32(const char* p)
{
	uint32_t u = ReadBigU32(p);
	float f;
	memcpy(&f, &u, sizeof(f));
	return f;
}

// Returns the padded length of an OSC string starting at data (multiple of 4),
// or -1 if unterminated within len.
static int OscStringLen(const char* data, int len)
{
	for (int i = 0; i < len; i++) {
		if (data[i] == '\0')
			return (i / 4 + 1) * 4;
	}
	return -1;
}

void NetworkTrackerReceiver::ParsePacket(const char* data, int len, int depth)
{
	if (depth > 4 || len < 8)
		return;

	// Bundle: "#bundle\0" + 8-byte timetag + (int32 size + element)*
	if (memcmp(data, "#bundle", 8) == 0) {
		int off = 16;
		while (off + 4 <= len) {
			int elemLen = (int)ReadBigU32(data + off);
			off += 4;
			if (elemLen <= 0 || off + elemLen > len)
				break;
			ParsePacket(data + off, elemLen, depth + 1);
			off += elemLen;
		}
		return;
	}

	ParseMessage(data, len);
}

void NetworkTrackerReceiver::ParseMessage(const char* data, int len)
{
	int addrLen = OscStringLen(data, len);
	if (addrLen < 0)
		return;
	const char* addr = data;

	// /tracking/trackers/<slot>/<position|rotation>, slot = 1..8 or "head"
	static const char PREFIX[] = "/tracking/trackers/";
	if (strncmp(addr, PREFIX, sizeof(PREFIX) - 1) != 0)
		return;
	const char* rest = addr + sizeof(PREFIX) - 1;

	int slot;
	if (strncmp(rest, "head/", 5) == 0) {
		slot = -1;
		rest += 5;
	} else if (rest[0] >= '1' && rest[0] <= '8' && rest[1] == '/') {
		slot = rest[0] - '1';
		rest += 2;
	} else {
		return;
	}

	bool isRotation;
	if (strcmp(rest, "position") == 0)
		isRotation = false;
	else if (strcmp(rest, "rotation") == 0)
		isRotation = true;
	else
		return;

	// Type tags: expect ",fff"
	const char* tags = data + addrLen;
	int tagsLen = OscStringLen(tags, len - addrLen);
	if (tagsLen < 0 || strncmp(tags, ",fff", 4) != 0)
		return;

	const char* args = tags + tagsLen;
	if (args + 12 > data + len)
		return;

	float xyz[3] = { ReadBigF32(args), ReadBigF32(args + 4), ReadBigF32(args + 8) };
	StoreSample(slot, isRotation, xyz);
}

void NetworkTrackerReceiver::StoreSample(int slot, bool isRotation, const float xyz[3])
{
	std::lock_guard<std::mutex> lock(mutex);
	NetTrackerSample& t = (slot < 0) ? head : trackers[slot];

	if (isRotation) {
		memcpy(t.eulerDeg, xyz, sizeof(t.eulerDeg));
		// Rotation-only senders (IMU trackers) still count as alive
		if (!t.everSeen) {
			t.everSeen = true;
			t.lastUpdateMs = NowMs();
		}
		return;
	}

	uint64_t now = NowMs();
	if (t.everSeen) {
		float dt = (now - t.lastUpdateMs) / 1000.0f;
		if (dt > 0.0f && dt < 0.5f) {
			for (int i = 0; i < 3; i++)
				t.vel[i] = (xyz[i] - t.pos[i]) / dt;
		} else {
			memset(t.vel, 0, sizeof(t.vel));
		}
	}
	memcpy(t.pos, xyz, sizeof(t.pos));
	t.everSeen = true;
	t.lastUpdateMs = now;
}

// ---- Reader API ------------------------------------------------------------

bool NetworkTrackerReceiver::GetTracker(int idx, NetTrackerSample& out)
{
	if (idx < 0 || idx >= MAX_TRACKERS)
		return false;
	std::lock_guard<std::mutex> lock(mutex);
	out = trackers[idx];
	return out.everSeen;
}

bool NetworkTrackerReceiver::GetHead(NetTrackerSample& out)
{
	std::lock_guard<std::mutex> lock(mutex);
	out = head;
	return out.everSeen;
}

void NetworkTrackerReceiver::UpdateAlignment(const float hmdPosStanding[3])
{
	std::lock_guard<std::mutex> lock(mutex);
	if (!head.everSeen || NowMs() - head.lastUpdateMs > 2000)
		return;

	// Sender head position, converted to right-handed (z flip) — the same
	// convention XrNetworkTracker uses for tracker positions.
	float target[3] = {
		hmdPosStanding[0] - head.pos[0],
		hmdPosStanding[1] - head.pos[1],
		hmdPosStanding[2] - (-head.pos[2]),
	};

	if (!alignValid) {
		memcpy(alignOffset, target, sizeof(alignOffset));
		alignValid = true;
		return;
	}

	// Slow EMA (~2s at 90Hz): the offset is near-constant since the sender's
	// head follows the real head; smoothing keeps HMD bob out of the trackers.
	const float alpha = 0.006f;
	for (int i = 0; i < 3; i++)
		alignOffset[i] += alpha * (target[i] - alignOffset[i]);
}

bool NetworkTrackerReceiver::GetAlignmentOffset(float out[3])
{
	std::lock_guard<std::mutex> lock(mutex);
	if (!alignValid) {
		out[0] = out[1] = out[2] = 0.0f;
		return false;
	}
	memcpy(out, alignOffset, 3 * sizeof(float));
	return true;
}
