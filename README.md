# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits in-game, place image decals, save and load designs, and apply edited textures to vanilla or modded suits.

## Features

- Default third-person paint editor: opening DrawableSuits switches to an editor camera around the local player so you can paint directly on the visible suit.
- Compact side overlay with Paint, Erase, Decal, brush/color sliders, decal controls, design name, visible decal rows, visible saved-design rows, Undo, Redo, Reset, Apply, Save, Load, and Close.
- Pause-menu entry point: use the `DrawableSuits` button below Resume.
- Fallback shortcuts: `F8` on keyboard or `View/Back + Y` on controller.
- Emergency open shortcut: `F10`, which opens the editor and does not toggle it closed.
- Controller support: left stick moves the editor cursor, `A` clicks the UI under the cursor, right trigger paints, right stick/bumpers orbit the camera, `Y` cycles tools, `X` undoes, and Start saves.
- Direct surface painting: the editor bakes a hidden mesh collider from the local player model and paints by raycasting from the third-person camera to suit UV coordinates.
- UV fallback mode: press `Use UV Fallback` if third-person setup fails or if you need the old texture-layout view for debugging.
- PNG/JPG decals from `BepInEx/config/DrawableSuits/Decals`. The in-game OS file dialog is disabled for stability in Gale/Unity.
- Reusable saved designs stored as JSON metadata plus PNG texture files.
- Apply/save multiplayer sync for other players who also have DrawableSuits installed.
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
- Left mouse: paint/erase/place decal when aiming at the suit.
- Right mouse: orbit the third-person editor camera.
- Mouse wheel: zoom the third-person camera.
- Ctrl + mouse wheel: change brush size.
- Escape or Close: close the editor.

Controller:

- `View/Back + Y`: open or close.
- Left stick: move the editor cursor.
- `A`: click the UI control under the cursor.
- Right trigger: paint/erase/place decal on the suit.
- Right stick or bumpers: orbit the third-person editor camera.
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

Edited suits update locally while painting. Other mod users receive the texture only when you press `Apply` or `Save`. Texture payloads are chunked through Unity Netcode custom messages and validated with a hash before being applied.

Players without DrawableSuits can still join normally, but they will see the original suit textures.

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
- `EnableExperimentalModelPreview`, disabled by default, keeps the old RenderTexture model preview as diagnostics only.
- OS file dialog import remains disabled/experimental and is ignored in-game for stability.

## Debugging

DrawableSuits writes detailed startup, pause-menu, input, editor, camera, collider, raycast, suit-detection, list-row, and paint logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.

When testing with Gale, also search `BepInEx/LogOutput.log` in the active Gale profile for `DrawableSuits`.

Expected 0.4.1 behavior:

- Opening the editor shows a compact side overlay and a third-person camera view of the local player.
- The diagnostics text should show `Preview mode: WorldThirdPerson` when the default path succeeds.
- Normal session startup should log `SessionSafetyCheck` with `EditorOpen=False` and no active DrawableSuits cameras.
- If third-person setup fails, the editor falls back to `TextureFallback` and logs the reason.
- Decal and saved-design rows are explicit anchored buttons, not ScrollRect/layout rows.

Troubleshooting:

- If entering a session starts on a black screen before opening DrawableSuits, check `SessionSafetyCheck` lines. They list active cameras, camera target textures, local renderer materials, and any repaired DrawableSuits objects. DrawableSuits should report no active DrawableSuits cameras while `EditorOpen=False`.
- If `LogOutput.log` repeatedly shows `JetpackWarning` `PlayerControllerB.LateUpdate` `NullReferenceException`, DrawableSuits logs a warning but does not patch that mod; use the new camera/material diagnostics to separate DrawableSuits state from external errors.
- If you cannot see the local suit in third person, check `diagnostics.log` for `WorldThirdPerson setup`, `Forced local player renderer visibility`, `WorldPaintProxy updated`, and `WorldEditorCamera updated`.
- If painting misses the suit, check `PaintAttempt` entries for `world paint input`, UV coordinates, and whether the cursor is over the editor panel.
- If decals or saved designs do not appear, check `RefreshFileLists complete` and `ListRowsBuilt` entries.
- If scan, inventory scroll, or item use still happen while the editor is open, check for `Global gameplay actions locked` and `Blocked PlayerControllerB` entries.
- If keyboard or controller shortcuts do not open the editor, use the pause-menu `DrawableSuits` button.
- If the mouse cannot move after opening from pause, check cursor unlock and `pointerSource=Mouse` diagnostics.
- Pressing `Refresh Decals` is safe in-game. To import decals, place image files in `BepInEx/config/DrawableSuits/Decals`, then press `Refresh Decals`.
- If you quit to the main menu while the editor is open, DrawableSuits closes the editor during the scene change so main-menu navigation is restored.
- Lethal Company v81 uses Unity's Input System path; repeated `UnityEngine.Input` exceptions should not appear from DrawableSuits.

## Known Limits

- Third-person painting uses the suit mesh UVs from `RaycastHit.textureCoord`; unusual modded suit UV layouts may still make strokes appear somewhere unexpected.
- Cross-suit loading depends on UV compatibility.
- Very large decal images are resized to the configured maximum texture size.
- Multiplayer sync is designed for applied designs, not every brush stroke.
- The old UV fallback view remains available for debugging and edge cases.
