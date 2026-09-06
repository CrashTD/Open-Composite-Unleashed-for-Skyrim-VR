#pragma once
#include "../Misc/FoveationRates.h"

#ifdef OC_HAS_NVAPI

#include <d3d11.h>
#include <vector>

class VRSManager {
public:
	struct EyeRegion {
		int left = 0;
		int top = 0;
		int width = 0;
		int height = 0;
	};

	VRSManager() = default;
	~VRSManager();

	// Initialize NVAPI and check VRS support. Returns true if VRS is available.
	bool Initialize(ID3D11Device* device);

	// Set projection centers for each eye (normalized 0-1 coordinates).
	// Call this once when eye projection data is available.
	void SetProjectionCenters(float leftProjX, float leftProjY, float rightProjX, float rightProjY);

	// Create/update one shading-rate resource for the full bound stereo render
	// target. NVIDIA requires this resource to match the complete render target
	// in 16x16 tiles; a one-eye resource is invalid for a side-by-side atlas.
	bool UpdateStereoPattern(int renderWidth, int renderHeight,
	    const EyeRegion& leftEye, const EyeRegion& rightEye, float innerRadius, float midRadius,
	    const ocu_foveation::RingRates& rates);

	// Apply the full stereo-atlas pattern before the game starts drawing a frame.
	bool ApplyStereo();

	// Disable VRS. Call before our own post-processing (FSR passes).
	void Disable();

	// Clean up all resources.
	void Shutdown();

	// Returns true if GPU supports VRS and initialization succeeded.
	bool IsAvailable() const { return available; }

	// Initialization is attempted at most once for a D3D device session. This
	// prevents unsupported adapters from retrying NVAPI every compositor frame.
	bool WasInitializationAttempted() const { return initializationAttempted; }

private:
	bool available = false;
	bool nvapiLoaded = false;
	bool initializationAttempted = false;

	ID3D11Device* device = nullptr;
	ID3D11DeviceContext* context = nullptr;

	// One resource matching the full stereo render target.
	ID3D11Texture2D* vrsTex = nullptr;
	void* vrsView = nullptr; // ID3D11NvShadingRateResourceView* — opaque to avoid nvapi.h in header
	int patternWidth = 0;
	int patternHeight = 0;
	int renderWidth = 0;
	int renderHeight = 0;
	EyeRegion eyeRegions[2];

	// Projection centers per eye
	float projX[2] = { 0.5f, 0.5f };
	float projY[2] = { 0.5f, 0.5f };
	bool patternDirty = true;

	// Cached config values used to detect changes
	float cachedInnerRadius = 0.0f;
	float cachedMidRadius = 0.0f;
	ocu_foveation::RingRates cachedRates;
	bool shadingRatesSet = false; // True after EnableShadingRates() — avoid redundant NVAPI calls

	// Generate a full-target pattern containing both eye regions.
	std::vector<uint8_t> CreateStereoPattern() const;

	// Set the shading rate table on the device context
	bool EnableShadingRates();

	// Create the full-target pattern texture and NVAPI resource view.
	void SetupStereoPattern();
	// Upload moving gaze centers without recreating the resource view.
	void UploadStereoPattern();

	void ReleasePatternResources();
};

#else // !OC_HAS_NVAPI

// Stub when NVAPI is not available — all methods are no-ops
class VRSManager {
public:
	struct EyeRegion {
		int left = 0;
		int top = 0;
		int width = 0;
		int height = 0;
	};

	bool Initialize(ID3D11Device*) { return false; }
	void SetProjectionCenters(float, float, float, float) {}
	bool UpdateStereoPattern(int, int, const EyeRegion&, const EyeRegion&, float, float,
	    const ocu_foveation::RingRates&) { return false; }
	bool ApplyStereo() { return false; }
	void Disable() {}
	void Shutdown() {}
	bool IsAvailable() const { return false; }
	bool WasInitializationAttempted() const { return true; }
};

#endif // OC_HAS_NVAPI
