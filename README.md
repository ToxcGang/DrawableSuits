# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits in-game, place image decals, save and load designs, and apply edited textures to vanilla or modded suits.

## Features

- In-game suit editor opened from the pause menu with the `DrawableSuits` button below Resume.
- Fallback shortcuts: `F8` on keyboard or `View/Back + Y` on controller, handled by the stable DrawableSuits runtime host.
- Emergency open shortcut: `F10`, which opens the editor shell and never toggles it closed.
- Lightweight debug HUD: shown briefly on startup and toggleable with `F9`.
- Controller support: use the pause-menu button or shortcut to open the editor, move the virtual cursor with the left stick, click the control under the cursor with `A`, paint with right trigger, rotate with bumpers, cycle tools with `Y`, undo with `X`, and save with Start.
- Paint, erase, undo, redo, reset, apply, save, load, and adjustable brush size, color, and opacity.
- Editor rendered as a Unity UI overlay instead of IMGUI, so it appears above the game HUD.
- 3D suit preview rendered inside the editor with a disabled manual-render preview camera and RenderTexture, isolated from the gameplay world.
- 3D suit preview painting by raycasting through the editor preview viewport against a baked suit mesh and writing to the suit texture UVs.
- PNG/JPG decals from `BepInEx/config/DrawableSuits/Decals`. The in-game OS file dialog is disabled for stability in Gale/Unity.
- Decal placement with adjustable decal size and rotation.
- Reusable saved designs stored as JSON metadata plus PNG texture files.
- Apply/save multiplayer sync for other players who also have DrawableSuits installed.
- Vanilla and modded suit support as long as the suit uses a normal suit material and texture.

## Install

1. Install BepInExPack for Lethal Company.
2. Build this project with `dotnet build -c Release`.
3. Copy `dist/DrawableSuits/BepInEx/plugins/DrawableSuits/DrawableSuits.dll` into your mod profile, or package the `dist/DrawableSuits` folder for Thunderstore.

## Folders

DrawableSuits creates these folders after launch:

- `BepInEx/config/DrawableSuits/Saves` stores `.json` design metadata.
- `BepInEx/config/DrawableSuits/Textures` stores saved design `.png` textures.
- `BepInEx/config/DrawableSuits/Decals` stores user decal images.
- `BepInEx/config/DrawableSuits/Logs` stores the diagnostics log.

Put `.png`, `.jpg`, or `.jpeg` files in `Decals` and press `Refresh` in the editor to use them.

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
- Max sync payload size.
- Sync chunk size.
- OS file dialog import config remains listed as disabled/experimental; 0.3.3 ignores it in-game for stability.

## Debugging

DrawableSuits writes detailed startup, pause-menu, input, editor, canvas, suit-detection, and preview logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.

When testing with Gale, also search `BepInEx/LogOutput.log` in the active Gale profile for `DrawableSuits`. The expected 0.2.x startup sequence includes plugin `Awake`, plugin `Start`, runtime host `Start`, and runtime host first `Update`.

Press `F10` to open the emergency editor shell. The shell is designed to appear even when no editable suit, local player model, main camera, or preview collider is available. If painting is disabled, the shell shows the exact missing state.

DrawableSuits also shows a lightweight runtime HUD for the first 30 seconds after plugin load. Press `F9` to pin or hide that HUD. If you do not see the startup HUD and `diagnostics.log` is not created, the plugin is not loading from the active mod profile.

Lethal Company v81 runs with Unity's Input System path. DrawableSuits 0.3.3 reads `F8`, `F9`, `F10`, Escape, mouse movement, mouse buttons, mouse wheel, and controller controls through that path so `UnityEngine.Input` errors should not repeat in `LogOutput.log`.

## Known Limits

- The editor uses the local player model as the baked preview mesh, normalizes it around the preview origin, then renders it through an isolated preview camera inside the editor panel. If no player model is available yet, the diagnostics overlay still opens and reports that missing dependency.
- If keyboard or controller shortcuts do not open the editor, use the `DrawableSuits` button in the pause menu.
- If the pause-menu button and shortcuts do nothing, press `F9`. If the debug HUD appears, the plugin loaded and the HUD will show whether the editor canvas is active.
- If the world turns white when the editor opens, make sure the installed package is `0.3.3` or newer. The preview light/camera should no longer render into or light the gameplay world.
- If the player preview is missing, check `diagnostics.log` for `RebuildPreview complete`, `previewLayer`, `cameraEnabled=False`, `normalizedBounds`, and `renderTexture` entries.
- If sliders appear as large orange blocks, make sure the installed package is `0.3.3` or newer. Sliders now use DrawableSuits' own slider control instead of Unity's `Slider`.
- If mouse clicks do not activate the menu, check `diagnostics.log` for `UiInputDiagnostics` raycast hits and confirm the active module is `InputSystemUIInputModule`.
- While the editor is open, DrawableSuits disables the local player's movement action map, locks movement/look/interact flags, and Harmony-blocks jump/look/interact callbacks so controller input drives the editor instead of jumping or walking the player.
- Controller `A` is not bound to Unity UI submit. DrawableSuits raycasts from the visible virtual cursor and clicks only the control under that cursor.
- If controller clicks seem offset, check `diagnostics.log` for `Virtual cursor A press`, `UiInputDiagnostics`, `canvasScale`, and `raycastHits`.
- Pressing `Refresh Decals` is safe in-game. To import decals, place `.png`, `.jpg`, or `.jpeg` files in `BepInEx/config/DrawableSuits/Decals`, then press `Refresh Decals`.
- If you quit to the main menu while the editor is open, DrawableSuits closes the editor during the scene change so main-menu navigation is restored.
- If `LogOutput.log` contains repeated `Legacy key polling failed` or `InvalidOperationException` entries from DrawableSuits, make sure the installed package is `0.3.3` or newer.
- If the editor opens with a player-model warning, join a lobby and wait until the local player model has spawned before painting.
- If the cursor appears without the editor UI, check `BepInEx/config/DrawableSuits/Logs/diagnostics.log` for canvas creation, EventSystem, and open-failure messages.
- If the pause-menu button overlaps another menu item after updating, restart the game so the old injected menu object is cleared.
- Cross-suit loading depends on UV compatibility.
- Very large decal images are resized to the configured maximum texture size.
- Multiplayer sync is designed for applied designs, not every brush stroke.
