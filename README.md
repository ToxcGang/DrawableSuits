# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits in-game, place image decals, save and load designs, and apply edited textures to vanilla or modded suits.

## Features

- Default third-person paint editor: opening DrawableSuits switches to an editor camera around the local player so you can paint directly on the visible suit.
- Compact side overlay with Paint, Erase, Decal, a UI-only Mirror toggle, brush sliders, a hue/SV color picker, decal controls, design name, visible decal rows, visible saved-design rows, Undo, Redo, Reset, Apply, Save, Load, and Close.
- Mirror painting duplicates Paint, Erase, and Decal edits across the texture's left-right UV axis without adding keyboard or controller shortcuts.
- Decal placement preview: Decal mode shows a translucent live preview before stamping, then places one decal per click or right-trigger press.
- Pause-menu entry point: use the `DrawableSuits` button below Resume.
- Fallback shortcuts: `F8` on keyboard or `View/Back + Y` on controller.
- Emergency open shortcut: `F10`, which opens the editor and does not toggle it closed.
- Controller support: left stick moves the editor cursor, `A` clicks exactly the UI control under the cursor, right trigger paints only, right stick/bumpers orbit the camera, D-pad up/down zooms, `Y` cycles tools, `X` undoes, and Start saves.
- Direct surface painting: the editor bakes a hidden mesh collider from the local player model and paints by raycasting from the third-person camera to suit UV coordinates.
- UV fallback mode: press `Use UV Fallback` if third-person setup fails; it shows the full editable suit texture.
- PNG/JPG decals from `BepInEx/config/DrawableSuits/Decals`. The in-game OS file dialog is disabled for stability in Gale/Unity.
- Reusable saved designs stored as JSON metadata plus PNG texture files.
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
- Left mouse: paint/erase continuously; in Decal mode, stamp one decal at the preview location.
- Mirror: click the `Mirror` UI button to duplicate paint, erase, and decal stamps across the UV left-right axis.
- Right mouse: orbit the third-person editor camera.
- Mouse wheel: zoom the third-person camera.
- Ctrl + mouse wheel: change brush size.
- Escape or Close: close the editor.

Controller:

- `View/Back + Y`: open or close.
- Left stick: move the editor cursor.
- `A`: click the button, field, slider, or color picker region directly under the cursor.
- Mirror: move the virtual cursor over the `Mirror` UI button and press `A`; there is no controller shortcut for this modifier.
- Right trigger: paint/erase continuously; in Decal mode, stamp one decal at the preview location.
- Right stick or bumpers: orbit the third-person editor camera.
- D-pad up/down: zoom the third-person editor camera.
- `Y`: cycle Paint/Erase/Decal. Decal is skipped until a decal is selected.
- `X`: undo.
- Start: save.

## Folders

DrawableSuits creates these folders after launch:

- `BepInEx/config/DrawableSuits/Saves` stores `.json` design metadata.
- `BepInEx/config/DrawableSuits/Textures` stores saved design `.png` textures.
- `BepInEx/config/DrawableSuits/Decals` stores user decal images.
- `BepInEx/config/DrawableSuits/Logs` stores the diagnostics log.

Put `.png`, `.jpg`, or `.jpeg` files in `Decals` and press `Refresh Decals` in the editor to use them.

## Multiplayer

Edited suits update locally while painting. Other mod users receive your player-specific texture only when you press `Apply` or `Save`. Texture payloads are chunked through Unity Netcode custom messages with the owner client ID and validated with a hash before being applied.

Players without DrawableSuits can still join normally, but they will see the original suit textures.

DrawableSuits no longer replaces every rack/player using the same base suit when one player edits their suit. Saved designs are reusable, but active in-session edits are applied to the selected player only.

## Modded Suits

DrawableSuits works with modded suits by detecting unlockables that expose a `suitMaterial`. Saved designs are reusable on any suit, but loading a design onto a suit with a different UV layout can stretch or misplace drawings and decals.

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
- `StartInUvFallbackMode`, disabled by default, opens directly into the old UV fallback view.
- `ThirdPersonCameraDistance`, the default third-person editor camera distance.
- `ApplyLocalFirstPersonArms`, disabled by default, is experimental and allows edited materials on local first-person arms/body outside the editor.
- `AutoDisableBrokenJetpackWarningLateUpdatePatch`, enabled by default, disables only the broken JetpackWarning `PlayerControllerB.LateUpdate` postfix after repeated null-reference errors are detected.
- `EnableExperimentalModelPreview`, disabled by default, keeps the old RenderTexture model preview as diagnostics only.
- OS file dialog import remains disabled/experimental and is ignored in-game for stability.

## Debugging

DrawableSuits writes detailed startup, pause-menu, input, editor, camera, collider, raycast, suit-detection, list-row, and paint logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.

When testing with Gale, also search `BepInEx/LogOutput.log` in the active Gale profile for `DrawableSuits`.

Expected 0.5.6 behavior:

- Opening the editor shows a compact side overlay and a third-person camera view of the local player.
- The diagnostics text should show `Preview mode: WorldThirdPerson` when the default path succeeds.
- The visible editor model is `DrawableSuitsWorldAvatarProxy`, a baked suit/body proxy on an isolated layer, not the live first-person local rig. First-person helmet/viewmodel renderers are hidden during editing and restored on close.
- Normal session startup should log `SessionSafetyCheck` with `EditorOpen=False`, no active DrawableSuits cameras, `Camera.main` state, local player state, prompt context, and `jetpackWarningGuard` status.
- If third-person setup fails, the editor falls back to `TextureFallback` and logs the reason.
- Decal and saved-design rows are explicit anchored buttons, not ScrollRect/layout rows.
- Controller right trigger paints only. Camera zoom uses mouse wheel or controller D-pad up/down.
- Active edited textures are per player/client, not global per suit type.
- The color changer is a compact side-by-side hue ring and saturation/value square with a swatch and editable `#RRGGBB` hex field.
- Color picker handles are tied to the same coordinate conversion used for mouse/controller input, so the visible handle positions should match the selected hue, saturation, value, and typed hex color.
- Reset, Save, and Load no longer rebuild list hitboxes during the click; decal and saved-design rows only change selection when their rows are clicked directly.
- Third-person camera yaw, pitch, and distance are preserved when loading a design or switching suits while the editor is open.
- Controller `A` does not activate UI immediately after opening; move the left stick once to arm the virtual cursor, then `A` clicks the control under the cursor.
- Normal buttons should not stay highlighted after unrelated clicks; only selected tools, decals, and saved designs keep orange selection styling.
- The decal section has one `Refresh Decals` button. It refreshes decal and save rows and shows only a short status line.
- In Decal mode with a selected decal, hovering over the suit shows a translucent preview and status `Previewing decal. Click/RT to stamp.`
- Decal placement is single-shot: holding left mouse or RT places one decal until the input is released and pressed again.
- UV fallback mode shows a non-interactive rotated decal preview over the texture panel.
- The `Mirror` button is a UI-only modifier. When it is orange, paint, erase, and decal stamps are duplicated across `mirroredUv = (1 - uv.x, uv.y)` in one undo action.
- Mirrored decal previews show both the primary and mirrored decal. The mirrored decal is horizontally flipped and uses inverse rotation.
- The part picker is removed. Third-person mode always shows the full avatar proxy, and UV fallback always shows the full editable suit texture.
- Paint, erase, decal preview, and decal stamping operate on the full editable texture.
- Active editor diagnostics report full proxy mesh/collider state through `WorldAvatarProxy updated`; `PartClassifierBuilt` should not appear during normal editor use.

Troubleshooting:

- If no decal preview appears, confirm a decal row is selected and Decal tool is active, then check `DecalPreviewUpdated` or `DecalPreviewHidden` diagnostics.
- If decals stamp repeatedly while holding input, confirm the installed package is 0.4.8 or newer and check for `DecalStampCommitted` entries; there should be one per press/release cycle.
- If Mirror does not appear to match the body side you expected, remember it mirrors in UV texture space. Modded suits or unusual UV layouts may not map perfectly to anatomical left/right symmetry.
- If entering a session starts on a black screen before opening DrawableSuits, check `SessionSafetyCheck` lines. They list `Camera.main`, active cameras, camera target textures, local player flags, prompt context such as grab/hover fields, local renderer materials, and any repaired DrawableSuits objects. DrawableSuits should report no active DrawableSuits cameras while `EditorOpen=False`.
- If the black screen shows `Grab: [E]` and `SessionSafetyCheck` reports `Camera.main=null`, inspect `LogOutput.log` for repeated `JetpackWarning` `PlayerControllerB.LateUpdate` `NullReferenceException`. By default, DrawableSuits disables only `JetpackWarning.Patches.PlayerControllerB_LateUpdate_Postfix` after repeated failures and logs the unpatch result in `diagnostics.log`. Set `AutoDisableBrokenJetpackWarningLateUpdatePatch=false` to turn this compatibility guard off.
- If third person shows first-person arms, a giant helmet, held items, or another partial rig, check `World renderer candidate`, `Hidden nearby first-person overlay renderer`, `World editor visible renderer candidate`, and `WorldAvatarProxy updated` lines. The selected renderer should be a body/suit renderer and the proxy should use only the player-specific DrawableSuits material for suit-compatible submeshes.
- If action buttons such as Reset, Save, or Load also select decal/save rows, confirm the installed package is 0.4.7 or newer. Lists now use stable row pools and log `ListRowsUpdated` instead of rebuilding/destroying row buttons during normal UI refresh.
- If controller `A` clicks the wrong UI item, confirm the installed package is 0.4.7 or newer, move the left stick before the first `A` press, then check `Virtual cursor A press` and `Virtual cursor A release` diagnostics. They should show the same resolved button or control that is visually under the cursor.
- If button highlights stick around, confirm the installed package is 0.4.7 or newer. Normal button selected colors are neutral.
- If the color picker handles do not line up with the selected color, check `ColorPickerInput` diagnostics for hue, saturation, value, local pointer coordinates, and final handle positions.
- If loading a design or switching suits resets the third-person camera, confirm the installed package is 0.4.7 or newer and check for `World camera state preserved` diagnostics.
- If the color picker does not update paint color, check the swatch, editable hex field, and `DrawableColorPickerBuilt` diagnostics. Hex input accepts `#RRGGBB` or `RRGGBB`.
- If right trigger zooms the third-person camera, confirm the installed package is 0.4.4 or newer. In 0.4.4, right trigger is paint-only and D-pad up/down controls controller zoom.
- If UV fallback shows a second colored cursor, confirm the installed package is 0.4.4 or newer. The old filled brush indicator is disabled because it looked like another cursor.
- If editing one player changes every other player wearing the same skin, confirm the installed package is 0.4.4 or newer. Active edits now sync with owner client IDs and do not mutate suit rack/global suit materials.
- If you cannot see the local suit in third person, check `diagnostics.log` for `WorldThirdPerson setup`, `WorldAvatarProxy updated`, and `WorldEditorCamera updated`.
- If painting misses the suit, check `PaintAttempt` entries for `world paint input`, UV coordinates, and whether the cursor is over the editor panel.
- If decals or saved designs do not appear, check `RefreshFileLists complete` and `ListRowsUpdated` entries.
- If scan, inventory scroll, or item use still happen while the editor is open, check for `Global gameplay actions locked` and `Blocked PlayerControllerB` entries.
- If keyboard or controller shortcuts do not open the editor, use the pause-menu `DrawableSuits` button.
- If the mouse cannot move after opening from pause, check cursor unlock and `pointerSource=Mouse` diagnostics.
- Pressing `Refresh Decals` is safe in-game and shows only a short status line. To import decals, place image files in `BepInEx/config/DrawableSuits/Decals`, then press `Refresh Decals`.
- If you quit to the main menu while the editor is open, DrawableSuits closes the editor during the scene change so main-menu navigation is restored.
- Lethal Company v81 uses Unity's Input System path; repeated `UnityEngine.Input` exceptions should not appear from DrawableSuits.

## Known Limits

- Third-person painting uses the suit mesh UVs from `RaycastHit.textureCoord`; unusual modded suit UV layouts may still make strokes appear somewhere unexpected.
- Mirror mode is UV-space mirroring, so unusual or asymmetric UV layouts may mirror to a different body area than expected.
- Cross-suit loading depends on UV compatibility.
- Very large decal images are resized to the configured maximum texture size.
- Multiplayer sync is designed for applied designs, not every brush stroke.
- The old UV fallback view remains available for debugging and edge cases.
