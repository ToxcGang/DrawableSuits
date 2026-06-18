# DrawableSuits

DrawableSuits is a Lethal Company BepInEx mod for drawing on your currently worn suit from inside the game.

Current version: `0.5.76`

## Features

- Third-person suit editor with a frozen avatar pose for stable painting.
- Always-visible UV texture panel for direct texture editing.
- Paint, Erase, Fill, Decal, Sticker, Text, Eyedropper, and Mirror tools.
- Brush shapes, recent colors, undo history, saved designs, and shareable `DSUIT2:` design codes.
- Decal and sticker temporary edits for crop, stretch, flip, and filters.
- Multiplayer Apply/Save sync for other players who also have DrawableSuits installed.
- Applied designs can be reused on matching dead bodies/ragdolls.

## Basic Controls

- Pause menu `DrawableSuits`, `F8`, or `F10`: open the editor.
- Left mouse / right trigger: paint, erase, fill, sample, or stamp depending on the active tool.
- Right mouse drag / right stick: orbit the editor camera, or pan the UV panel when over it.
- Mouse wheel / D-pad up/down: zoom the camera, or zoom the UV panel when over it.
- `[` / `]` or controller `LB` / `RB`: rotate the UV panel.
- `,` / `.` or D-pad left/right: rotate Decal or Sticker placement.
- `Escape` or `Close`: close the editor.

Most advanced tools are UI-only so controller and keyboard shortcuts stay limited.

## Tools

- `Paint`: draw with the current brush color, opacity, and brush shape.
- `Erase`: restore pixels toward the base suit texture.
- `Fill`: flood-fill a contiguous matching color region.
- `Decal`: stamp user PNG/JPG images from the Decals folder.
- `Sticker`: stamp built-in shapes such as stars, hearts, arrows, rings, and shields.
- `Text`: stamp one line of Arial text using the brush color and opacity.
- `Eyedropper`: sample a color from the suit or UV panel.
- `Mirror`: mirror supported edits to the opposite suit surface when a target is found.

## Decals

Decals are loaded from:

`BepInEx/config/DrawableSuits/Decals`

Use the Decals menu button `Copy Folder Path`, place `.png`, `.jpg`, or `.jpeg` files in that folder manually, then press `Refresh`.

DrawableSuits does not launch an OS file picker, shell, Explorer window, or external helper process for decals.

## Designs And Codes

- `Save` writes the current design to disk.
- `Designs` opens saved designs for the current suit.
- `Apply` updates the visible runtime suit and broadcasts the design in multiplayer.
- `Export Code` copies a portable `DSUIT2:` share code.
- `Import Code` loads a code locally; it does not broadcast until `Apply` or `Save`.
- `Clear History` clears undo/redo history without changing the current texture.

## Multiplayer

Multiplayer sync uses Unity Netcode named messages.

Designs sync only when you press `Apply` or `Save`; live brush strokes are not sent. The edited texture is encoded as PNG bytes, chunked, sent reliably, rebuilt by other clients, and applied to the matching player/suit.

All players who should see edited suits need DrawableSuits installed, network sync enabled, and compatible versions. Players without DrawableSuits can still join, but they will see original suit textures.

## Compatibility

DrawableSuits works best with vanilla-style suits and suit mods that expose the normal suit material/texture layout.

DrawableSuits is not compatible with ModelReplacementAPI. Replacement models can use separate meshes, renderers, or materials that DrawableSuits cannot safely map to the suit texture, which can cause duplicate helmets, incorrect geometry, or uneditable surfaces.

## Support

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/B0M720UWJS)

Donations help support continued DrawableSuits development, and commissions are available.

## Development Note

DrawableSuits has used AI-assisted development for implementation, text, debugging, and error fixing.

## Troubleshooting

- DrawableSuits logs to `BepInEx/config/DrawableSuits/Logs/diagnostics.log`.
- Gale/BepInEx also writes the profile log at `BepInEx/LogOutput.log`.
- If decals do not appear, copy images into the Decals folder and press `Refresh`.
- If multiplayer designs do not appear, confirm all players have DrawableSuits installed and press `Apply` or `Save`.
- If a modded or replacement suit displays incorrectly, use the UV panel where possible; ModelReplacementAPI is unsupported.
