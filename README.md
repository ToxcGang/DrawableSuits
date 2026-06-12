# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits in-game, place image decals, save and load designs, and apply edited textures to vanilla or modded suits.

## Features

- Default third-person paint editor: opening DrawableSuits switches to an editor camera around the local player so you can paint directly on the visible suit.
- Imperium-inspired terminal overlay with red/dark panels, generated asset-backed Paint, Erase, Fill, Decal, Text, Sticker, Eyedropper, and Mirror icon buttons, brush shape selector, brush/fill sliders, a hue/SV color picker, persistent recent color swatches, decal/text/sticker controls, design name, Decals/Stickers/Saved Designs menus, share-code import/export, Undo, Redo, a selectable labeled undo history panel, Reset, Apply, Save, and Close.
- The editor always edits the local player's currently worn suit. Manual cross-suit selection is not available in the editor.
- Fill Bucket flood-fills contiguous matching texture regions under the cursor using the current brush color, opacity, and Fill Tolerance slider.
- Brush shapes for Paint and Erase: Circle, Square, Pixel, Spray Paint, Soft Airbrush, and Noise/Scatter.
- Mirror painting duplicates Paint, Erase, Fill, Decal, Text, and Sticker edits onto the opposite suit surface using the editor's baked avatar mesh, without adding keyboard or controller shortcuts.
- Decal placement preview: Decal mode shows a translucent live preview before stamping, then places one projected decal per click or right-trigger press in third-person mode.
- Text stamping: Text mode previews typed single-line text on the suit as a transparent alpha-mask stamp, then bakes it into the texture once per click or right-trigger press. In third-person mode, text is projected onto the visible suit surface instead of stamped as a flat UV rectangle.
- Sticker stamping: Sticker mode previews built-in transparent shape masks such as Star, Heart, Arrow, Ring, and Shield, then bakes one tinted sticker into the suit texture per click or right-trigger press.
- Pause-menu entry point: use the `DrawableSuits` button below Resume.
- Fallback shortcuts: `F8` on keyboard or `View/Back + Y` on controller.
- Emergency open shortcut: `F10`, which opens the editor and does not toggle it closed.
- Controller support: left stick moves the editor cursor, `A` clicks exactly the UI control under the cursor, right trigger paints only, right stick/bumpers orbit the camera, D-pad up/down zooms, `Y` cycles tools, `X` undoes, and Start saves.
- Direct surface painting: the editor bakes a hidden mesh collider from the local player model and paints by raycasting from the third-person camera to suit UV coordinates.
- Always-visible UV texture panel: while third-person editing is active, the right-column texture panel stays visible and editable without toggling views. Texture-only fallback is still available for diagnostics or third-person setup failures.
- PNG/JPG decals from `BepInEx/config/DrawableSuits/Decals`. The Decals menu `Add Decal` button launches an external Windows image picker process, copies the selected PNG/JPG/JPEG into the Decals folder, refreshes the rows, and loads the imported decal.
- Reusable saved designs stored as JSON metadata plus PNG texture files.
- Compact lossless `DSUIT2:` design codes for copy/paste import and export between profiles or players, with legacy `DSUIT1:` import compatibility.
- Apply/save multiplayer sync for other players who also have DrawableSuits installed, keyed per player so two players wearing the same suit can have different edits.
- Vanilla and modded suit support as long as the suit exposes a normal suit material and texture.

## Install

1. Install BepInExPack for Lethal Company.
2. Build this project with `dotnet build -c Release`.
3. Copy `dist/DrawableSuits/BepInEx/plugins/DrawableSuits/DrawableSuits.dll` into your mod profile, or package the `dist/DrawableSuits` folder for Thunderstore.

## Controls

Keyboard and mouse:

- Pause menu `DrawableSuits`: primary open path.
- `F8`: toggle editor.
- `F10`: emergency open.
- Left mouse: paint/erase continuously; in Fill mode, fill once per press; in Decal, Text, or Sticker mode, stamp one preview at the cursor location.
- Brush Shape: choose Circle, Square, Pixel, Spray Paint, Soft Airbrush, or Noise/Scatter from the Brush dropdown. Pixel ignores the brush size slider and edits one texture pixel.
- Tool icons: Paint, Erase, Fill, Decal, Text, Sticker, Eyedropper, and Mirror are selected with compact red icon buttons. The active tool label names the current icon selection, and the icons are embedded generated PNG masks tinted by the terminal UI.
- Recent Colors: click a recent swatch below the color picker to restore that brush color. Colors are added only after Paint, Fill, Text, or Sticker successfully writes onto the suit.
- Fill: click the `Fill` UI button, adjust Fill Tolerance if needed, then left-click a contiguous color region on the suit.
- Text: click the `Text` UI button, type up to 64 characters, then left-click the suit to stamp it. Text uses the current brush color and opacity.
- Decal: click the `Decal` UI button, open the `Decals` menu to choose a decal, then left-click the suit to stamp it.
- Sticker: click the `Sticker` UI button, open the `Stickers` menu to choose a built-in shape, then left-click the suit to stamp it. Stickers use the current brush color and opacity.
- Eyedropper: click the `Eyedropper` UI button, then left-click the suit to sample that texture color. It returns to the previous tool after one successful sample.
- Mirror: click the `Mirror` UI button to duplicate paint, erase, fill, decal stamps, text stamps, and sticker stamps onto the opposite suit surface.
- Export Code / Import Code: use the design code panel to copy the current editable texture as a compact `DSUIT2:` code or paste a shared `DSUIT2:` or legacy `DSUIT1:` code into the current suit.
- Saved Designs: open the `Designs` menu to select and load saved designs into the current suit.
- Undo History: click a history row to select it, then press `Undo Selected` to undo only that labeled action where possible. Use Undo/Redo to step one action at a time.
- UV texture panel: move the cursor over the right-column texture panel to paint, erase, fill, stamp decals/text/stickers, or sample colors directly on the UV layout while the third-person view remains active.
- Right mouse: orbit the third-person editor camera.
- Mouse wheel: zoom the third-person camera.
- Ctrl + mouse wheel: change brush size.
- Escape or Close: close the editor.

Controller:

- `View/Back + Y`: open or close.
- Left stick: move the editor cursor.
- `A`: click the button, field, slider, or color picker region directly under the cursor.
- Tool icons: move the virtual cursor over a red icon button and press `A`; the active tool label confirms the selected tool.
- Text: move the virtual cursor over the `Text` UI button and press `A`, type text with the UI field, then aim at the suit and press right trigger once to stamp it.
- Decal: move the virtual cursor over the `Decal` UI button and press `A`, open `Decals`, choose a decal row with `A`, then aim at the suit and press right trigger once to stamp it.
- Sticker: move the virtual cursor over the `Sticker` UI button and press `A`, open `Stickers`, choose a built-in sticker shape with `A`, then aim at the suit and press right trigger once to stamp it.
- Fill: move the virtual cursor over the `Fill` UI button and press `A`, adjust Fill Tolerance if needed, then aim at a matching color region and press right trigger once.
- Brush Shape: move the virtual cursor over the Brush Shape dropdown and press `A`, then pick a shape with `A`.
- Recent Colors: move the virtual cursor over a recent color swatch and press `A` to restore that brush color.
- Eyedropper: move the virtual cursor over the `Eyedropper` UI button, press `A`, then aim at the suit and press right trigger once to sample a color. It returns to the previous tool after one successful sample.
- Mirror: move the virtual cursor over the `Mirror` UI button and press `A`; there is no controller shortcut for this modifier.
- Export Code / Import Code: use `A` on the design code UI buttons. There are no shortcuts for import/export.
- Saved Designs: move the virtual cursor over `Designs`, press `A`, select a saved design row, then press `Load Selected`.
- Undo History: move the virtual cursor over a history row and press `A` to select it, then press `A` on `Undo Selected` to undo only that action where possible. Use `X` or the Undo/Redo buttons for one-step history.
- Right trigger: paint/erase continuously; in Fill mode, fill once per press; in Decal, Text, or Sticker mode, stamp one preview at the cursor location.
- Right stick or bumpers: orbit the third-person editor camera.
- D-pad up/down: zoom the third-person editor camera.
- `Y`: cycle Paint/Erase/Decal. Fill, Text, Sticker, Eyedropper, Mirror, and Brush Shape are UI-only and are not part of this shortcut.
- `X`: undo.
- Start: save.

## Folders

DrawableSuits creates these folders after launch:

- `BepInEx/config/DrawableSuits/Saves` stores `.json` design metadata.
- `BepInEx/config/DrawableSuits/Textures` stores saved design `.png` textures.
- `BepInEx/config/DrawableSuits/Decals` stores user decal images.
- `BepInEx/config/DrawableSuits/Logs` stores the diagnostics log.

Use the in-game `Decals` menu `Add Decal` button to copy a `.png`, `.jpg`, or `.jpeg` into `Decals`. The picker runs in a separate PowerShell process so the game never calls the Windows common dialog in-process. Manual folder placement still works: put image files in `Decals`, open the `Decals` menu, and press `Refresh`.

## Multiplayer

Edited suits update locally while painting. Other mod users receive your player-specific texture only when you press `Apply` or `Save`. Texture payloads are chunked through Unity Netcode custom messages with the owner client ID and validated with a hash before being applied.

Players without DrawableSuits can still join normally, but they will see the original suit textures.

DrawableSuits no longer replaces every rack/player using the same base suit when one player edits their suit. Saved designs are reusable, but active in-session edits are applied to the selected player only.

Share codes embed a PNG copy of the current editable texture plus metadata. Importing a code loads it into the local player's current suit editor texture only. It creates one undo entry, does not auto-save, and does not sync to multiplayer until you press `Apply` or `Save`.

New exports use the shorter lossless `DSUIT2:` format. Older `DSUIT1:` codes still import normally, but newly copied codes default to `DSUIT2:`.

## Modded Suits

DrawableSuits works with modded suits by detecting unlockables that expose a `suitMaterial`. Saved designs are reusable on any suit, but loading a design onto a suit with a different UV layout can stretch or misplace drawings and decals.

DrawableSuits is not compatible with ModelReplacementAPI. Replacement models can use separate runtime renderers and materials that DrawableSuits cannot safely map to the current suit texture, so the third-person editor may show incorrect geometry, duplicate helmets, or an uneditable model. If you use ModelReplacementAPI, use the UV texture panel where possible or disable the replacement while editing.

## Configuration

The BepInEx config file controls:

- Open editor key.
- Emergency diagnostics overlay key.
- Debug HUD toggle key.
- Startup diagnostics HUD enable/disable and display duration.
- Controller cursor speed.
- Max editable texture size.
- Undo history size.
- Multiplayer sync enable/disable.
- Max sync payload size and chunk size.
- `StartInUvFallbackMode`, disabled by default, opens directly into texture-only UV panel mode for diagnostics.
- `ThirdPersonCameraDistance`, the default third-person editor camera distance.
- `RecentColors`, a comma-separated list of up to 12 recent `#RRGGBB` brush colors populated only after Paint, Fill, Text, or Sticker writes to the suit.
- `ApplyLocalFirstPersonArms`, disabled by default, is experimental and allows edited materials on local first-person arms/body outside the editor.
- `AutoDisableBrokenJetpackWarningLateUpdatePatch`, enabled by default, disables only the broken JetpackWarning `PlayerControllerB.LateUpdate` postfix after repeated null-reference errors are detected.
- `EnableExperimentalModelPreview`, disabled by default, keeps the old RenderTexture model preview as diagnostics only.
- `EnableOsFileDialog` is a legacy ignored setting. The Decals menu `Add Decal` button uses an external Windows picker process instead.

## Debugging

DrawableSuits writes detailed startup, pause-menu, input, editor, camera, collider, raycast, suit-detection, list-row, and paint logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.

When testing with Gale, also search `BepInEx/LogOutput.log` in the active Gale profile for `DrawableSuits`.

Expected 0.5.54 behavior:

- Opening the editor shows an Imperium-inspired red/dark terminal overlay and a third-person camera view of the local player.
- Paint, Erase, Fill, Decal, Text, Sticker, Eyedropper, and Mirror use embedded generated PNG icon masks instead of large text tool buttons or procedural mesh glyphs. The active tool label names the current icon, and Mirror remains a UI-only modifier.
- Paint, Fill, and Eyedropper icons should read clearly as a paint brush, paint bucket, and eyedropper at the in-game toolbar size.
- The editor edits only the local player's currently worn suit. The old Previous, Use Current, and Next suit-selection buttons are not present.
- The diagnostics text should show `Preview mode: WorldThirdPerson` when the default path succeeds.
- The UV texture panel is visible at the same time as the third-person suit and can be edited directly by moving the cursor over it.
- The visible editor model is `DrawableSuitsWorldAvatarProxy`, a baked suit/body proxy on an isolated layer, not the live first-person local rig. First-person helmet/viewmodel renderers are hidden during editing and restored on close.
- Normal session startup should log `SessionSafetyCheck` with `EditorOpen=False`, no active DrawableSuits cameras, `Camera.main` state, local player state, prompt context, and `jetpackWarningGuard` status.
- If third-person setup fails, the editor falls back to texture-only `TextureFallback` and logs the reason.
- The UV texture panel shows the editable texture in a reserved right-column preview slot below the placement controls. It should not cover the color picker, brush controls, tools, design controls, or modal selectors.
- Decals, Stickers, and saved designs open in separate modal menus with larger selectable rows/buttons.
- Controller right trigger paints only. Camera zoom uses mouse wheel or controller D-pad up/down.
- Active edited textures are per player/client, not global per suit type.
- The color changer is a compact side-by-side hue ring and saturation/value square with a swatch and editable `#RRGGBB` hex field.
- Recent Colors swatches appear below the color picker. Dragging the picker, typing hex, or using Eyedropper does not add a swatch until that color is placed by Paint, Fill, Text, or Sticker.
- Undo History shows the newest undoable action labels first, including Brush stroke, Erase, Decal placed, Text placed, Sticker placed, Color fill, Reset, Load design, and Import code. Clicking a row only selects it; `Undo Selected` removes only that selected action where the snapshot diff can be isolated, preserves newer overlapping pixels, and keeps redo history available.
- `Clear History` clears undo and redo history without changing the current suit texture.
- The Saved Designs and Decals menus include two-step `Delete Selected` buttons. Deletes are permanent and only remove the selected save `.json` or selected decal image file from the DrawableSuits folder.
- Color picker handles are tied to the same coordinate conversion used for mouse/controller input, so the visible handle positions should match the selected hue, saturation, value, and typed hex color.
- Reset and Save no longer rebuild list hitboxes during the click; decal rows only change selection when their rows are clicked directly, and saved-design rows live in the separate Designs menu.
- Third-person camera yaw, pitch, and distance are preserved when loading a design or importing a design code while the editor is open.
- Controller `A` does not activate UI immediately after opening; move the left stick once to arm the virtual cursor, then `A` clicks the control under the cursor.
- Normal buttons should not stay highlighted after unrelated clicks; only selected tools, decals, and saved designs keep orange selection styling.
- The decal section has a `Decals` menu button. Its modal `Add Decal` button starts an external Windows picker process, waits asynchronously for a selected PNG/JPG/JPEG, validates and copies it into the Decals folder, refreshes decal rows, selects the imported image, and shows only a short status line. `Refresh` still reloads decal rows after manual folder changes.
- `Edit Decal` opens a temporary edit panel for the selected decal. Crop, stretch, flip, and filter settings affect previews and future stamps; the source image file is never overwritten.
- Decal and Sticker temporary filters use fixed rows for Grayscale, Sepia, Invert, Brightness, Contrast, Saturation, and Hue Shift, with independent intensity sliders that can be combined.
- The temporary edit preview preserves the generated stamp aspect after crop and width/height stretch, so the panel preview should match the placement preview and final stamp shape. Slider changes defer the expensive preview rebuild briefly and keep the last valid preview visible while you drag.
- All editor sliders use compact orange dot handles instead of tall rectangular handles.
- Long selected decal filenames are middle-ellipsized in compact display boxes while the Decals menu and diagnostics keep the full name.
- In Decal or Sticker mode over the third-person suit, the projected preview hides while the cursor is moving and appears after the cursor has been still briefly. Click/RT still stamps immediately even before the preview appears.
- Decal placement is single-shot: holding left mouse or RT places one decal until the input is released and pressed again.
- Third-person Decal preview and stamping project onto the visible suit surface and fill between valid projected samples, so decals avoid both UV-island wrapping and small suit-background cracks on curved geometry. The UV panel keeps direct flat UV decal stamping.
- The editor cursor is dynamic and rendered as a top-level non-raycastable graphic inside the visible editor canvas: Paint and Erase show a hollow brush ring sized to the current editable target, while UI hover, invalid targets, Decal, Text, Sticker, Eyedropper, and normal navigation show a small white dot.
- The old filled UV brush indicator and world-space sphere marker are kept hidden, so there should not be a colored square or blob following the cursor.
- In Text mode, the text input uses Unity's built-in Arial font, accepts one line up to 64 characters, and shows `Previewing text. Click/RT to stamp.` when the cursor is over a valid suit target.
- Text stamps are generated as transparent alpha-mask textures and tinted with the current brush color/opacity, so they should not stamp a black rectangle behind the letters.
- In third-person mode, Text stamps project onto the visible suit surface and skip glyph pixels that fall off the suit instead of wrapping them through unrelated UV islands.
- Third-person Text should read left-to-right from the editor camera. Mirrored Text should only appear when the UI-only `Mirror` button is enabled.
- The UV panel keeps direct flat UV Text stamping for texture-layout editing.
- Text is baked into the suit texture after stamping. It is not an editable layer after placement.
- In Sticker mode, the `Stickers` menu offers Circle, Square, Triangle, Diamond, Star, Heart, Arrow, Lightning Bolt, Plus/Cross, Ring, Crescent, and Shield. Stickers use the current brush color and opacity, preview live on the suit or UV panel, stamp once per press, and are baked into the texture.
- `Edit Sticker` opens the same temporary edit controls for the selected built-in sticker. Stickers remain brush-color based; filters are applied after tinting for the temporary stamp texture only.
- The `Fill` button is a UI-only tool. It flood-fills the contiguous same-color region under the cursor using the current brush color and opacity.
- Fill is single-shot: holding left mouse or controller RT fills once until the input is released and pressed again.
- The Fill Tolerance slider appears when Fill is active. Lower tolerance fills tighter matching regions; higher tolerance accepts more color variation.
- `Export Code` copies a compact lossless `DSUIT2:` code to the clipboard and fills the design code field. `Import Code` validates a pasted `DSUIT2:` or legacy `DSUIT1:` code and loads it into the current suit without auto-saving, broadcasting, or resetting the third-person camera.
- The UV panel shows a non-interactive rotated decal preview over the texture panel.
- The UV panel can zoom from `1x` to `8x` while the cursor is over it. Mouse wheel or controller D-pad up/down zooms around the cursor, and right-mouse drag or controller right stick pans the zoomed view.
- Controller right stick is contextual: it pans the UV panel while the cursor is over the panel, and still orbits the third-person camera when the cursor is outside the panel.
- UV-panel Paint, Erase, Fill, Decal, Text, Sticker, Eyedropper, Mirror previews, and the brush cursor all use the currently visible zoomed texture view.
- The `Mirror` button is a UI-only modifier. When it is orange, paint, erase, fill, decal stamps, text stamps, and sticker stamps use a surface-map mirror target on the opposite side of the baked suit mesh in one undo action.
- Mirrored decal previews show both the primary and mirrored decal. The mirrored decal is horizontally flipped and uses inverse rotation.
- The UV panel also uses the mesh mirror map when the clicked UV maps back to a suit triangle. If no mirror target is available, DrawableSuits applies the primary edit only and shows a short status.
- The `Eyedropper` button is a UI-only one-shot tool. It samples the editable suit texture at the cursor hit point, updates the swatch, color picker, hex field, and brush color, then returns to the previous Paint, Erase, or Decal tool.
- Eyedropper does not create undo entries and Mirror does not affect sampling.
- The part picker is removed. Third-person mode always shows the full avatar proxy, and the UV panel always shows the full editable suit texture.
- Paint and Erase in third-person mode project onto the visible suit surface and fill between valid projected samples, so brush strokes avoid UV-island cutoffs while still guarding against unrelated UV island bleeding. The UV panel keeps direct flat UV brush painting.
- Brush Shape changes Paint and Erase only. Circle keeps the classic brush, Square paints a square footprint, Pixel edits one texture pixel, Spray Paint and Noise/Scatter use deterministic sparse coverage per stroke, and Soft Airbrush uses a softer circular falloff.
- Paint, erase, decal preview, and decal stamping operate on the full editable texture.
- Active editor diagnostics report full proxy mesh/collider state through `WorldAvatarProxy updated`; `PartClassifierBuilt` should not appear during normal editor use.

Troubleshooting:

- If no decal preview appears, confirm a decal row is selected and Decal tool is active, then check `DecalPreviewUpdated` or `DecalPreviewHidden` diagnostics.
- If a design or decal delete button does nothing on the first click, that is the confirmation step. Press `Confirm Delete` to permanently remove the selected file, or change selection/refresh/close the modal to cancel.
- If Decal or Sticker preview movement feels laggy, confirm the installed package is 0.5.45 or newer and check `PlacementPreviewWaitingForIdle`, `PlacementPreviewHiddenWhileMoving`, `PlacementPreviewIdleRebuilt`, `DecalPreviewUpdated`, or `StickerPreviewUpdated` diagnostics. Third-person placement previews now wait until the cursor stops moving before rebuilding, while final stamps still use the full-quality projection path.
- If decals stamp repeatedly while holding input, confirm the installed package is 0.4.8 or newer and check for `DecalStampCommitted` entries; there should be one per press/release cycle.
- If Mirror does not appear to find the opposite side, check `MirrorSurfaceMap built` and `MirrorSurfaceTarget` diagnostics. Asymmetric or unusual modded meshes may not have a reliable opposite surface for every hit.
- If entering a session starts on a black screen before opening DrawableSuits, check `SessionSafetyCheck` lines. They list `Camera.main`, active cameras, camera target textures, local player flags, prompt context such as grab/hover fields, local renderer materials, and any repaired DrawableSuits objects. DrawableSuits should report no active DrawableSuits cameras while `EditorOpen=False`.
- If the black screen shows `Grab: [E]` and `SessionSafetyCheck` reports `Camera.main=null`, inspect `LogOutput.log` for repeated `JetpackWarning` `PlayerControllerB.LateUpdate` `NullReferenceException`. By default, DrawableSuits disables only `JetpackWarning.Patches.PlayerControllerB_LateUpdate_Postfix` after repeated failures and logs the unpatch result in `diagnostics.log`. Set `AutoDisableBrokenJetpackWarningLateUpdatePatch=false` to turn this compatibility guard off.
- If third person shows first-person arms, a giant helmet, held items, or another partial rig, check `World renderer candidate`, `Hidden nearby first-person overlay renderer`, `World editor visible renderer candidate`, and `WorldAvatarProxy updated` lines. The selected renderer should be a body/suit renderer and the proxy should use only the player-specific DrawableSuits material for suit-compatible submeshes.
- If you use ModelReplacementAPI and the editor shows the wrong model, duplicate helmets, or an uneditable suit, this is expected. DrawableSuits is not compatible with ModelReplacementAPI; use the UV panel where possible or disable the replacement while editing.
- If action buttons such as Reset, Save, or Load also select decal/save rows, confirm the installed package is 0.4.7 or newer. Lists now use stable row pools and log `ListRowsUpdated` instead of rebuilding/destroying row buttons during normal UI refresh.
- If controller `A` clicks the wrong UI item, confirm the installed package is 0.4.7 or newer, move the left stick before the first `A` press, then check `Virtual cursor A press` and `Virtual cursor A release` diagnostics. They should show the same resolved button or control that is visually under the cursor.
- If button highlights stick around, confirm the installed package is 0.4.7 or newer. Normal button selected colors are neutral.
- If the color picker handles do not line up with the selected color, check `ColorPickerInput` diagnostics for hue, saturation, value, local pointer coordinates, and final handle positions.
- If loading a design or importing a design code resets the third-person camera, confirm the installed package is 0.4.7 or newer and check for `World camera state preserved` diagnostics.
- If the color picker does not update paint color, check the swatch, editable hex field, and `DrawableColorPickerBuilt` diagnostics. Hex input accepts `#RRGGBB` or `RRGGBB`.
- If right trigger zooms the third-person camera, confirm the installed package is 0.4.4 or newer. In 0.4.4, right trigger is paint-only and D-pad up/down controls controller zoom.
- If the UV panel or texture-only fallback shows a second colored cursor, confirm the installed package is 0.4.4 or newer. The old filled brush indicator is disabled because it looked like another cursor.
- If editing one player changes every other player wearing the same skin, confirm the installed package is 0.4.4 or newer. Active edits now sync with owner client IDs and do not mutate suit rack/global suit materials.
- If you cannot see the local suit in third person, check `diagnostics.log` for `WorldThirdPerson setup`, `WorldAvatarProxy updated`, and `WorldEditorCamera updated`.
- If painting misses the suit, check `PaintAttempt` entries for `world paint input`, UV coordinates, and whether the cursor is over the editor panel.
- If Eyedropper does not sample a color, check `EyedropperMiss` entries for whether the cursor was over the visible suit or UV panel. A successful sample logs `EyedropperSampled` with UV, pixel, sampled hex color, and return tool.
- If Text does not preview or stamp, check that the text field is not empty, then search `diagnostics.log` for `TextStampRendered`, `TextPreviewUpdated`, `TextPreviewHidden`, `TextStampCommitted`, or `TextStampSkipped`.
- If Sticker does not preview or stamp, confirm Sticker tool is active and a shape is selected, then search `diagnostics.log` for `StickerShapeSelected`, `StickerPreviewUpdated`, `StickerPreviewHidden`, `StickerStampCommitted`, or `StickerStampSkipped`.
- If Decal or Sticker temporary edits do not appear, final Decal stamps use the original image, the edit-panel preview looks stretched, or temporary edit preview updates feel too slow, confirm the installed package is 0.5.54 or newer and check `PlacementEditPanelOpened`, `PlacementEditChanged`, `PlacementEditPreviewUpdated`, `PlacementEditPreviewCacheHit`, `PlacementEditPreviewSkipped`, `PlacementEditedStampGenerated`, `PlacementEditReset`, and `PlacementEditPanelClosed`. Edit-panel previews are real-time preview-quality renders; placement previews and final stamps remain full quality. These edits are session-only and never modify decal files or built-in sticker sources.
- If Text stamps with a black rectangle, confirm the installed package is 0.5.10 or newer. `TextStampRendered` should report `alphaMode=luminance`, glyph bounds, and a trimmed final texture size.
- If third-person Text drops side letters, confirm the installed package is 0.5.11 or newer and check `TextSurfacePreviewUpdated`, `TextSurfaceStampCommitted`, or `TextSurfaceStampSkipped` for written and skipped glyph-pixel counts.
- If third-person Text appears backwards, confirm the installed package is 0.5.12 or newer and check `TextProjectionFrameBuilt` for camera-right alignment and sample order diagnostics.
- If decals rotate unexpectedly or get clipped on third-person suit seams, confirm the installed package is 0.5.14 or newer and check `DecalSurfacePreviewUpdated`, `DecalSurfaceStampCommitted`, or `DecalSurfaceStampSkipped` diagnostics for projected written/skipped pixels.
- If third-person decals show small suit-background cracks through the decal, confirm the installed package is 0.5.15 or newer and check the same decal surface diagnostics for sample, hit, rasterized-cell, seam-skip, off-suit, and written-pixel counts. High seam-skip counts mean DrawableSuits is intentionally avoiding UV island bleeding.
- If Paint or Erase strokes cut off on third-person suit seams, confirm the installed package is 0.5.22 or newer and check `BrushSurfaceStrokeApplied`, `BrushSurfaceStrokeSkipped`, and `BrushSurfaceProjectionWarning` diagnostics for sample, hit, rasterized-cell, seam-skip, off-suit, and written-pixel counts. High seam-skip counts mean DrawableSuits is intentionally avoiding UV island bleeding.
- If Fill affects too much or too little of the suit, adjust Fill Tolerance and check `FillBucketApplied` diagnostics for seed color, tolerance, checked pixel count, matched pixels, written pixels, and mirror target. Fill is texture-contiguous, so separated UV islands may require separate fills.
- If the UV panel covers the color picker or other controls, confirm the installed package is 0.5.25 or newer and check `TexturePanel[...]` diagnostics for the preview viewport rect, sibling index, and anchored position.
- If UV-panel edits land in the wrong place while zoomed, confirm the installed package is 0.5.49 or newer and check `UvPanelViewChanged`, `TexturePanel[...]`, and `UiInputDiagnostics` for `uvPanelZoom`, `uvRect`, and `previewUv`.
- If controller right stick does not pan the zoomed UV panel, confirm the installed package is 0.5.50 or newer, place the cursor over the UV panel, and check `UvPanelViewChanged` for `source=GamepadRightStickPan`.
- If the cursor is missing, confirm the installed package is 0.5.21 or newer and check `CanvasCursorBuilt`, `CanvasCursorUpdated`, or `CanvasCursorHidden` diagnostics. The editor now draws the cursor directly inside the same UGUI canvas as the visible editor controls.
- If the cursor is still a filled square or colored world blob, confirm the installed package is 0.5.21 or newer and check `CanvasCursorUpdated` diagnostics. Paint and Erase should use `mode=BrushRing`; Decal, Text, Eyedropper, UI hover, and invalid targets should use `mode=Dot`.
- If the Paint or Erase ring size looks wrong, check `CanvasCursorUpdated` for `target=WorldThirdPerson`, `target=TexturePanel`, or `target=TextureFallback`, the computed diameter, hit triangle, UV, canvas-local position, and any fallback reason.
- If the controller cursor does not line up with the visible cursor, check `CanvasCursorUpdated` and the virtual cursor diagnostics. The visible cursor follows DrawableSuits' virtual cursor directly instead of warping the OS mouse cursor.
- If importing a design code resets the third-person camera, confirm the installed package is 0.5.14 or newer and check `DesignCodeImported` plus `World camera state preserved` diagnostics.
- If a design code does not import, confirm it starts with `DSUIT2:` or `DSUIT1:` and check `DesignCodeImportFailed` diagnostics for prefix, Base64Url, decompression, binary/JSON payload, PNG, or texture-size validation errors. DrawableSuits never logs the full code.
- If decals or saved designs do not appear, check `RefreshFileLists complete` and `ListRowsUpdated` entries.
- If scan, inventory scroll, or item use still happen while the editor is open, check for `Global gameplay actions locked` and `Blocked PlayerControllerB` entries.
- If keyboard or controller shortcuts do not open the editor, use the pause-menu `DrawableSuits` button.
- If the mouse cannot move after opening from pause, check cursor unlock and `pointerSource=Mouse` diagnostics.
- If `Add Decal` is canceled or unavailable, manually place image files in `BepInEx/config/DrawableSuits/Decals`, then open `Decals` and press `Refresh`. Check `DecalImportPickerStarted`, `DecalImportPickerCompleted`, `DecalImportPickerCanceled`, `DecalImportPickerFailed`, `DecalImported`, or `DecalImportFailed` diagnostics for picker/import details.
- If you quit to the main menu while the editor is open, DrawableSuits closes the editor during the scene change so main-menu navigation is restored.
- Lethal Company v81 uses Unity's Input System path; repeated `UnityEngine.Input` exceptions should not appear from DrawableSuits.

## Known Limits

- Third-person Paint, Erase, Decal, Text, and Sticker project onto the visible suit surface before resolving to texture UVs. Unusual modded suit UV layouts may still make edits appear somewhere unexpected, and seam guards may skip ambiguous pixels instead of bleeding across unrelated islands.
- Mirror mode uses a mesh surface map, so unusual or asymmetric meshes may skip the mirrored edit when no reliable opposite surface can be found.
- Text stamps use Unity's built-in Arial font only in this version and are baked as tinted transparent alpha masks. Third-person Text projection can still skip letters that physically land off the visible suit surface.
- Cross-suit loading depends on UV compatibility.
- Share codes can still be long because they embed PNG image data. `DSUIT2:` removes the older inner Base64 PNG-in-JSON overhead, but it is still lossless and does not downscale or reduce quality.
- Very large decal images are resized to the configured maximum texture size.
- Multiplayer sync is designed for applied designs, not every brush stroke.
- Texture-only fallback remains available for debugging and edge cases.
