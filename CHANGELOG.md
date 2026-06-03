# Changelog

## 0.5.32

- Removed the manual suit selector from the editor.
- Locked editor operations to the local player's currently worn suit.
- Added README guidance that ModelReplacementAPI is not compatible with DrawableSuits.

## 0.5.25

- Made the UV texture panel visible alongside the third-person editor instead of requiring a normal view toggle.
- Added target-based input routing so hovering the UV panel edits the texture panel while hovering the world edits the third-person suit.
- Kept texture-only fallback behavior for `StartInUvFallbackMode` and failed third-person setup.
- Added `TexturePanel[...]` diagnostics for UV panel assignment and input target distinction.

## 0.5.24

- Moved the UV fallback preview into a reserved right-column slot so it no longer covers the color picker or brush controls.
- Rebalanced the right-column design/action/list controls around the new UV preview area.
- Added UV preview sibling index and anchored position diagnostics to `TexturePreview[...]` logs.

## 0.5.23

- Added a UI-only Fill Bucket tool with a visible Fill Tolerance slider.
- Fill flood-fills the contiguous matching texture region under the cursor using the current brush color and opacity.
- Added single-shot mouse/controller RT fill behavior with one undo entry for primary plus mirrored fills.
- Added anatomical Mirror support for Fill when a mirror target is available.
- Added `FillBucketApplied` and `FillBucketSkipped` diagnostics.

## 0.5.22

- Changed third-person Paint and Erase to project brush strokes onto the visible suit surface before resolving to texture UVs.
- Added filled surface-brush coverage with seam guards so strokes no longer cut off at normal UV island boundaries while avoiding unrelated-island bleeding.
- Kept UV fallback Paint and Erase as direct UV-layout editing.
- Added `BrushSurfaceStrokeApplied`, `BrushSurfaceStrokeSkipped`, and `BrushSurfaceProjectionWarning` diagnostics.

## 0.5.21

- Replaced the native software cursor renderer with a direct editor-canvas cursor drawn through the same UGUI canvas as the visible editor controls.
- Kept dynamic Paint and Erase brush rings plus dot-mode cursor behavior without relying on Unity native cursor textures, IMGUI, or a separate cursor canvas.
- Stopped controller cursor rendering from depending on OS mouse warping while preserving existing virtual-cursor UI targeting.
- Added `CanvasCursorBuilt`, `CanvasCursorUpdated`, and `CanvasCursorHidden` diagnostics.

## 0.5.20

- Replaced the IMGUI cursor renderer with Unity's native software cursor path using generated cursor textures.
- Restored visible native cursor handling while the editor is open, including dynamic Paint and Erase brush rings plus dot-mode cursor behavior.
- Added controller cursor warping so the native cursor follows the virtual cursor during gamepad editing.
- Added `NativeCursorUpdated`, `NativeCursorWarped`, `NativeCursorReset`, and `NativeCursorSetFailed` diagnostics.

## 0.5.19

- Replaced the unreliable UGUI cursor canvas with an immediate-mode overlay cursor drawn through `OnGUI`.
- Kept dynamic Paint and Erase brush rings plus dot-mode cursor behavior without relying on Canvas, Image, CanvasScaler, sorting order, EventSystem, or raycasters.
- Hid the native Unity cursor while the editor is open so the immediate cursor is the only editor pointer.
- Added `ImmediateCursorUpdated` and `ImmediateCursorDrawSkipped` diagnostics for the new cursor renderer.

## 0.5.18

- Fixed the invisible dynamic cursor caused by assigning the cursor canvas sorting order above Unity's safe signed range.
- Moved the editor canvas below the cursor canvas and kept the cursor canvas at `sortingOrder=32767` with override sorting enabled.
- Slightly increased dot cursor contrast/size and expanded cursor diagnostics with requested versus actual canvas order.

## 0.5.17

- Fixed the missing dynamic cursor by moving it to a dedicated topmost `DrawableSuitsCursorCanvas`.
- Rebuilt cursor visuals as layered non-raycastable dot/ring images with a dark backing for contrast.
- Kept Paint and Erase brush rings dynamic while UI hover, invalid targets, Decal, Text, Eyedropper, and normal navigation use a small dot.
- Added `CursorCanvasState` diagnostics and expanded `DynamicCursorUpdated` cursor visibility details.

## 0.5.16

- Replaced the fixed square editor cursor with a dynamic UGUI cursor.
- Added hollow Paint and Erase brush rings that scale to the active brush radius in third-person and UV fallback modes.
- Changed non-paint tools, invalid targets, and UI hover to use a small dot cursor while keeping the old filled brush indicator and world sphere marker hidden.
- Added rate-limited `DynamicCursorUpdated` diagnostics for cursor mode, target, brush size, computed diameter, UV, triangle, and fallback sizing.

## 0.5.15

- Fixed third-person decal tearing by rasterizing projected decal coverage between valid surface samples instead of writing only individual projected points.
- Added seam guards so projected decals skip cells that jump across UV islands instead of bleeding into unrelated atlas regions.
- Expanded decal projection diagnostics with sample, hit, rasterized-cell, seam-skip, off-suit, and written-pixel counts.

## 0.5.14

- Added compact lossless `DSUIT2:` share codes while keeping legacy `DSUIT1:` import compatibility.
- Preserved third-person camera yaw, pitch, and zoom when importing a design code.
- Changed third-person Decal preview and stamping to project onto the visible suit surface, matching the Text projection path and avoiding UV island clipping/rotation issues.
- Added `DecalSurfacePreviewUpdated`, `DecalSurfaceStampCommitted`, and expanded design-code diagnostics for payload and code lengths.

## 0.5.13

- Added shareable `DSUIT1:` design codes that export the current editable suit texture and metadata into a copy/paste code.
- Added an import/export design code panel with Copy Current, Paste, Import, and Close controls.
- Imports validate code prefix, Base64Url, compressed JSON, PNG data, texture dimensions, and design name before loading into the current suit without auto-saving or broadcasting.
- Added `DesignCodeExported`, `DesignCodeImportRequested`, `DesignCodeImported`, and `DesignCodeImportFailed` diagnostics without logging full codes.

## 0.5.12

- Fixed third-person Text preview and stamping appearing horizontally mirrored by preserving camera-right alignment in the surface projection basis.
- Added focused `TextProjectionFrameBuilt` diagnostics for text projection orientation, handedness, sample order, and camera alignment.
- Kept mirrored Text behavior limited to the UI-only Mirror toggle.

## 0.5.11

- Changed third-person Text stamping to project glyph pixels onto the visible suit surface instead of applying a flat UV rectangle.
- Added third-person Text preview and stamp diagnostics for projected world size, written pixels, skipped glyph pixels, hit point, and hit normal.
- Kept UV fallback Text stamping as direct UV editing while preserving transparent alpha-mask text rendering from 0.5.10.

## 0.5.10

- Fixed Text stamps rendering with an opaque black background by converting rendered glyphs into transparent alpha-mask textures.
- Fixed clipped Text stamps by trimming generated glyph bounds with padding so first and last letters are preserved.
- Updated Text diagnostics to log raw render size, glyph bounds, final texture size, visible pixel count, and alpha extraction mode.

## 0.5.9

- Added a UI-only `Text` tool that previews and stamps single-line text onto the suit in third-person mode and UV fallback.
- Text stamps use the built-in Arial font, current brush color, brush opacity, size/rotation placement controls, and one undo entry per stamp.
- Added Mirror support for text stamps plus `TextPreviewUpdated`, `TextPreviewHidden`, `TextStampCommitted`, and `TextStampSkipped` diagnostics.

## 0.5.8

- Added a UI-only one-shot `Eyedropper` tool for sampling colors from the third-person suit surface or UV fallback preview.
- Sampling updates the brush color, swatch, hue/SV picker, and hex input, then returns to the previous Paint, Erase, or Decal tool.
- Added `EyedropperSampled` and rate-limited miss diagnostics without adding undo entries or new keyboard/controller shortcuts.

## 0.5.7

- Replaced UV-axis mirror painting with anatomical surface-map mirroring in third-person mode.
- Added mesh-based mirror lookup for UV fallback when the clicked UV maps to the suit mesh.
- Updated mirrored decal previews and stamps to use the resolved opposite suit surface instead of `uv.x = 1 - uv.x`.
- Added mirror diagnostics for source local point, reflected local point, selected mirror triangle, distance, and skipped mirror targets.

## 0.5.6

- Added a UI-only `Mirror` toggle that duplicates Paint, Erase, and Decal edits across the suit texture's left-right UV axis.
- Mirrored decal stamping now places the primary and mirrored decal in one click or right-trigger press, with one undo entry.
- Added mirrored decal placement previews in third-person mode and UV fallback mode.
- Added mirror diagnostics for primary and mirrored UV/pixel coordinates.

## 0.5.5

- Removed the part picker and restored full-suit third-person editing as the only active editing workflow.
- Stopped filtering third-person proxy meshes, UV fallback textures, paint strokes, erase strokes, and decal stamps through generated part masks.
- Stopped creating the `PartPresets` folder during runtime setup; existing user files are left untouched but ignored by the editor.
- Updated diagnostics so active editor logs report full proxy mesh state instead of part classifier output.

## 0.5.4

- Tightened the vanilla humanoid part preset so Helmet no longer absorbs upper torso geometry.
- Moved central upper-body and strap geometry back into Torso before arm classification, preventing the right arm from including chest strap pieces.
- Added vanilla preset correction diagnostics for `helmetToTorso`, `otherToTorso`, `armStrapToTorso`, `otherToArm`, and per-part connected-component ranges.
- Preserved the improved vanilla leg classification from 0.5.3 while correcting Helmet, Torso, Arm, and Other cleanup behavior.

## 0.5.3

- Added preset-based part classification for the vanilla Lethal Company humanoid suit, matched by renderer, mesh, material, and texture fingerprint.
- Added optional JSON part preset loading from `BepInEx/config/DrawableSuits/PartPresets` for future modded suit overrides.
- Reworked vanilla Helmet, Torso, Arm, and Leg selection to use the vanilla suit's baked mesh orientation instead of loose broad bounds thresholds.
- Expanded part diagnostics with mesh fingerprints, preset match results, preset triangle counts, and clearer fallback reporting.

## 0.5.2

- Reworked part isolation to use corrected bone-token classification as the primary path, with bounds fallback only for weak or missing bone data.
- Fixed right-side bone names such as `arm.R_lower` being misread as left-side names.
- Rebuilt selected third-person parts as compact proxy meshes so hidden triangles and unused vertices no longer leak into selected-part bounds, colliders, or visuals.
- Changed part cleanup diagnostics to report suspicious tiny components without aggressively reassigning bone-classified triangles.

## 0.5.1

- Reworked suit part classification to use geometry gates first and bone weights only when they match plausible helmet, torso, arm, or leg regions.
- Fixed vanilla helmet detection when suit bones do not expose a usable head/helmet bone, so the Helmet selector can be available from top-cap geometry.
- Added connected-component cleanup for tiny stray triangle islands that previously made torso/limb selections show fragments from other parts.
- Changed part button availability to use visible geometry instead of requiring editable UV pixels, with visible-only warnings when a part cannot paint.
- Reduced false shared-UV warnings by ignoring tiny raster edge overlaps and expanded part classifier diagnostics with raw/cleaned counts, bounds, components, and mapped bones.

## 0.5.0

- Added `All`, `Helmet`, `Torso`, `Left Arm`, `Right Arm`, `Left Leg`, `Right Leg`, and conditional `Other` part selection controls.
- Added automatic proxy classification using recognized bone influences with normalized geometry fallback for modded or unrecognized suit meshes.
- Filtered the third-person avatar proxy and paint collider so only the selected part is shown and targetable while editing.
- Added UV masks that clip paint, erase, decal preview, and decal stamps to the selected part, with overlap diagnostics for suits that reuse UV pixels across body parts.
- Applied part isolation to UV fallback mode by displaying and editing only the selected part's UV islands while retaining full-texture save/load/sync compatibility.

## 0.4.8

- Added a translucent decal placement preview in third-person paint mode using a temporary proxy-only preview texture/material.
- Added a non-raycastable rotated decal preview overlay for UV fallback mode.
- Refactored decal compositing so preview and final stamping use the same placement math.
- Changed Decal tool input to place one decal per mouse click or controller RT press instead of repeatedly stamping while held.
- Added rate-limited `DecalPreviewUpdated`, `DecalPreviewHidden`, and `DecalStampCommitted` diagnostics.

## 0.4.7

- Replaced decal and saved-design list rebuilds with stable row pools so Reset, Save, and Load cannot hit stale list row buttons during the same click.
- Changed Save to preserve only an already selected saved-design row instead of auto-selecting the newly saved file.
- Fixed hue ring and saturation/value handle placement by parenting handles to their own picker rects and using shared coordinate conversion for input and visuals.
- Preserved third-person camera yaw, pitch, and zoom when loading a design or switching suits while the editor is open.
- Reduced repeated world proxy material diagnostics to change-based/rate-limited logs.

## 0.4.6

- Rebuilt the color picker layout so the saturation/value square sits beside the hue ring instead of covering it.
- Replaced the color handle squares with small outline handles and added editable `#RRGGBB` hex color input.
- Added a controller virtual-cursor arming guard so the first `A` press after opening cannot close the menu or switch suits before the stick is moved.
- Removed the duplicate decal refresh button and kept one short-status `Refresh Decals` action.
- Further neutralized normal button highlight/selection state while preserving selected styling for tools, decals, and saves.

## 0.4.5

- Reworked controller virtual-cursor `A` clicks to resolve only actionable UI controls under the cursor, including buttons, input fields, DrawableSuits sliders, and the new color picker.
- Stopped normal buttons from keeping stale selected/highlight colors after unrelated clicks while preserving selected styling for tools, decals, and saved designs.
- Replaced RGB brush sliders with a compact hue-ring and saturation/value color picker with a live hex swatch.
- Shortened the in-game `Refresh Decals` status message so it no longer overlaps the world-help text or diagnostics area.
- Added diagnostics for raw UI raycast hits, resolved virtual-cursor targets, press targets, and release targets.

## 0.4.4

- Made active edited textures player-specific by owner client ID instead of global per suit ID, so editing one player no longer changes every player or rack using the same base suit.
- Added owner client ID to multiplayer texture sync and late-join active design responses.
- Stopped mutating `UnlockableItem.suitMaterial` and suit rack materials for player-specific edits.
- Removed right trigger from third-person camera zoom; right trigger now paints only and controller zoom uses D-pad up/down.
- Disabled the filled UV fallback brush indicator that looked like a second color-changing cursor.
- Added first-person overlay hiding/filtering for nearby helmet, visor, viewmodel, arms, hands, camera, and held-item renderers while the editor is open.
- Added proxy material filtering and diagnostics for visible editor-camera renderers, hidden first-person overlays, proxy material slots, and player-specific sync state.

## 0.4.3

- Replaced direct live local-player rendering in third-person editor mode with a DrawableSuits-owned `DrawableSuitsWorldAvatarProxy`.
- Added renderer candidate scoring and diagnostics so the editor selects a body/suit `SkinnedMeshRenderer` instead of first-person arms, held items, camera children, or viewmodel pieces.
- Hid and restored local player/camera renderers while editing so the third-person camera shows the proxy model without first-person rig clutter.
- Updated world painting to raycast only against the proxy collider layer and to keep the proxy renderer using the active DrawableSuits runtime material.
- Added diagnostics for selected source renderer, hidden renderer count, avatar proxy, proxy material, camera mask, and restore behavior.

## 0.4.2

- Added `AutoDisableBrokenJetpackWarningLateUpdatePatch`, enabled by default, to disable only the broken `JetpackWarning.Patches.PlayerControllerB_LateUpdate_Postfix` after repeated null-reference errors are detected in `BepInEx/LogOutput.log`.
- Made `SessionSafetyCheck` run in `SampleSceneRelay` even when `Camera.main` is null and added diagnostics for `Camera.main`, active cameras, local player flags, prompt-related fields, local renderer materials, and JetpackWarning guard status.
- Kept DrawableSuits editor cameras, paint proxies, brush markers, input locks, and local renderer overrides repaired/restored while `EditorOpen=False`.
- Gated early scene/session material reapplies until unlockables and player scripts are ready, reducing non-editor startup side effects.
- Disabled the startup HUD by default for normal builds; `F9` still toggles the debug HUD when needed.

## 0.4.1

- Added closed-session safety checks on plugin start, scene load, player connect/spawn, and early gameplay frames.
- Destroyed or disabled stray DrawableSuits third-person/preview cameras, paint proxies, and brush markers whenever the editor is closed.
- Restored closed-editor cursor/input/renderer state and added `SessionSafetyCheck` diagnostics for active cameras and local renderer materials.
- Stopped applying DrawableSuits runtime materials to the local first-person arms/body outside the editor by default.
- Added `ApplyLocalFirstPersonArms`, disabled by default and documented as experimental.
- Hardened third-person editor lifetime so the editor camera is created only during open and remains disabled until setup succeeds.
- Added a one-time diagnostic warning when the active BepInEx log shows repeated external `JetpackWarning` `PlayerControllerB.LateUpdate` null-reference errors.

## 0.4.0

- Replaced the UV texture-preview workflow as the default editor with an in-world third-person paint mode.
- Added a third-person editor camera around the local player and force-shows the local suit renderer while editing.
- Added a hidden baked `MeshCollider` paint proxy from `PlayerControllerB.thisPlayerModel` and raycasts from the editor camera to paint/erase/place decals on suit UV coordinates.
- Added a surface brush marker and third-person orbit/zoom controls for mouse and controller.
- Kept the UV texture preview as `Use UV Fallback` and added `StartInUvFallbackMode` plus `ThirdPersonCameraDistance` config entries.
- Replaced fragile ScrollRect/layout decal and save lists with deterministic anchored row buttons and page controls.
- Broadened gameplay input suppression aliases for scan, item use, and inventory switching while the editor is open.
- Updated diagnostics for world camera setup, renderer visibility, paint proxy baking, raycast hits, UV/pixel paint data, and list row creation.

## 0.3.6

- Added PaintAttempt/PaintApplied diagnostics for the texture preview paint path, including pointer source, UV, pixel coordinate, active tool, brush size, opacity, and decal state.
- Prevented Decal mode from silently no-oping when no decal is selected; the editor now keeps Paint active and shows a clear status message.
- Added a brush indicator over the texture preview so small brush sizes are easier to see.
- Disabled and restored known global Lethal Company gameplay actions while the editor is open, including scan, item use, inventory switching, crouch, utility slot, and emote actions where present.
- Added guarded optional Harmony blocks for leaked PlayerControllerB gameplay callbacks so missing method names do not break startup.

## 0.3.5

- Replaced the camera/RenderTexture model preview as the default editor view with a deterministic live suit texture preview.
- Changed paint, erase, and decal placement to map the mouse/controller cursor directly to preview texture coordinates, avoiding the black 3D viewport failure path.
- Added `EnableExperimentalModelPreview`, disabled by default, for the old 3D preview diagnostic path.
- Added black RenderTexture readback detection that falls back to the texture preview when experimental model rendering produces an empty frame.
- Improved preview diagnostics with active preview mode, editable texture details, UI texture assignment, and pointer-to-texture coordinates.

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
