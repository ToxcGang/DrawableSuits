# Changelog

## 0.3.4

- Delayed pause-menu editor opening until after `QuickMenuManager.CloseQuickMenu()` has had a frame to release cursor/UI state.
- Reapplied editor cursor unlock while open and logged cursor lock recovery when the game recaptures it.
- Made mouse the default editor pointer even when a controller is connected; controller virtual cursor only takes over while actively used.
- Avoided preview layer 31 and rebuilt preview rendering around a pivoted, centered mesh with a RenderTexture-targeted camera.
- Kept the preview camera enabled while it targets the RenderTexture and rendered the preview every frame.
- Added pointer-source, mouse position, gamepad stick, cursor-lock, and preview camera diagnostics.

## 0.3.3

- Isolated the preview rig from the gameplay world by normalizing the baked suit mesh, rendering through a disabled manual preview camera, and avoiding active world-lighting side effects.
- Added preview diagnostics for selected layer, camera enabled state, culling masks, normalized bounds, scale, and RenderTexture state.
- Added Harmony input guards that block Lethal Company jump, look, interact, and item-interact callbacks while the DrawableSuits editor is open.
- Removed gamepad `A` from Unity UI submit and routed it through DrawableSuits' virtual-cursor raycast so it clicks only the control under the visible cursor.
- Replaced Unity `Slider` controls with DrawableSuits-owned slider controls to prevent stretched orange fill/handle blocks.
- Disabled the in-game Windows file dialog import path; decals are now refreshed from `BepInEx/config/DrawableSuits/Decals` only.
- Updated troubleshooting for white-world rendering, missing preview model, controller jump, cursor offset, and import crashes.

## 0.3.2

- Moved the suit preview into an editor-owned camera and RenderTexture shown inside the UI, so the model is not hidden by the world camera, ship geometry, or darkness.
- Changed painting, erasing, and decals to raycast through the editor preview viewport instead of `Camera.main`.
- Fixed slider child rect setup so fill and handle graphics no longer stretch into large orange blocks.
- Added a DrawableSuits-owned `InputSystemUIInputModule` while the editor is open, then restored the game's UI input modules on close.
- Made the virtual cursor marker ignore UI raycasts so mouse clicks can reach buttons, sliders, and inputs.
- Disabled the local player's `PlayerActions.Movement` map while editing and restored it on close, preventing controller `A` from jumping in the menu.
- Removed the global controller `A` Apply shortcut; `A` now submits the selected UI control only.
- Added UI input, raycast, preview camera, preview viewport, and RenderTexture diagnostics.

## 0.3.1

- Rebuilt the editor panel with deterministic anchored Unity UI controls to fix the blue translucent panel with missing labels/buttons.
- Added detailed editor control-tree diagnostics for visible controls, text, images, and selectable state.
- Locked local player movement and look input while the editor is open, then restored the previous input state on close.
- Closed the editor automatically on full scene changes and main-menu transitions so main-menu navigation is not left captured.
- Ignored normal editor open shortcuts outside gameplay context while keeping `F10` available for diagnostics.
- Prevented controller submit from triggering Apply while a UI button or design-name input field is selected.

## 0.3.0

- Restored the usable suit editor on the stable 0.2.x runtime host.
- Replaced the diagnostic-only editor shell with a full built-in Unity UI editor canvas.
- Added live controls for suit selection, paint, erase, decals, brush size, opacity, RGB color, decal scale/rotation, undo, redo, reset, apply, save, load, refresh, import, and close.
- Re-enabled real-time preview painting, erase, decal placement, save/load, and apply flows.
- Added explicit UI selectable navigation and kept controller virtual-cursor painting support.
- Kept diagnostics/status text visible when suit, player model, camera, or preview dependencies are missing.

## 0.2.1

- Replaced active editor/runtime keyboard and mouse polling with Unity Input System reads for Lethal Company v81.
- Stopped repeated legacy `UnityEngine.Input` exceptions when the game is configured for Input System only.
- Kept a one-time guarded legacy fallback for unusual cases where no Input System keyboard is available.
- Cleaned up the diagnostic editor shell so the log path uses the readable config-relative path and panel text has more room.
- Updated troubleshooting notes for the Input System-only runtime path.

## 0.2.0

- Rebuilt runtime lifetime around the stable BepInEx plugin host instead of a separate early runtime object.
- Added `EnsureRuntimeReady()` recreation for registry, sync, editor, and runtime host components.
- Centralized `F8`, `F10`, and controller open-chord polling in `DrawableSuitsRuntimeHost`.
- Changed `F10` to open-only emergency behavior so it cannot immediately close the editor.
- Replaced the debug HUD with a runtime-host-owned bootstrap HUD using built-in Unity UI/Text and `OnGUI`.
- Simplified the editor to a reliable diagnostic shell while painting/decal/save/load controls are restored after runtime stability is confirmed.
- Hardened Harmony patch and pause-menu button paths with try/catch diagnostics and runtime recreation before open requests.
- Added Gale `BepInEx/LogOutput.log` troubleshooting notes.

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
