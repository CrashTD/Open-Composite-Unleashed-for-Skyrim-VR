#pragma once
#include <map>
#include <cstdint>
#include <string>
#include <vector>

class KeyboardLayout {
public:
	struct VisualStyle {
		bool enabled = false;
		bool keyPlatesEnabled = true;
		bool topButtonPlatesEnabled = true;
		bool inputBarPlateEnabled = true;
		uint8_t fontColor[4] = { 237, 240, 245, 255 };
		uint8_t fontOutlineColor[4] = { 8, 11, 15, 220 };
		uint8_t fontGlowColor[4] = { 132, 242, 158, 255 };
		bool fontGlowEnabled = false;
		int fontGlowStrength = 45;
		int fontGlowRadius = 3;
		bool fontBreatheEnabled = false;
		int fontBreatheMinPercent = 35;
		float fontBreathePeriodSeconds = 2.0f;
		float fontBreathePhaseDegrees = 0.0f;
		uint8_t keyColor[4] = { 62, 190, 143, 205 };
		uint8_t plateFillColor[4] = { 22, 26, 33, 175 };
		int plateOutlineWidth = 2;
		uint8_t glowColor[4] = { 132, 242, 158, 255 };
		uint8_t hoverColor[4] = { 132, 242, 158, 255 };
		bool glowEnabled = true;
		int glowStrength = 45;
		int glowRadius = 4;
		bool hoverEnabled = true;
		int hoverStrength = 55;
		bool labelOutline = true;
		int keyRoundness = 14;
		bool keyBreatheEnabled = false;
		int keyBreatheMinPercent = 35;
		float keyBreathePeriodSeconds = 2.0f;
		float keyBreathePhaseDegrees = 0.0f;
	};

	struct ImageLayer {
		std::string file;
		float x = 0;
		float y = 0;
		float width = 1024;
		float height = 560;
		int opacity = 100;
		int edgeFade = 0;
		float rotation = 0;
		int roundness = 0;
		bool glowEnabled = false;
		uint8_t glowColor[4] = { 132, 242, 158, 255 };
		int glowStrength = 55;
		int glowRadius = 12;
		bool breatheEnabled = false;
		int breatheMinPercent = 35;
		float breathePeriodSeconds = 2.0f;
		float breathePhaseDegrees = 0.0f;
	};

	struct ControlDesign {
		float width = 92;
		float height = 98;
		float upOffsetX = 0;
		float upOffsetY = 0;
		float upWidth = 24;
		float upHeight = 20;
		float downOffsetX = 0;
		float downOffsetY = 0;
		float downWidth = 24;
		float downHeight = 20;
		float labelOffsetX = 0;
		float labelOffsetY = 0;
		float labelScale = 0.58f;
		float valueOffsetX = 0;
		float valueOffsetY = 0;
		float valueScale = 0.58f;
	};

	struct Key {
		int id;

		wchar_t ch, shift;
		float x, y;

		std::wstring label, labelShift;
		float w, h;
		// Per-key label placement authored by OCU Keyboard Studio. Offsets are
		// pixels in the 1024x560 keyboard texture; scale 1.0 is atlas-native.
		float labelOffsetX, labelOffsetY;
		float labelScale;

		// Is the key touching the right-most side of the keyboard?
		bool spansToRight;

		// The IDs of the keys on each side, and -1 indicates there is no key on that side
		int toLeft, toRight, toUp, toDown;
	};

	using keymap_t = std::vector<Key>;

	KeyboardLayout(std::vector<char>);
	~KeyboardLayout();

	const keymap_t& GetKeymap() const { return keys; }
	int GetWidth() const { return width; }
	const std::string& GetBaseTheme() const { return baseTheme; }
	const std::string& GetFontName() const { return fontName; }
	const VisualStyle& GetVisualStyle() const { return visualStyle; }
	const std::string& GetBackgroundFile() const { return background.file; }
	const ImageLayer& GetBackgroundLayer() const { return background; }
	const std::vector<ImageLayer>& GetSprites() const { return sprites; }
	float GetSizeControlOffsetX() const { return sizeControlOffsetX; }
	float GetSizeControlOffsetY() const { return sizeControlOffsetY; }
	float GetOpacityControlOffsetX() const { return opacityControlOffsetX; }
	float GetOpacityControlOffsetY() const { return opacityControlOffsetY; }
	float GetTiltControlOffsetX() const { return tiltControlOffsetX; }
	float GetTiltControlOffsetY() const { return tiltControlOffsetY; }
	float GetTextBarOffsetX() const { return textBarOffsetX; }
	float GetTextBarOffsetY() const { return textBarOffsetY; }
	float GetModeButtonOffsetX() const { return modeButtonOffsetX; }
	float GetModeButtonOffsetY() const { return modeButtonOffsetY; }
	float GetLockButtonOffsetX() const { return lockButtonOffsetX; }
	float GetLockButtonOffsetY() const { return lockButtonOffsetY; }
	const ControlDesign& GetSizeControlDesign() const { return sizeControlDesign; }
	const ControlDesign& GetOpacityControlDesign() const { return opacityControlDesign; }
	const ControlDesign& GetTiltControlDesign() const { return tiltControlDesign; }
	const std::string& GetControlArrowFile() const { return controlArrowFile; }
	float GetControlArrowRotation() const { return controlArrowRotation; }
	bool GetControlArrowBreatheEnabled() const { return controlArrowBreatheEnabled; }
	int GetControlArrowBreatheMinPercent() const { return controlArrowBreatheMinPercent; }
	float GetControlArrowBreathePeriodSeconds() const { return controlArrowBreathePeriodSeconds; }
	float GetControlArrowBreathePhaseDegrees() const { return controlArrowBreathePhaseDegrees; }
	bool HasBreathingEffects() const {
		if ((visualStyle.enabled && visualStyle.glowEnabled && visualStyle.keyBreatheEnabled)
		    || (visualStyle.enabled && visualStyle.fontGlowEnabled && visualStyle.fontBreatheEnabled)
		    || (!background.file.empty() && background.breatheEnabled)
		    || (!controlArrowFile.empty() && controlArrowBreatheEnabled))
			return true;
		for (const ImageLayer& sprite : sprites) {
			if (sprite.glowEnabled && sprite.breatheEnabled)
				return true;
		}
		return false;
	}
	const std::string& GetOverlayFile() const { return overlayFile; }
	float GetOverlayX() const { return overlayX; }
	float GetOverlayY() const { return overlayY; }
	float GetOverlayWidth() const { return overlayWidth; }
	float GetOverlayHeight() const { return overlayHeight; }
	int GetOverlayOpacity() const { return overlayOpacity; }

private:
	keymap_t keys;
	int width = 0;
	std::string baseTheme;
	std::string fontName;
	VisualStyle visualStyle;
	ImageLayer background;
	std::vector<ImageLayer> sprites;
	float sizeControlOffsetX = 0;
	float sizeControlOffsetY = 0;
	float opacityControlOffsetX = 0;
	float opacityControlOffsetY = 0;
	float tiltControlOffsetX = 0;
	float tiltControlOffsetY = 0;
	float textBarOffsetX = 0;
	float textBarOffsetY = 0;
	float modeButtonOffsetX = 0;
	float modeButtonOffsetY = 0;
	float lockButtonOffsetX = 0;
	float lockButtonOffsetY = 0;
	ControlDesign sizeControlDesign;
	ControlDesign opacityControlDesign;
	ControlDesign tiltControlDesign;
	std::string controlArrowFile;
	float controlArrowRotation = 0;
	bool controlArrowBreatheEnabled = false;
	int controlArrowBreatheMinPercent = 35;
	float controlArrowBreathePeriodSeconds = 2.0f;
	float controlArrowBreathePhaseDegrees = 0.0f;

	// Legacy v1 Studio overlay. It is migrated to sprites after parsing.
	std::string overlayFile;
	float overlayX = 120;
	float overlayY = 70;
	float overlayWidth = 784;
	float overlayHeight = 80;
	int overlayOpacity = 100;
};
