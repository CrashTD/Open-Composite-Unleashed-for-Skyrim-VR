#pragma once

#include <d3d11.h>
#include "../Misc/FoveationRates.h"

// Cross-vendor radial-density masking for D3D11. Unlike hardware VRS, this
// backend reduces pixel-shader work by writing a sparse pattern into the
// scene depth buffer after it is cleared, then reconstructing the skipped
// color samples before OCU's compositor and temporal-upscaler passes.
class DensityMaskManager {
public:
	struct EyeRegion {
		int left = 0;
		int top = 0;
		int width = 0;
		int height = 0;
	};
	struct PatternSettings {
		float innerRadius = 0.60f;
		float midRadius = 0.80f;
		bool compatibilityMode = true;
		bool customEyeRates = false;
		ocu_foveation::RingRates rates;
	};
	void SetPatternSettings(const PatternSettings& settings) { patternSettings = settings; }

	DensityMaskManager() = default;
	~DensityMaskManager();

	bool Initialize(ID3D11Device* device);
	bool PrepareStereoTarget(ID3D11Texture2D* target, int renderWidth, int renderHeight,
	    const EyeRegion& leftEye, const EyeRegion& rightEye);
	void SetProjectionCenters(float leftX, float leftY, float rightX, float rightY);

	// Called immediately after the game's main depth buffer is cleared.
	bool ApplyDepthMask(ID3D11DepthStencilView* dsv, float clearDepth);

	// Reconstructs the sparse stereo color target. The returned texture is owned
	// by this manager and remains valid until the target changes or Shutdown().
	ID3D11Texture2D* ReconstructStereo(ID3D11Texture2D* source, int submittedEye);

	void BeginFrame();
	void EndFrameMasking();
	void Shutdown();

	bool IsAvailable() const { return available; }
	bool IsArmed() const { return armed; }
	bool WasMaskAppliedThisFrame() const { return maskAppliedThisFrame; }
	bool WasInitializationAttempted() const { return initializationAttempted; }

private:
	struct MaskConstants {
		float depthOut;
		float radius[3];
		float invClusterResolution[2];
		float projectionCenter[2];
		float eyeOrigin[2];
		float compatibilityMode;
		float padding;
		unsigned ringRates[4]; // inner/mid/outer stable Rate IDs, custom enabled
	};

	struct ReconstructConstants {
		float eyeOrigin[2];
		float eyeSize[2];
		float projectionCenter[2];
		float projectionPadding[2];
		float radius[3];
		float compatibilityMode;
		unsigned ringRates[4];
	};
	static_assert(sizeof(MaskConstants) % 16 == 0,
	    "Density-mask constants must obey D3D11 constant-buffer alignment");
	static_assert(sizeof(ReconstructConstants) % 16 == 0,
	    "Density-mask reconstruction constants must obey D3D11 constant-buffer alignment");

	bool CreateShadersAndStates();
	bool CreateColorResources(const D3D11_TEXTURE2D_DESC& sourceDesc);
	bool ValidateGeometry(int width, int height,
	    const EyeRegion& leftEye, const EyeRegion& rightEye) const;
	bool DrawMaskForEye(ID3D11DepthStencilView* dsv, int eye, float clearDepth);
	bool DrawReconstructionForEye(int eye);
	void ReleaseColorResources();

	bool available = false;
	bool initializationAttempted = false;
	bool armed = false;
	bool maskAppliedThisFrame = false;
	bool reconstructedThisFrame = false;
	bool unsupportedTargetLogged = false;
	PatternSettings patternSettings;

	ID3D11Device* device = nullptr;
	ID3D11DeviceContext* context = nullptr;
	ID3D11Texture2D* sceneTarget = nullptr; // observed game resource; not owned
	int renderWidth = 0;
	int renderHeight = 0;
	EyeRegion eyeRegions[2];
	float projX[2] = { 0.5f, 0.5f };
	float projY[2] = { 0.5f, 0.5f };

	ID3D11VertexShader* maskVS = nullptr;
	ID3D11VertexShader* reconstructVS = nullptr;
	ID3D11PixelShader* maskPS = nullptr;
	ID3D11PixelShader* reconstructPS = nullptr;
	ID3D11Buffer* maskCB = nullptr;
	ID3D11Buffer* reconstructCB = nullptr;
	ID3D11DepthStencilState* maskDepthState = nullptr;
	ID3D11DepthStencilState* noDepthState = nullptr;
	ID3D11RasterizerState* rasterizerState = nullptr;
	ID3D11SamplerState* samplerState = nullptr;

	ID3D11Texture2D* sourceCopy = nullptr;
	ID3D11ShaderResourceView* sourceSRV = nullptr;
	ID3D11Texture2D* reconstructed = nullptr;
	ID3D11RenderTargetView* reconstructedRTV = nullptr;
	DXGI_FORMAT resourceFormat = DXGI_FORMAT_UNKNOWN;
};
