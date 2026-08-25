OCU Keyboard Studio
===================

This separate desktop tool edits the real OpenComposite Unleashed keyboard.

Quick use
---------
1. Choose a complete keyboard from Keyboard Design, import a portable .ocukb,
   open a legacy .kb, or start from bundled Parchment, Dwemer, or Pug Dragon. Parchment remains the default. Open / Import,
   drag-and-drop, Save, and Save As register custom
   designs as self-contained managed projects in this dropdown for later
   sessions, and the Configurator reads the same list. When Studio is launched
   from an installed OCU folder, Save and Save As automatically apply the design
   to that same OCU root and set keyboard layout=auto. No folder selection is
   required.
2. Pick the exact OCU base theme, font, and Lower/Shift/Caps preview state. Add TTF
   converts a local TTF/OTF into OCU's SFN plus texture atlas automatically. It
   audits only normal/shift labels on keys that exist in the open keyboard. If
   one of those glyphs is missing, only that glyph is filled by OCU Nordic.
   Editing a key to use new unsupported text reports it and rebuilds the atlas.
   Base Theme is the built-in foundation: its fallback background/colors, plate
   treatment, Parchment ribbon eligibility, and theme-specific top-button offsets.
   A custom background and Use custom keyboard colors intentionally override most
   of its visible styling; the dropdown and status line now make that explicit.
   The first time Use custom keyboard colors is enabled, Studio copies the
   currently visible Base Theme colors into the editable swatches. Enabling it
   therefore does not replace a Dwemer, Parchment, or other template with the
   old green defaults.
3. Click the actually painted pixels of a letter/symbol to move that key's
   content, or click between/around glyphs on the empty plate to move the whole
   key. Plate handles resize from the exact dragged edge while the opposite edge
   stays anchored. While text is selected, the wheel resizes that font anywhere
   on the canvas. Select a plate, artwork, background, or empty canvas and the wheel
   zooms the entire keyboard around the pointer; middle-drag pans the zoomed view.
   No edit-mode switching is required. The text bar, PC/VR Mode button, and Lock button are independently
   selectable, draggable, and resizable; their laser hit areas follow their saved
   position and plate size. Their font scale is independent and the mouse wheel
   changes the selected top element's font size. Typing keys may be moved above
   or left of the original grid—negative X/Y positions are supported by Studio,
   the saved layout, native rendering, and laser hit testing.
   On the Key page, click Assigned key and press the real physical key you want.
   Studio translates its normal and Shift output from the active Windows keyboard
   layout and fills the matching key text automatically. Font text and assignment
   remain separately editable so special labels such as Ctrl and PrtSc stay clear.
   Undo and Redo sit at the lower-right beneath the preview, Ctrl+Z/Ctrl+Y work globally,
   and the bottom-right history counter shows both available stacks.
   Drag across empty preview space to marquee-select every visible key, top-bar
   item, settings control, and PNG the box intersects. Drag any outlined member
   to move the selected group together; Ctrl/Shift-drag adds more. A full-canvas
   background joins only when the selection box fully encloses it, so it cannot
   swallow normal marquee selections. Hidden plates are never selected. Pointer
   rendering is coalesced while dragging and inspector fields update on release,
   keeping dense custom keyboards responsive without lowering export quality.
   Grid Column Count is density: 14 makes larger cells than 15 because the fixed
   keyboard width is divided among fewer columns.
4. On Appearance, use independent color wheels for font, font outline, font
   glow, plate fill, plate outline, plate glow, and hover. Font and plate glows
   each have their own strength, radius, minimum intensity, period, phase, and
   breathing toggle. The readable font and plate outline stay stable while the
   selected halo breathes. Fill and outline have independent transparency,
   outline width is adjustable, plates can be hidden, and roundness runs from
   square to fully pill-shaped. Ordinary keys, the PC/VR Mode plus Lock buttons,
   and the input bar have independent plate-visibility switches. In Studio, an
   invisible plate is click-through and only its still-visible text/content can be
   selected. In game, hiding the artwork does not erase the key's assigned action.
5. On Artwork, place, resize, rotate, fade, pillbox, and change the opacity of a
   custom background. Corner roundness is proportional, so a pill's transparent
   mask readjusts whenever its width or height changes. Add any number of transparent PNG sprites, borders, or
   ribbons by drag/drop, clipboard paste, or the Add button. Drag artwork itself,
   drag corner/edge handles to resize it, and drag the circle above it to rotate.
   Right-click provides cut/copy/paste/delete and layer ordering. Each sprite may
   have an independently colored procedural glow that breathes without fading
   the PNG itself.
6. On Controls, select and resize the outer Size, Opacity, and Tilt boxes, then
   select their arrows, labels, or values as independent children. Built-in
   triangles have independent glow color, strength, radius, and breathing controls.
   Replace the Up triangle with any transparent PNG shape; OCU rotates the same
   image 180 degrees for Down and applies the same glow controls.
7. Save and Save As use .ocukb, the reusable OCU keyboard-project format. After
   a successful Save, Save disables until something really changes; use Save As
   when you intentionally want a second copy. One
   portable file contains the layout,
   background, every PNG sprite/ribbon, the custom arrow, and generated custom
   font files. Other users use Open / Import or drag the .ocukb onto Studio. The
   package is mod-manager-neutral: MO2, Vortex, and manual users import the same
   file, then Install for Next Launch deploys it through their normal OCU path.
   A .ocukb is a guarded ZIP container internally, but users should keep its
   .ocukb extension and trade it as one file. Legacy .kb files remain supported,
   but their artwork must stay beside them and their next Save converts them to
   .ocukb. Vortex does not need to recognize the custom extension itself; Studio
   imports the project and the Configurator performs the guarded deployment.
8. Export MO2 Mod creates an installable ZIP containing root\OCUKeyboard.kb,
   its PNG artwork, and the generated SFN/atlas when a converted font is active.
   The layout carries its exact theme, font, colors, and geometry without
   replacing opencomposite.ini. Send this ZIP to other users;
   they install it after OCU and can disable it to return to their own setup.
9. Install for Next Launch targets the exact OCU root passed by the Configurator.
   Studio falls back to the OCU installation containing its executable only when
   launched directly. A folder picker appears only for a standalone copy that
   genuinely cannot identify an OCU root. The confirmation names the exact file
   written. This is not a live reload: restart Skyrim VR to see the keyboard.
   MO2 keeps it persistent through Root Builder. Vortex and manual installs run
   the Configurator's guarded root synchronizer when Studio closes, copying the
   keyboard and artwork into the Skyrim VR game root without relying on MO2.

The preview and in-game runtime share the same 1024x560 geometry, visible-glyph
centering rule, theme/font selection, label offsets/scaling, custom colors,
font/outline glow and breathing, key
geometry, layered artwork, rotation, rounded-boundary edge fades, and nested
control geometry. Click empty control space for the resizable outer box; click
an arrow, label glyph, or value glyph to select and move that child alone. The
text bar and top buttons use the same saved rectangles for drawing and laser hits.
Artwork is scaled with premultiplied-alpha bilinear filtering instead of jagged
nearest-neighbor sampling. The native runtime caches that scaling and rotation
once when the keyboard opens; enabled breathing refreshes at a capped 20 Hz and
has no cost while the keyboard is closed.
OCU automatically discovers root\OCUKeyboard.kb. Restart Skyrim VR after
installing a different layout; remove that file or set layout=embedded to
restore OCU's built-in keyboard.

Sprites, ribbons, borders, and arrows must be transparent PNGs. JPEG is accepted
only for backgrounds after a warning and is converted to a real PNG during Save,
Install, .ocukb, and MO2 export. GIF/APNG frame animation is not supported; breathing is
a lightweight procedural effect applied by OCU to static artwork.

Shortcuts: Ctrl+O open, Ctrl+S save and apply, Ctrl+Shift+S save as and apply,
Ctrl+Z undo, Ctrl+Y redo, Ctrl+X/C/V cut/copy/paste selected PNG artwork or the
Parchment ribbon, and Delete removes the selected artwork/background or key.
