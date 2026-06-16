# DrawableSuits

DrawableSuits is a Lethal Company v81 BepInEx mod for drawing directly on your currently worn suit. It includes a third-person suit editor, an always-visible UV texture panel, decals, stickers, text, saved designs, and shareable design codes.

Current version: `0.5.70`

## What It Does

- Edits only the local player's currently worn suit.
- Shows a frozen third-person editor avatar so idle breathing does not move the paint target.
- Keeps the UV texture panel visible beside the third-person view for direct texture editing.
- Applies full-suit texture edits; there is no part picker.
- Supports player-specific multiplayer apply/save sync for other players using DrawableSuits.
- Saves designs as local files and exports/imports portable `DSUIT2:` share codes.

## Tools

- `Paint`: draw on the suit or UV panel.
- `Erase`: restore pixels toward the base suit texture.
- `Fill`: flood-fill a contiguous matching color region.
- `Decal`: place PNG/JPG decals from the Decals menu.
- `Sticker`: place built-in shapes such as stars, hearts, arrows, rings, and shields.
- `Text`: stamp one line of Arial text using the brush color and opacity.
- `Eyedropper`: sample a color from the suit, then return to the previous tool.
- `Mirror`: UI toggle that mirrors Paint, Erase, Fill, Decal, Sticker, and Text onto the opposite suit surface when possible.

Paint and Erase support brush shapes: Circle, Square, Pixel, Spray Paint, Soft Airbrush, and Noise/Scatter.

Decals and stickers can be temporarily edited before placement with crop, stretch, flip, and filter controls. These edits affect previews and future stamps only; original decal files and built-in sticker sources are not changed.

## Editor Controls

### Keyboard And Mouse

- Pause menu `DrawableSuits`, `F8`, or `F10`: open the editor.
- Left mouse: paint/erase continuously, fill once, or stamp Decal/Text/Sticker once.
- Right mouse drag: orbit the third-person camera, or pan the UV panel when over the panel.
- Mouse wheel: zoom the third-person camera, or zoom the UV panel when over the panel.
- Ctrl + mouse wheel: change brush size.
- `,` / `.`: rotate Decal or Sticker placement left/right by 5 degrees.
- `[` / `]`: rotate the UV panel left/right by 90 degrees.
- `Escape` or `Close`: close the editor.

### Controller

- `View/Back + Y`: open or close the editor.
- Left stick: move the editor cursor.
- `A`: click the UI control under the cursor.
- Right trigger: paint/erase, fill once, sample, or stamp once depending on the active tool.
- Right stick: orbit the third-person camera, or pan the UV panel when over it.
- `LB` / `RB`: rotate the UV panel left/right by 90 degrees.
- D-pad up/down: zoom the third-person camera, or zoom the UV panel when over it.
- D-pad left/right: rotate Decal or Sticker placement by 5 degrees.
- `Y`: cycles Paint, Erase, and Decal.
- `X`: undo.
- Start: save.

Most newer tools and modifiers are UI-only to avoid shortcut clutter.

## Decals, Stickers, And Text

- Open `Decals` to select, add, refresh, or delete decal image files.
- `Add Decal` copies a selected PNG/JPG/JPEG into the DrawableSuits Decals folder using an external Windows picker process.
- Open `Stickers` to choose a built-in shape.
- Decal and Sticker world previews wait until the cursor stops moving before rebuilding, which keeps movement smooth.
- Text, Decal, and Sticker stamps are baked into the suit texture after placement. They are not editable layers afterward.

## Designs, Codes, And History

- `Save` writes the current design to disk.
- `Designs` opens saved designs for the current suit editor.
- `Apply` syncs the current edited texture to other DrawableSuits users.
- `Export Code` copies a compact lossless `DSUIT2:` code.
- `Import Code` loads a `DSUIT2:` or legacy `DSUIT1:` code into the current suit without auto-saving or auto-applying.
- Undo History labels actions such as Brush stroke, Erase, Decal placed, Sticker placed, Text placed, Color fill, Reset, Load design, and Import code.
- `Undo Selected` removes only the selected history action where the snapshot diff can be isolated.
- `Clear History` clears undo/redo history without changing the current texture.

## Folders

DrawableSuits creates these folders under `BepInEx/config/DrawableSuits`:

- `Saves`: saved design metadata.
- `Textures`: saved design PNG textures.
- `Decals`: user decal images.
- `Logs`: diagnostics logs.

Manual decal placement still works: copy images into `Decals`, open the Decals menu, and press `Refresh`.

## Compatibility Notes

DrawableSuits works best with vanilla-style suits and modded suits that expose a normal `suitMaterial`. Designs can be loaded across suits, but different UV layouts may stretch or misplace drawings.

DrawableSuits is not compatible with ModelReplacementAPI. Replacement models can use separate renderers and materials that DrawableSuits cannot safely map to the current suit texture, so the editor may show incorrect geometry, duplicate helmets, or uneditable surfaces. Use the UV panel where possible or disable the replacement while editing.

Players without DrawableSuits can join normally, but they will see original suit textures.

## Support

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/B0M720UWJS)

Donations help support continued DrawableSuits development, and commissions are available.

## Troubleshooting

- Check `BepInEx/config/DrawableSuits/Logs/diagnostics.log` for DrawableSuits editor, input, painting, decal, sticker, sync, and import/export details.
- In Gale, also check the active profile's `BepInEx/LogOutput.log` and search for `DrawableSuits`.
- If `Add Decal` is unavailable or canceled, place the image in the Decals folder manually and press `Refresh`.
- If Mirror cannot find the opposite surface, DrawableSuits applies the primary edit only and logs the skipped mirror target.
- If a modded or replacement suit displays incorrectly, use the UV panel fallback; ModelReplacementAPI is not supported.
