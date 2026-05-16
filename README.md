# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod that lets players draw on suits in-game, place image decals, save and load designs, and apply edited textures to vanilla or modded suits.

## Features

- In-game suit editor opened from the pause menu with the `DrawableSuits` button below Resume.
- Fallback shortcuts: `F8` on keyboard or `View/Back + Y` on controller, handled through Unity's Input System with legacy input as a backup.
- Emergency diagnostics overlay shortcut: `F10`.
- Lightweight debug HUD: shown briefly on startup and toggleable with `F9`.
- Controller support: use the pause-menu button or shortcut to open the editor, left stick moves the virtual cursor, right trigger paints, bumpers rotate the preview, `Y` cycles tools, `X` undoes, `Start` saves, and `A` applies.
- Paint, erase, undo, redo, reset, and adjustable brush size, color, and opacity.
- Editor rendered as a Unity UI overlay instead of IMGUI, so it appears above the game HUD.
- 3D suit preview painting by raycasting against a baked suit mesh and writing to the suit texture UVs.
- PNG/JPG decals from `BepInEx/config/DrawableSuits/Decals`, plus an optional Windows file dialog import button.
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
- Optional OS file dialog import.

## Debugging

DrawableSuits writes detailed startup, pause-menu, input, editor, canvas, suit-detection, and preview logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.

Press `F10` to open the emergency diagnostics overlay. The overlay is designed to appear even when no editable suit, local player model, main camera, or preview collider is available. If painting is disabled, the overlay shows the exact missing state.

DrawableSuits also shows a lightweight debug HUD for the first 30 seconds after plugin load. Press `F9` to pin or hide that HUD. If you do not see the startup HUD and `diagnostics.log` is not created, the plugin is not loading from the active mod profile.

## Known Limits

- The editor uses the local player model as the baked preview mesh. If no player model is available yet, the diagnostics overlay still opens and reports that missing dependency.
- If keyboard or controller shortcuts do not open the editor, use the `DrawableSuits` button in the pause menu.
- If the pause-menu button and shortcuts do nothing, press `F9`. If the debug HUD appears, the plugin loaded and the HUD will show whether the editor canvas is active.
- If the editor opens with a player-model warning, join a lobby and wait until the local player model has spawned before painting.
- If the cursor appears without the editor UI, check `BepInEx/config/DrawableSuits/Logs/diagnostics.log` for canvas creation, EventSystem, and open-failure messages.
- If the pause-menu button overlaps another menu item after updating, restart the game so the old injected menu object is cleared.
- Cross-suit loading depends on UV compatibility.
- Very large decal images are resized to the configured maximum texture size.
- Multiplayer sync is designed for applied designs, not every brush stroke.
