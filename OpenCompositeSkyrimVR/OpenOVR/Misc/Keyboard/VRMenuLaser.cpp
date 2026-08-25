#include "stdafx.h"

#include "VRMenuLaser.h"

#include <algorithm>
#include <d3d11.h>
#include <cmath>
#include <initializer_list>

#include "Reimpl/BaseInput.h"
#include "Reimpl/BaseSystem.h"
#include "Misc/LaserCalibration.h"
#include "LaserRaySmoothing.h"
#include "BeamTexture.h"
#include "generated/static_bases.gen.h"

#ifdef _WIN32
#pragma comment(lib, "d3d11.lib")
#endif

// ── Math helpers (same as VRKeyboard) ──

static inline float ml_dot(const XrVector3f& a, const XrVector3f& b)
{
	return a.x * b.x + a.y * b.y + a.z * b.z;
}

namespace {
	// The calibration surface is deliberately 16:9 because it is submitted on
	// the same OpenXR quad used for the game's in-world UI. A high-resolution
	// texture keeps the cell IDs readable in-headset; the old 16x16 solid fill
	// could prove the plane existed, but not whether U/V were flipped, scaled,
	// or offset relative to Scaleform's cursor space.
	// 2026-07-25: doubled resolution + 20x20 divisions (user request: 4x the
	// reference points once the quad became visible). Labels are now 4 digits:
	// column pair then row pair — "0007" = col 00, row 07. Fill is fully
	// transparent; only lines + labels render (the solid green obstructed the
	// menu behind it).
	constexpr int kDebugGridWidth = 2048;
	constexpr int kDebugGridHeight = 1152;
	constexpr int kDebugGridDivisions = 20;

	bool IsBgraFormat(DXGI_FORMAT format)
	{
		return format == DXGI_FORMAT_B8G8R8A8_UNORM ||
		       format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
	}

	bool IsSrgbFormat(DXGI_FORMAT format)
	{
		return format == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB ||
		       format == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB;
	}

	const char* LaserFormatName(DXGI_FORMAT format)
	{
		switch (format) {
		case DXGI_FORMAT_R8G8B8A8_UNORM: return "R8G8B8A8_UNORM";
		case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB: return "R8G8B8A8_UNORM_SRGB";
		case DXGI_FORMAT_B8G8R8A8_UNORM: return "B8G8R8A8_UNORM";
		case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB: return "B8G8R8A8_UNORM_SRGB";
		default: return "OTHER";
		}
	}

	DXGI_FORMAT PickSupportedFormat(const std::vector<int64_t>& supported,
	    std::initializer_list<DXGI_FORMAT> preferences)
	{
		for (DXGI_FORMAT candidate : preferences) {
			if (std::find(supported.begin(), supported.end(), static_cast<int64_t>(candidate)) != supported.end())
				return candidate;
		}
		return DXGI_FORMAT_UNKNOWN;
	}

	std::string DescribeFormats(const std::vector<int64_t>& formats)
	{
		std::string result;
		for (int64_t raw : formats) {
			if (!result.empty()) result += ", ";
			DXGI_FORMAT format = static_cast<DXGI_FORMAT>(raw);
			result += LaserFormatName(format);
			result += "(" + std::to_string(static_cast<long long>(raw)) + ")";
		}
		return result;
	}

	uint32_t PackColor(DXGI_FORMAT format, uint8_t r, uint8_t g, uint8_t b, uint8_t a)
	{
		if (IsBgraFormat(format))
			std::swap(r, b);
		return static_cast<uint32_t>(r) |
		       (static_cast<uint32_t>(g) << 8) |
		       (static_cast<uint32_t>(b) << 16) |
		       (static_cast<uint32_t>(a) << 24);
	}

	uint8_t LinearToSrgbByte(float linear)
	{
		linear = std::clamp(linear, 0.0f, 1.0f);
		float srgb = linear <= 0.0031308f
		    ? linear * 12.92f
		    : 1.055f * std::pow(linear, 1.0f / 2.4f) - 0.055f;
		return static_cast<uint8_t>(std::lround(srgb * 255.0f));
	}

	// Five-bit-wide, seven-row digits. Cell labels are colcol/rowrow: 0000 is
	// top-left, 1900 is top-right, 0019 is bottom-left, 1919 is bottom-right.
	constexpr uint8_t kDigitGlyphs[10][7] = {
		{ 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E }, // 0
		{ 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E }, // 1
		{ 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F }, // 2
		{ 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E }, // 3
		{ 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 }, // 4
		{ 0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E }, // 5
		{ 0x0E, 0x10, 0x10, 0x1E, 0x11, 0x11, 0x0E }, // 6
		{ 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 }, // 7
		{ 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E }, // 8
		{ 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x01, 0x0E }  // 9
	};
}

// ── VRMenuLaser implementation ──

VRMenuLaser::VRMenuLaser(ID3D11Device* dev)
    : dev(dev)
{
	dev->GetImmediateContext(&ctx);

	// OpenXR swapchain formats are runtime-specific. Preserve OCU's existing
	// formats whenever the active runtime advertises them, then fall back between
	// RGBA/BGRA and sRGB/linear. PimaxXR, VDXR, and Oculus therefore keep their
	// current path while SteamVR can use the formats it actually exposes.
	uint32_t formatCount = 0;
	OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainFormats(xr_session.get(), 0, &formatCount, nullptr));
	std::vector<int64_t> runtimeFormats(formatCount);
	OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainFormats(
	    xr_session.get(), formatCount, &formatCount, runtimeFormats.data()));

	DXGI_FORMAT laserColorFormat = PickSupportedFormat(runtimeFormats, {
	    DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
	    DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
	    DXGI_FORMAT_R8G8B8A8_UNORM,
	    DXGI_FORMAT_B8G8R8A8_UNORM,
	});
	debugQuadFormat = PickSupportedFormat(runtimeFormats, {
	    DXGI_FORMAT_R8G8B8A8_UNORM,
	    DXGI_FORMAT_B8G8R8A8_UNORM,
	    DXGI_FORMAT_R8G8B8A8_UNORM_SRGB,
	    DXGI_FORMAT_B8G8R8A8_UNORM_SRGB,
	});
	std::string offeredFormats = DescribeFormats(runtimeFormats);
	if (laserColorFormat == DXGI_FORMAT_UNKNOWN || debugQuadFormat == DXGI_FORMAT_UNKNOWN) {
		OOVR_ABORTF("Menu laser requires a supported 8-bit RGBA/BGRA swapchain format. Runtime offered: %s",
		    offeredFormats.c_str());
	}
	OOVR_LOGF("Menu laser swapchain formats: offered=[%s] beam/dot=%s(%d) calibration=%s(%d)",
	    offeredFormats.c_str(), LaserFormatName(laserColorFormat), static_cast<int>(laserColorFormat),
	    LaserFormatName(debugQuadFormat), static_cast<int>(debugQuadFormat));

	// Create active-idle, active-clicked, and matching standby color states. Both hand
	// composition layers can legally reference the same released swapchain image
	// in one frame, so the standby state adds no per-frame texture work.
	for (int state = 0; state < 3; state++) {
		XrSwapchainCreateInfo sci = { XR_TYPE_SWAPCHAIN_CREATE_INFO };
		sci.usageFlags = XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;
		sci.format = static_cast<int64_t>(laserColorFormat);
		sci.sampleCount = 1;
		sci.width = beamtex::kW;
		sci.height = beamtex::kH;
		sci.faceCount = 1;
		sci.arraySize = 1;
		sci.mipCount = 1;

		OOVR_FAILED_XR_ABORT(xrCreateSwapchain(xr_session.get(), &sci, &beamChain[state]));

		uint32_t imgCount = 0;
		OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainImages(beamChain[state], 0, &imgCount, nullptr));
		std::vector<XrSwapchainImageD3D11KHR> imgs(imgCount, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
		OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainImages(beamChain[state], imgCount, &imgCount,
		    (XrSwapchainImageBaseHeader*)imgs.data()));

		std::vector<uint32_t> colorPixels;
		uint8_t r = state == 1 ? 55 : 255;
		uint8_t g = state == 1 ? 145 : 240;
		uint8_t b = state == 1 ? 255 : 220;
		uint8_t a = state == 1 ? 220 : 200;
		if (IsBgraFormat(laserColorFormat))
			std::swap(r, b);
		beamtex::Fill(colorPixels, r, g, b, a);

		D3D11_TEXTURE2D_DESC td = {};
		td.Width = beamtex::kW;
		td.Height = beamtex::kH;
		td.MipLevels = 1;
		td.ArraySize = 1;
		td.Format = laserColorFormat;
		td.SampleDesc = { 1, 0 };
		td.Usage = D3D11_USAGE_DEFAULT;

		D3D11_SUBRESOURCE_DATA init = { colorPixels.data(), sizeof(uint32_t) * beamtex::kW, sizeof(uint32_t) * beamtex::kW * beamtex::kH };
		CComPtr<ID3D11Texture2D> tex;
		OOVR_FAILED_DX_ABORT(dev->CreateTexture2D(&td, &init, &tex));

		XrSwapchainImageAcquireInfo acq = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
		uint32_t idx = 0;
		OOVR_FAILED_XR_ABORT(xrAcquireSwapchainImage(beamChain[state], &acq, &idx));
		XrSwapchainImageWaitInfo wait = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
		wait.timeout = 500000000;
		OOVR_FAILED_XR_ABORT(xrWaitSwapchainImage(beamChain[state], &wait));
		ctx->CopyResource(imgs[idx].texture, tex);
		XrSwapchainImageReleaseInfo rel = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
		OOVR_FAILED_XR_ABORT(xrReleaseSwapchainImage(beamChain[state], &rel));
	}

	for (int i = 0; i < 2; i++) {
		memset(&beamLayer[i], 0, sizeof(beamLayer[i]));
		beamLayer[i].type = XR_TYPE_COMPOSITION_LAYER_QUAD;
		beamLayer[i].layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
		beamLayer[i].space = xr_gbl->floorSpace;
		beamLayer[i].eyeVisibility = XR_EYE_VISIBILITY_BOTH;
		beamLayer[i].subImage.swapchain = beamChain[0];
		beamLayer[i].subImage.imageRect.offset = { 0, 0 };
		beamLayer[i].subImage.imageRect.extent = { beamtex::kW, beamtex::kH };
		beamLayer[i].subImage.imageArrayIndex = 0;
	}

	// Create matching active-idle, active-clicked, and standby dot textures.
	for (int state = 0; state < 3; state++) {
		XrSwapchainCreateInfo sci = { XR_TYPE_SWAPCHAIN_CREATE_INFO };
		sci.usageFlags = XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;
		sci.format = static_cast<int64_t>(laserColorFormat);
		sci.sampleCount = 1;
		sci.width = 8;
		sci.height = 8;
		sci.faceCount = 1;
		sci.arraySize = 1;
		sci.mipCount = 1;

		OOVR_FAILED_XR_ABORT(xrCreateSwapchain(xr_session.get(), &sci, &dotChain[state]));

		uint32_t imgCount = 0;
		OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainImages(dotChain[state], 0, &imgCount, nullptr));
		std::vector<XrSwapchainImageD3D11KHR> imgs(imgCount, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
		OOVR_FAILED_XR_ABORT(xrEnumerateSwapchainImages(dotChain[state], imgCount, &imgCount,
		    (XrSwapchainImageBaseHeader*)imgs.data()));

		uint8_t cr = state == 1 ? 55 : 255;
		uint8_t cg = state == 1 ? 145 : 240;
		uint8_t cb = state == 1 ? 255 : 220;
		uint8_t ca = 255;
		uint32_t packed = PackColor(laserColorFormat, cr, cg, cb, ca);
		uint32_t dotPixels[64];
		for (int py = 0; py < 8; py++) {
			for (int px = 0; px < 8; px++) {
				float dx = px - 3.5f, dy = py - 3.5f;
				dotPixels[py * 8 + px] = (dx * dx + dy * dy <= 12.25f) ? packed : 0;
			}
		}

		D3D11_TEXTURE2D_DESC td = {};
		td.Width = 8;
		td.Height = 8;
		td.MipLevels = 1;
		td.ArraySize = 1;
		td.Format = laserColorFormat;
		td.SampleDesc = { 1, 0 };
		td.Usage = D3D11_USAGE_DEFAULT;

		D3D11_SUBRESOURCE_DATA init = { dotPixels, sizeof(uint32_t) * 8, sizeof(uint32_t) * 64 };
		CComPtr<ID3D11Texture2D> tex;
		OOVR_FAILED_DX_ABORT(dev->CreateTexture2D(&td, &init, &tex));

		XrSwapchainImageAcquireInfo acq = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
		uint32_t idx = 0;
		OOVR_FAILED_XR_ABORT(xrAcquireSwapchainImage(dotChain[state], &acq, &idx));
		XrSwapchainImageWaitInfo wait = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
		wait.timeout = 500000000;
		OOVR_FAILED_XR_ABORT(xrWaitSwapchainImage(dotChain[state], &wait));
		ctx->CopyResource(imgs[idx].texture, tex);
		XrSwapchainImageReleaseInfo rel = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
		OOVR_FAILED_XR_ABORT(xrReleaseSwapchainImage(dotChain[state], &rel));
	}

	for (int i = 0; i < 2; i++) {
		memset(&dotLayer[i], 0, sizeof(dotLayer[i]));
		dotLayer[i].type = XR_TYPE_COMPOSITION_LAYER_QUAD;
		dotLayer[i].layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
		dotLayer[i].space = xr_gbl->floorSpace;
		dotLayer[i].eyeVisibility = XR_EYE_VISIBILITY_BOTH;
		dotLayer[i].subImage.swapchain = dotChain[0];
		dotLayer[i].subImage.imageRect.offset = { 0, 0 };
		dotLayer[i].subImage.imageRect.extent = { 8, 8 };
		dotLayer[i].subImage.imageArrayIndex = 0;
	}

	OOVR_LOG("Menu laser swapchains: 6 shared active/clicked/standby beam/dot textures; calibration grid is lazy");
}

VRMenuLaser::~VRMenuLaser()
{
	for (int state = 0; state < 3; state++) {
		if (beamChain[state] != XR_NULL_HANDLE)
			xrDestroySwapchain(beamChain[state]);
		if (dotChain[state] != XR_NULL_HANDLE)
			xrDestroySwapchain(dotChain[state]);
	}
	DestroyDebugQuad();
	if (ctx)
		ctx->Release();
}

void VRMenuLaser::SetShowDebugQuad(bool show)
{
	if (showDebugQuad == show)
		return;
	showDebugQuad = show;
	debugCreationFailed = false;
	if (!show)
		DestroyDebugQuad();
}

bool VRMenuLaser::EnsureDebugQuad()
{
	if (debugQuadChain != XR_NULL_HANDLE)
		return true;
	if (debugCreationFailed)
		return false;

	XrSwapchainCreateInfo sci = { XR_TYPE_SWAPCHAIN_CREATE_INFO };
	sci.usageFlags = XR_SWAPCHAIN_USAGE_TRANSFER_DST_BIT | XR_SWAPCHAIN_USAGE_SAMPLED_BIT | XR_SWAPCHAIN_USAGE_COLOR_ATTACHMENT_BIT;
	sci.format = static_cast<int64_t>(debugQuadFormat);
	sci.sampleCount = 1;
	sci.width = kDebugGridWidth;
	sci.height = kDebugGridHeight;
	sci.faceCount = 1;
	sci.arraySize = 1;
	sci.mipCount = 1;

	XrResult result = xrCreateSwapchain(xr_session.get(), &sci, &debugQuadChain);
	if (XR_FAILED(result)) {
		debugQuadChain = XR_NULL_HANDLE;
		debugCreationFailed = true;
		OOVR_LOGF("Menu laser calibration grid unavailable: xrCreateSwapchain=%d", result);
		return false;
	}

	uint32_t imgCount = 0;
	result = xrEnumerateSwapchainImages(debugQuadChain, 0, &imgCount, nullptr);
	if (XR_FAILED(result) || imgCount == 0) {
		OOVR_LOGF("Menu laser calibration grid unavailable: image count result=%d count=%u", result, imgCount);
		DestroyDebugQuad();
		debugCreationFailed = true;
		return false;
	}
	debugSwapImages.assign(imgCount, { XR_TYPE_SWAPCHAIN_IMAGE_D3D11_KHR });
	result = xrEnumerateSwapchainImages(debugQuadChain, imgCount, &imgCount,
	    reinterpret_cast<XrSwapchainImageBaseHeader*>(debugSwapImages.data()));
	if (XR_FAILED(result)) {
		OOVR_LOGF("Menu laser calibration grid unavailable: enumerate images=%d", result);
		DestroyDebugQuad();
		debugCreationFailed = true;
		return false;
	}

	memset(&debugQuadLayer, 0, sizeof(debugQuadLayer));
	debugQuadLayer.type = XR_TYPE_COMPOSITION_LAYER_QUAD;
	debugQuadLayer.layerFlags = XR_COMPOSITION_LAYER_BLEND_TEXTURE_SOURCE_ALPHA_BIT;
	debugQuadLayer.space = xr_gbl->floorSpace;
	debugQuadLayer.eyeVisibility = XR_EYE_VISIBILITY_BOTH;
	debugQuadLayer.subImage.swapchain = debugQuadChain;
	debugQuadLayer.subImage.imageRect.offset = { 0, 0 };
	debugQuadLayer.subImage.imageRect.extent = { kDebugGridWidth, kDebugGridHeight };
	debugQuadLayer.subImage.imageArrayIndex = 0;
	debugLastBakedOpacity = -1;
	debugCreationFailed = false;
	RebakeDebugQuadTexture(debugOpacityPercent);
	OOVR_LOG("Menu laser calibration grid swapchain created on demand");
	return true;
}

void VRMenuLaser::DestroyDebugQuad()
{
	if (debugQuadChain != XR_NULL_HANDLE)
		xrDestroySwapchain(debugQuadChain);
	debugQuadChain = XR_NULL_HANDLE;
	debugSwapImages.clear();
	memset(&debugQuadLayer, 0, sizeof(debugQuadLayer));
	debugLastBakedOpacity = -1;
}

void VRMenuLaser::RebakeDebugQuadTexture(int opacityPercent)
{
	if (debugQuadChain == XR_NULL_HANDLE || debugSwapImages.empty())
		return;
	if (opacityPercent == debugLastBakedOpacity)
		return;
	debugLastBakedOpacity = opacityPercent;

	// Premultiplied alpha. Linear UNORM keeps the historical appearance; if the
	// runtime only exposes sRGB, encode premultiplied RGB so composition decodes
	// it back to the same linear values. Alpha itself remains linear.
	float alphaF = opacityPercent / 100.0f;
	// 2026-07-25: fill is fully transparent — lines and labels only. The solid
	// color fill obstructed the menu once the quad actually became visible.
	// Grid: 40% brighter than the configured color, clamped to 255
	int bri = (int)(debugColorR * 1.4f); if (bri > 255) bri = 255;
	int bgi = (int)(debugColorG * 1.4f); if (bgi > 255) bgi = 255;
	int bbi = (int)(debugColorB * 1.4f); if (bbi > 255) bbi = 255;
	uint32_t fillColor = 0; // premultiplied transparent black

	std::vector<uint32_t> pixels(kDebugGridWidth * kDebugGridHeight, fillColor);
	auto setPixel = [&](int x, int y, uint32_t color) {
		if (x >= 0 && x < kDebugGridWidth && y >= 0 && y < kDebugGridHeight)
			pixels[y * kDebugGridWidth + x] = color;
	};
	auto premultipliedColor = [&](uint8_t r, uint8_t g, uint8_t b, float a) {
		a = std::clamp(a, 0.0f, 1.0f);
		uint8_t pr;
		uint8_t pg;
		uint8_t pb;
		if (IsSrgbFormat(debugQuadFormat)) {
			pr = LinearToSrgbByte((r / 255.0f) * a);
			pg = LinearToSrgbByte((g / 255.0f) * a);
			pb = LinearToSrgbByte((b / 255.0f) * a);
		} else {
			pr = static_cast<uint8_t>(r * a);
			pg = static_cast<uint8_t>(g * a);
			pb = static_cast<uint8_t>(b * a);
		}
		return PackColor(debugQuadFormat, pr, pg, pb,
		    static_cast<uint8_t>(a * 255.0f));
	};

	const float gridAF = std::min(1.0f, alphaF + 0.25f);
	const float majorAF = std::min(1.0f, alphaF + 0.50f);
	const float labelAF = std::min(1.0f, alphaF + 0.65f);
	const uint32_t gridColor = premultipliedColor(
	    (uint8_t)bri, (uint8_t)bgi, (uint8_t)bbi, gridAF);
	const uint32_t majorColor = premultipliedColor(255, 255, 255, majorAF);
	const uint32_t labelColor = premultipliedColor(255, 255, 255, labelAF);
	const uint32_t topEdgeColor = premultipliedColor(255, 70, 50, majorAF);
	const uint32_t leftEdgeColor = premultipliedColor(60, 150, 255, majorAF);

	auto drawVertical = [&](int x, int thickness, uint32_t color) {
		for (int dx = -(thickness / 2); dx <= thickness / 2; ++dx)
			for (int y = 0; y < kDebugGridHeight; ++y)
				setPixel(x + dx, y, color);
	};
	auto drawHorizontal = [&](int y, int thickness, uint32_t color) {
		for (int dy = -(thickness / 2); dy <= thickness / 2; ++dy)
			for (int x = 0; x < kDebugGridWidth; ++x)
				setPixel(x, y + dy, color);
	};

	// Ten-by-ten UV grid. The 50% axes and outer border are heavier.
	for (int i = 0; i <= kDebugGridDivisions; ++i) {
		int x = (int)lroundf((float)i * (kDebugGridWidth - 1) / kDebugGridDivisions);
		int y = (int)lroundf((float)i * (kDebugGridHeight - 1) / kDebugGridDivisions);
		bool major = (i == 0 || i == kDebugGridDivisions / 2 || i == kDebugGridDivisions);
		drawVertical(x, major ? 3 : 1, major ? majorColor : gridColor);
		drawHorizontal(y, major ? 3 : 1, major ? majorColor : gridColor);
	}

	// Direction keys make a flipped export immediately obvious in-headset:
	// red is V=0/top, blue is U=0/left.
	drawHorizontal(1, 3, topEdgeColor);
	drawVertical(1, 3, leftEdgeColor);

	auto drawDigit = [&](int digit, int originX, int originY, int scale) {
		for (int gy = 0; gy < 7; ++gy) {
			uint8_t row = kDigitGlyphs[digit][gy];
			for (int gx = 0; gx < 5; ++gx) {
				if ((row & (1u << (4 - gx))) == 0)
					continue;
				for (int sy = 0; sy < scale; ++sy)
					for (int sx = 0; sx < scale; ++sx)
						setPixel(originX + gx * scale + sx, originY + gy * scale + sy, labelColor);
			}
		}
	};

	// Label every cell with its zero-based address: column pair, then row pair
	// ("0107" = col 01, row 07). This lets a tester say "laser dot is in 0107,
	// game cursor is in 0209" without guessing pixels.
	const int cellW = kDebugGridWidth / kDebugGridDivisions;
	const int cellH = kDebugGridHeight / kDebugGridDivisions;
	constexpr int glyphScale = 3;
	constexpr int glyphWidth = 5 * glyphScale;
	constexpr int glyphGap = 1 * glyphScale;
	constexpr int pairGap = 3 * glyphScale; // wider gap between col pair and row pair
	constexpr int labelWidth = glyphWidth * 4 + glyphGap * 2 + pairGap;
	constexpr int labelHeight = 7 * glyphScale;
	for (int cellY = 0; cellY < kDebugGridDivisions; ++cellY) {
		for (int cellX = 0; cellX < kDebugGridDivisions; ++cellX) {
			int x0 = cellX * cellW + (cellW - labelWidth) / 2;
			int y0 = cellY * cellH + (cellH - labelHeight) / 2;
			int x = x0;
			drawDigit(cellX / 10, x, y0, glyphScale); x += glyphWidth + glyphGap;
			drawDigit(cellX % 10, x, y0, glyphScale); x += glyphWidth + pairGap;
			drawDigit(cellY / 10, x, y0, glyphScale); x += glyphWidth + glyphGap;
			drawDigit(cellY % 10, x, y0, glyphScale);
		}
	}

	D3D11_TEXTURE2D_DESC td = {};
	td.Width = kDebugGridWidth;
	td.Height = kDebugGridHeight;
	td.MipLevels = 1;
	td.ArraySize = 1;
	td.Format = debugQuadFormat;
	td.SampleDesc = { 1, 0 };
	td.Usage = D3D11_USAGE_DEFAULT;

	D3D11_SUBRESOURCE_DATA init = {
		pixels.data(),
		sizeof(uint32_t) * kDebugGridWidth,
		sizeof(uint32_t) * kDebugGridWidth * kDebugGridHeight
	};
	CComPtr<ID3D11Texture2D> tex;
	HRESULT hr = dev->CreateTexture2D(&td, &init, &tex);
	if (FAILED(hr)) return;

	XrSwapchainImageAcquireInfo acq = { XR_TYPE_SWAPCHAIN_IMAGE_ACQUIRE_INFO };
	uint32_t idx = 0;
	if (XR_FAILED(xrAcquireSwapchainImage(debugQuadChain, &acq, &idx))) return;
	XrSwapchainImageWaitInfo wait = { XR_TYPE_SWAPCHAIN_IMAGE_WAIT_INFO };
	wait.timeout = 500000000;
	if (XR_FAILED(xrWaitSwapchainImage(debugQuadChain, &wait))) return;
	ctx->CopyResource(debugSwapImages[idx].texture, tex);
	XrSwapchainImageReleaseInfo rel = { XR_TYPE_SWAPCHAIN_IMAGE_RELEASE_INFO };
	xrReleaseSwapchainImage(debugQuadChain, &rel);
}

void VRMenuLaser::SetMenuQuad(XrPosef pose, XrExtent2Df size)
{
	menuPose = pose;
	menuSize = size;
	menuValid = true;
}

bool VRMenuLaser::RayIntersectQuad(const XrVector3f& origin, const XrVector3f& dir,
    float& u, float& v, float& t)
{
	if (!menuValid)
		return false;

	XrVector3f planeNormal, localRight, localUp;
	rotate_vector_by_quaternion({ 0, 0, 1 }, menuPose.orientation, planeNormal);
	rotate_vector_by_quaternion({ 1, 0, 0 }, menuPose.orientation, localRight);
	rotate_vector_by_quaternion({ 0, 1, 0 }, menuPose.orientation, localUp);

	float denom = ml_dot(dir, planeNormal);
	if (fabsf(denom) < 1e-6f)
		return false;

	XrVector3f PO = {
		menuPose.position.x - origin.x,
		menuPose.position.y - origin.y,
		menuPose.position.z - origin.z
	};
	t = ml_dot(PO, planeNormal) / denom;
	if (t <= 0.0f)
		return false;

	XrVector3f hitPoint = {
		origin.x + t * dir.x,
		origin.y + t * dir.y,
		origin.z + t * dir.z
	};

	XrVector3f HP = {
		hitPoint.x - menuPose.position.x,
		hitPoint.y - menuPose.position.y,
		hitPoint.z - menuPose.position.z
	};
	float localX = ml_dot(HP, localRight);
	float localY = ml_dot(HP, localUp);

	u = (localX + menuSize.width * 0.5f) / menuSize.width;
	v = (localY + menuSize.height * 0.5f) / menuSize.height;

	if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f)
		return false;

	return true;
}

XrQuaternionf VRMenuLaser::BeamOrientation(const XrVector3f& beamDir,
    const XrVector3f& midpoint, const XrVector3f& viewerPos)
{
	XrVector3f up = beamDir;

	XrVector3f toViewer = {
		viewerPos.x - midpoint.x,
		viewerPos.y - midpoint.y,
		viewerPos.z - midpoint.z
	};
	float ml = sqrtf(toViewer.x * toViewer.x + toViewer.y * toViewer.y + toViewer.z * toViewer.z);
	XrVector3f fwd;
	if (ml > 0.001f)
		fwd = { toViewer.x / ml, toViewer.y / ml, toViewer.z / ml };
	else
		fwd = { 0, 0, 1 };

	XrVector3f right = {
		up.y * fwd.z - up.z * fwd.y,
		up.z * fwd.x - up.x * fwd.z,
		up.x * fwd.y - up.y * fwd.x
	};
	float rl = sqrtf(right.x * right.x + right.y * right.y + right.z * right.z);
	if (rl < 0.001f) {
		right = { 1, 0, 0 };
		rl = 1.0f;
	}
	right.x /= rl;
	right.y /= rl;
	right.z /= rl;

	fwd = {
		right.y * up.z - right.z * up.y,
		right.z * up.x - right.x * up.z,
		right.x * up.y - right.y * up.x
	};

	float trace = right.x + up.y + fwd.z;
	XrQuaternionf q;

	if (trace > 0) {
		float s = 0.5f / sqrtf(trace + 1.0f);
		q.w = 0.25f / s;
		q.x = (up.z - fwd.y) * s;
		q.y = (fwd.x - right.z) * s;
		q.z = (right.y - up.x) * s;
	} else if (right.x > up.y && right.x > fwd.z) {
		float s = 2.0f * sqrtf(1.0f + right.x - up.y - fwd.z);
		q.w = (up.z - fwd.y) / s;
		q.x = 0.25f * s;
		q.y = (up.x + right.y) / s;
		q.z = (fwd.x + right.z) / s;
	} else if (up.y > fwd.z) {
		float s = 2.0f * sqrtf(1.0f + up.y - right.x - fwd.z);
		q.w = (fwd.x - right.z) / s;
		q.x = (up.x + right.y) / s;
		q.y = 0.25f * s;
		q.z = (fwd.y + up.z) / s;
	} else {
		float s = 2.0f * sqrtf(1.0f + fwd.z - right.x - up.y);
		q.w = (right.y - up.x) / s;
		q.x = (fwd.x + right.z) / s;
		q.y = (fwd.y + up.z) / s;
		q.z = 0.25f * s;
	}

	return q;
}

void VRMenuLaser::UpdateBeam(int side, const XrVector3f& origin, const XrVector3f& dir,
    float beamLen, const XrVector3f& headPos)
{
	XrVector3f beamEnd = {
		origin.x + dir.x * beamLen,
		origin.y + dir.y * beamLen,
		origin.z + dir.z * beamLen
	};

	XrVector3f mid = {
		(origin.x + beamEnd.x) * 0.5f,
		(origin.y + beamEnd.y) * 0.5f,
		(origin.z + beamEnd.z) * 0.5f
	};

	beamLayer[side].pose.position = mid;
	// MapMenu is farther away and visually busier than a flat UI panel. Its
	// compositor-owned compatibility beam replaces the old five-copy Skyrim
	// mesh treatment with one clean 6mm layer.
	beamLayer[side].size.width = mapVisualMode ? 0.006f : 0.003f;
	beamLayer[side].size.height = beamLen;
	beamLayer[side].pose.orientation = BeamOrientation(dir, mid, headPos);
}

void VRMenuLaser::UpdateDot(int side, const XrVector3f& hitPoint)
{
	// Place a small dot at the hit point on the menu surface, slightly in front
	XrVector3f planeNormal;
	rotate_vector_by_quaternion({ 0, 0, 1 }, menuPose.orientation, planeNormal);

	dotLayer[side].pose.position = {
		hitPoint.x + planeNormal.x * 0.001f, // 1mm in front to avoid z-fighting
		hitPoint.y + planeNormal.y * 0.001f,
		hitPoint.z + planeNormal.z * 0.001f
	};
	dotLayer[side].pose.orientation = menuPose.orientation;
	// The map is visually busy and farther away than flat inventory panels.
	// Give its endpoint enough size to remain obvious without obscuring icons.
	float dotSize = mapVisualMode ? 0.018f : 0.012f;
	dotLayer[side].size.width = dotSize;
	dotLayer[side].size.height = dotSize;
}

void VRMenuLaser::UpdateWorldDot(int side, const XrVector3f& hitPoint,
    const XrQuaternionf& headOrientation, const XrVector3f& rayDir)
{
	// Pull the marker 3mm toward the controller so it cannot disappear inside
	// the collision surface. Matching the HMD orientation makes the circular
	// quad face the viewer anywhere in the full 360-degree scene.
	dotLayer[side].pose.position = {
		hitPoint.x - rayDir.x * 0.003f,
		hitPoint.y - rayDir.y * 0.003f,
		hitPoint.z - rayDir.z * 0.003f
	};
	dotLayer[side].pose.orientation = headOrientation;
	float dotSize = mapVisualMode ? 0.018f : 0.015f;
	dotLayer[side].size.width = dotSize;
	dotLayer[side].size.height = dotSize;
}

const std::vector<XrCompositionLayerBaseHeader*>& VRMenuLaser::Update(
    XrTime predictedTime, const bool keyboardHitSide[2])
{
	activeLayers.clear();
	for (int side = 0; side < 2; ++side) {
		rayValid[side] = false;
		hitActive[side] = false;
	}
	XrSpace appSpace = xr_space_from_ref_space_type(GetUnsafeBaseSystem()->currentSpace);

	if (!menuValid) {
		for (int side = 0; side < 2; ++side)
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
		return activeLayers;
	}

	// Use the exact reference space selected by the game for its projection
	// layer. The exported RoomNode-local menu mesh, controller rays, dot, beam,
	// and debug quad must all be expressed in this same space.
	for (int side = 0; side < 2; ++side) {
		beamLayer[side].space = appSpace;
		dotLayer[side].space = appSpace;
	}

	// Debug quad — show the menu hit area as a semi-transparent overlay
	if (showDebugQuad && EnsureDebugQuad()) {
		debugQuadLayer.space = appSpace;
		RebakeDebugQuadTexture(debugOpacityPercent);
		debugQuadLayer.pose = menuPose;
		debugQuadLayer.size = menuSize;
		activeLayers.push_back((XrCompositionLayerBaseHeader*)&debugQuadLayer);
	}

	// Get head position for beam billboard orientation
	XrSpaceLocation headLoc = { XR_TYPE_SPACE_LOCATION };
	xrLocateSpace(xr_gbl->viewSpace, appSpace, predictedTime, &headLoc);
	XrVector3f headPos = headLoc.pose.position;

	// Get input system for controller poses
	std::shared_ptr<BaseInput> input = GetBaseInput();
	if (!input || !input->AreActionsLoaded()) {
		for (int side = 0; side < 2; ++side)
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
		return activeLayers;
	}

	// Get controller states for trigger tracking
	BaseSystem* sys = GetUnsafeBaseSystem();

	// Short default beam when not hitting the menu — just enough to
	// show pointing direction.  When the ray hits the menu surface
	// the beam extends exactly to the dot (stops at the menu).
	constexpr float DEFAULT_BEAM = 0.5f; // meters (visual hint only)

	for (int side = 0; side < 2; side++) {
		// Save previous trigger/thumbstick/X-button state
		triggerLast[side] = triggerState[side];
		thumbstickLast[side] = thumbstickState[side];
		xBtnLast[side] = xBtnState[side];
		appMenuLast[side] = appMenuState[side];
		hitActive[side] = false;
		rayValid[side] = false;

		// Skip this hand if keyboard has it
		if (keyboardHitSide[side]) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		// Get controller aim space
		XrSpace aimSpace = XR_NULL_HANDLE;
		input->GetHandSpace((vr::TrackedDeviceIndex_t)(side + 1), aimSpace, true);
		if (aimSpace == XR_NULL_HANDLE) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		XrSpaceLocation location = { XR_TYPE_SPACE_LOCATION };
		XrResult result = xrLocateSpace(aimSpace, appSpace, predictedTime, &location);
		if (XR_FAILED(result) || !(location.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) || !(location.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT)) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		XrVector3f rayOrigin = location.pose.position;
		float originDown = oovr_laser_calibration::OriginDown(side);
		if (originDown != 0.0f) {
			XrVector3f downWorld;
			rotate_vector_by_quaternion({ 0.0f, -1.0f, 0.0f }, location.pose.orientation, downWorld);
			rayOrigin.x += downWorld.x * originDown;
			rayOrigin.y += downWorld.y * originDown;
			rayOrigin.z += downWorld.z * originDown;
		}
		XrVector3f fwd = oovr_laser_calibration::LocalForward(side);
		XrVector3f rayDir;
		rotate_vector_by_quaternion(fwd, location.pose.orientation, rayDir);
		if (!oovr_laser_smoothing::Filter(
		        oovr_laser_smoothing::Consumer::Menu, side, appSpace,
		        predictedTime, rayOrigin, rayDir))
			continue;
		rayValid[side] = true;

		// Store ray data for calibration/diagnostics
		lastRayOrigin[side] = rayOrigin;
		lastRayDir[side] = rayDir;

		// Flat menus intersect the exported Scaleform quad. MapMenu instead uses
		// Skyrim's native depth-resolved distance, which already follows terrain
		// relief and floating icons, on this live OpenXR controller ray.
		float u = 0.0f, v = 0.0f, t = -1.0f;
		bool hit = false;
		bool visualSurfaceHit = false;
		XrVector3f hitPoint = {};
		if (mapVisualMode) {
			if (mapVisualDistanceValid && std::isfinite(mapVisualDistanceMeters) &&
			    mapVisualDistanceMeters > 0.03f && mapVisualDistanceMeters < 12.0f) {
				t = mapVisualDistanceMeters;
				hitPoint = {
					rayOrigin.x + t * rayDir.x,
					rayOrigin.y + t * rayDir.y,
					rayOrigin.z + t * rayDir.z
				};
				hit = true;
				visualSurfaceHit = true;
			}
		} else {
			hit = RayIntersectQuad(rayOrigin, rayDir, u, v, t);
			visualSurfaceHit = hit;
			if (hit) {
				hitPoint = {
					rayOrigin.x + t * rayDir.x,
					rayOrigin.y + t * rayDir.y,
					rayOrigin.z + t * rayDir.z
				};
			}
		}
		lastRayDir[side] = rayDir;

		bool drawDot = false;
		if (visualSurfaceHit) {
			lastHitT[side] = t;
			hitActive[side] = true;
			if (!mapVisualMode) {
				hitU[side] = u;
				// Flip V so 0=top, 1=bottom (Scaleform convention: 0,0 is top-left)
				hitV[side] = 1.0f - v;
			}

			if (renderHand[side]) {
				if (mapVisualMode)
					UpdateWorldDot(side, hitPoint, headLoc.pose.orientation, rayDir);
				else
					UpdateDot(side, hitPoint);
				drawDot = true;
			}
		}

		// Flat menus use OCU's shaft. MapMenu uses the same owned shaft only while
		// Skyrim has published a valid depth-resolved distance. Because its direction
		// remains the live OpenXR controller ray, no RoomNode endpoint conversion can
		// produce the old vertical beam.
		// Hidden hands still track hits/trigger above so they can claim the
		// pointer, but draw nothing.
		if (renderHand[side]) {
			if (!mapVisualMode || visualSurfaceHit) {
				float beamLen = visualSurfaceHit ? t : DEFAULT_BEAM;
				UpdateBeam(side, rayOrigin, rayDir, beamLen, headPos);
				activeLayers.push_back((XrCompositionLayerBaseHeader*)&beamLayer[side]);
			}
			// Submit the dot after the beam so it remains visible on top of the
			// shaft and Skyrim's busy map art.
			if (drawDot)
				activeLayers.push_back((XrCompositionLayerBaseHeader*)&dotLayer[side]);
		}

		// Track trigger state with hysteresis to prevent bouncing
		if (sys) {
			vr::TrackedDeviceIndex_t devIdx = sys->GetTrackedDeviceIndexForControllerRole(
			    side == 0 ? vr::TrackedControllerRole_LeftHand : vr::TrackedControllerRole_RightHand);
			if (devIdx != vr::k_unTrackedDeviceIndexInvalid) {
				vr::VRControllerState_t ctrlState = {};
				sys->GetUnmaskedControllerState(devIdx, &ctrlState, sizeof(ctrlState));
				float trigVal = ctrlState.rAxis[1].x;
				bool btnPressed = (ctrlState.ulButtonPressed & vr::ButtonMaskFromId(vr::k_EButton_SteamVR_Trigger)) != 0;
				// Hysteresis: press at 0.7, release at 0.3 (prevents bouncing near threshold)
				if (triggerState[side])
					triggerState[side] = (trigVal >= 0.3f) || btnPressed;
				else
					triggerState[side] = (trigVal >= 0.7f) || btnPressed;

				// Thumbstick click — simple digital button for calibration
				bool stickClick = (ctrlState.ulButtonPressed & vr::ButtonMaskFromId(vr::k_EButton_SteamVR_Touchpad)) != 0;
				thumbstickState[side] = stickClick;

				// Thumbstick axis values for in-VR quad adjustment
				thumbstickX[side] = ctrlState.rAxis[0].x;
				thumbstickY[side] = ctrlState.rAxis[0].y;

				// X/A button — for calibration depth probes
				bool xBtn = (ctrlState.ulButtonPressed & vr::ButtonMaskFromId(vr::k_EButton_A)) != 0;
				xBtnState[side] = xBtn;

				// ApplicationMenu button (Y on left, B on right) — for tab cycling
				bool appMenu = (ctrlState.ulButtonPressed & vr::ButtonMaskFromId(vr::k_EButton_ApplicationMenu)) != 0;
				appMenuState[side] = appMenu;
			}
		}

		// Composition layers are submitted after Update returns, so switching the
		// selected pre-baked swapchain here affects this same frame.
		int colorState = side == activeHand ? (triggerState[side] ? 1 : 0) : 2;
		beamLayer[side].subImage.swapchain = beamChain[colorState];
		dotLayer[side].subImage.swapchain = dotChain[colorState];
	}

	return activeLayers;
}

const std::vector<XrCompositionLayerBaseHeader*>& VRMenuLaser::UpdateWorld(
    XrTime predictedTime, const bool keyboardHitSide[2],
    const bool worldHitSide[2], const float worldHitDistanceMeters[2])
{
	activeLayers.clear();

	XrSpace appSpace = xr_space_from_ref_space_type(GetUnsafeBaseSystem()->currentSpace);
	for (int side = 0; side < 2; ++side) {
		rayValid[side] = false;
		hitActive[side] = false;
	}
	for (int side = 0; side < 2; ++side) {
		beamLayer[side].space = appSpace;
		dotLayer[side].space = appSpace;
	}

	XrSpaceLocation headLoc = { XR_TYPE_SPACE_LOCATION };
	XrResult headResult = xrLocateSpace(xr_gbl->viewSpace, appSpace, predictedTime, &headLoc);
	if (XR_FAILED(headResult) ||
	    !(headLoc.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) ||
	    !(headLoc.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT)) {
		for (int side = 0; side < 2; ++side)
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
		return activeLayers;
	}

	std::shared_ptr<BaseInput> input = GetBaseInput();
	if (!input || !input->AreActionsLoaded()) {
		for (int side = 0; side < 2; ++side)
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
		return activeLayers;
	}
	BaseSystem* sys = GetUnsafeBaseSystem();
	constexpr float kWorldNoHitBeam = 20.0f;
	constexpr float kMaxWorldHit = 120.0f;

	for (int side = 0; side < 2; ++side) {
		triggerLast[side] = triggerState[side];
		thumbstickLast[side] = thumbstickState[side];
		xBtnLast[side] = xBtnState[side];
		appMenuLast[side] = appMenuState[side];
		hitActive[side] = false;
		rayValid[side] = false;

		// Always sample the unmasked state, even over the keyboard. That prevents
		// a trigger held while typing from becoming a false fresh world click when
		// the hand leaves the keyboard surface.
		if (sys) {
			vr::TrackedDeviceIndex_t devIdx = sys->GetTrackedDeviceIndexForControllerRole(
			    side == 0 ? vr::TrackedControllerRole_LeftHand : vr::TrackedControllerRole_RightHand);
			if (devIdx != vr::k_unTrackedDeviceIndexInvalid) {
				vr::VRControllerState_t ctrlState = {};
				if (sys->GetUnmaskedControllerState(devIdx, &ctrlState, sizeof(ctrlState))) {
					float trigVal = ctrlState.rAxis[1].x;
					bool btnPressed = (ctrlState.ulButtonPressed &
					    vr::ButtonMaskFromId(vr::k_EButton_SteamVR_Trigger)) != 0;
					if (triggerState[side])
						triggerState[side] = (trigVal >= 0.3f) || btnPressed;
					else
						triggerState[side] = (trigVal >= 0.7f) || btnPressed;
				}
			}
		}

		int colorState = triggerState[side] ? 1 : 0;
		beamLayer[side].subImage.swapchain = beamChain[colorState];
		dotLayer[side].subImage.swapchain = dotChain[colorState];

		if (keyboardHitSide[side]) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		XrSpace aimSpace = XR_NULL_HANDLE;
		input->GetHandSpace((vr::TrackedDeviceIndex_t)(side + 1), aimSpace, true);
		if (aimSpace == XR_NULL_HANDLE) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		XrSpaceLocation location = { XR_TYPE_SPACE_LOCATION };
		XrResult result = xrLocateSpace(aimSpace, appSpace, predictedTime, &location);
		if (XR_FAILED(result) ||
		    !(location.locationFlags & XR_SPACE_LOCATION_POSITION_VALID_BIT) ||
		    !(location.locationFlags & XR_SPACE_LOCATION_ORIENTATION_VALID_BIT)) {
			oovr_laser_smoothing::Reset(
			    oovr_laser_smoothing::Consumer::Menu, side, appSpace);
			continue;
		}

		XrVector3f rayOrigin = location.pose.position;
		float originDown = oovr_laser_calibration::OriginDown(side);
		if (originDown != 0.0f) {
			XrVector3f downWorld;
			rotate_vector_by_quaternion({ 0.0f, -1.0f, 0.0f },
			    location.pose.orientation, downWorld);
			rayOrigin.x += downWorld.x * originDown;
			rayOrigin.y += downWorld.y * originDown;
			rayOrigin.z += downWorld.z * originDown;
		}
		XrVector3f localForward = oovr_laser_calibration::LocalForward(side);
		XrVector3f rayDir;
		rotate_vector_by_quaternion(localForward, location.pose.orientation, rayDir);
		if (!oovr_laser_smoothing::Filter(
		        oovr_laser_smoothing::Consumer::Menu, side, appSpace,
		        predictedTime, rayOrigin, rayDir))
			continue;

		rayValid[side] = true;
		lastRayOrigin[side] = rayOrigin;
		lastRayDir[side] = rayDir;

		float beamLength = kWorldNoHitBeam;
		bool hit = worldHitSide[side] &&
		    std::isfinite(worldHitDistanceMeters[side]) &&
		    worldHitDistanceMeters[side] > 0.01f &&
		    worldHitDistanceMeters[side] <= kMaxWorldHit;
		if (hit) {
			beamLength = worldHitDistanceMeters[side];
			lastHitT[side] = beamLength;
			hitActive[side] = true;
			XrVector3f hitPoint = {
				rayOrigin.x + rayDir.x * beamLength,
				rayOrigin.y + rayDir.y * beamLength,
				rayOrigin.z + rayDir.z * beamLength
			};
			UpdateWorldDot(side, hitPoint, headLoc.pose.orientation, rayDir);
			activeLayers.push_back((XrCompositionLayerBaseHeader*)&dotLayer[side]);
		}

		UpdateBeam(side, rayOrigin, rayDir, beamLength, headLoc.pose.position);
		activeLayers.push_back((XrCompositionLayerBaseHeader*)&beamLayer[side]);
	}

	return activeLayers;
}
