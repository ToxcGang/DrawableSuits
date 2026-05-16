# Changelog

## 0.1.5

- Added an independent lightweight debug HUD that appears briefly on startup and can be toggled with `F9`.
- Added a second forced `F10` emergency-open path outside the editor controller so input/editor failures are easier to separate.
- Logged debug HUD state, active scene, quick-menu state, editor canvas state, EventSystem, camera, and diagnostics log path.
- Documented how to tell the difference between "plugin is not loading" and "editor canvas is not opening."

## 0.1.4

- Added deep diagnostics to BepInEx logs and `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.
- Made the editor overlay open as a diagnostic shell even when suit, player model, camera, or preview detection is incomplete.
- Added visible editor diagnostics for selected suit id, suit count, local player, player model, camera, and preview collider state.
- Changed pause-menu opening to close the quick menu first, then open DrawableSuits on the next frame with detailed click and row-placement logging.
- Added `F10` emergency diagnostics overlay shortcut.
- Added a fallback Unity `Text` diagnostics canvas if TMP editor canvas construction fails.

## 0.1.3

- Replaced the drawing menu renderer with a Unity UI overlay canvas.
- Prevented cursor-only failed opens by making editor opening report success or an explicit failure reason.
- Added EventSystem fallback creation for the DrawableSuits overlay when the game scene does not expose one.
- Updated README troubleshooting for cursor-only editor failures.

## 0.1.2

- Fixed the pause-menu `DrawableSuits` button overlapping Resume and other menu rows.
- Added explicit controller navigation for the injected pause-menu button.
- Improved `F8` and `View/Back + Y` fallback shortcut reliability through Unity Input System polling.
- Added clearer warnings when the editor cannot open because a player model or editable suit is unavailable.
- Updated README troubleshooting for pause-menu placement and player-model load timing.

## 0.1.1

- Added a `DrawableSuits` button to the in-game pause menu.
- Improved editor opening reliability for keyboard and controller users.
- Kept `F8` and `View/Back + Y` as fallback editor shortcuts.
- Updated README documentation for the pause-menu opening flow.

## 0.1.0

- Initial DrawableSuits implementation.
- Added BepInEx plugin scaffold with GUID `com.toxcgang.drawablesuits`.
- Added in-game suit editor with keyboard, mouse, and controller controls.
- Added 3D mesh UV painting, erase, undo, redo, reset, and decal placement.
- Added save/load support using JSON metadata and PNG textures.
- Added decal import from the DrawableSuits decal folder and optional Windows file dialog.
- Added apply/save multiplayer sync using Unity Netcode named messages.
- Added vanilla and modded suit material reapplication patches.
- Added Thunderstore manifest metadata.
