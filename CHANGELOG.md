# Changelog

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
