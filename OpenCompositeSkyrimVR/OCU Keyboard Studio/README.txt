OCU Keyboard Studio
===================

This separate desktop tool edits the real OpenComposite Unleashed keyboard.

Quick use
---------
1. Choose a complete keyboard from Keyboard Design, open another .kb, or start
   from the bundled Parchment design. Opening, Save, and Save As register custom
   designs as self-contained managed projects in this dropdown for later
   sessions, and the Configurator reads the same list. Choosing a design only
   previews it; Install to OCU or Configurator Save is the explicit step that
   activates it in game.
2. Pick the exact OCU base theme, font, and Lower/Shift/Caps preview state. Add TTF
   converts a local TTF/OTF into OCU's SFN plus texture atlas automatically. It
   audits only normal/shift labels on keys that exist in the open keyboard. If
   one of those glyphs is missing, only that glyph is filled by OCU Nordic.
   Editing a key to use new unsupported text reports it and rebuilds the atlas.
3. Click the actually painted pixels of a letter/symbol to move that key's
   content, or click between/around glyphs on the empty plate to move the whole
   key. Plate handles resize from the exact dragged edge while the opposite edge
   stays anchored; the wheel resizes selected content. No edit-mode switching is
   required. The text bar, PC/VR Mode button, and Lock button are independently
   selectable and draggable; their laser hit areas follow their saved positions.
   Undo and Redo are always on the main toolbar, Ctrl+Z/Ctrl+Y work globally,
   and the bottom-right history counter shows both available stacks.
4. On Appearance, use independent color wheels for font, font outline, font
   glow, plate fill, plate outline, plate glow, and hover. Font and plate glows
   each have their own strength, radius, minimum intensity, period, phase, and
   breathing toggle. The readable font and plate outline stay stable while the
   selected halo breathes. Fill and outline have independent transparency,
   outline width is adjustable, plates can be hidden, and roundness runs from
   square to fully pill-shaped. Ordinary keys, the PC/VR Mode plus Lock buttons,
   and the input bar have independent plate-visibility switches; hiding a plate
   never removes that control's laser hit area.
5. On Artwork, place, resize, rotate, fade, pillbox, and change the opacity of a
   custom background. Corner roundness is proportional, so a pill's transparent
   mask readjusts whenever its width or height changes. Add any number of transparent PNG sprites, borders, or
   ribbons by drag/drop, clipboard paste, or the Add button. Drag artwork itself,
   drag corner/edge handles to resize it, and drag the circle above it to rotate.
   Right-click provides cut/copy/paste/delete and layer ordering. Each sprite may
   have an independently colored procedural glow that breathes without fading
   the PNG itself.
6. On Controls, select and resize the outer Size, Opacity, and Tilt boxes, then
   select their arrows, labels, or values as independent children. Replace the
   Up triangle with any transparent PNG shape; OCU rotates the same image 180
   degrees for Down. The custom arrow can breathe too.
7. Save As creates a reusable keyboard project, copies its artwork beside it,
   and adds it to Keyboard Design automatically.
8. Export Shareable Mod creates an installable ZIP containing root\OCUKeyboard.kb,
   its PNG artwork, and the generated SFN/atlas when a converted font is active.
   The layout carries its exact theme, font, colors, and geometry without
   replacing opencomposite.ini. Send this ZIP to other users;
   they install it after OCU and can disable it to return to their own setup.
9. Install to OCU accepts the Skyrim VR game root, the active OCU MO2 mod, or
   that mod's root folder. Installing into the active MO2 mod keeps the custom
   keyboard persistent across Root Builder deployments.

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
Install, and MO2 export. GIF/APNG frame animation is not supported; breathing is
a lightweight procedural effect applied by OCU to static artwork.

Shortcuts: Ctrl+O open, Ctrl+S save, Ctrl+Shift+S save as, Ctrl+Z undo,
Ctrl+Y redo, Ctrl+X/C/V cut/copy/paste selected PNG artwork, and Delete removes
the selected artwork/background or key.
