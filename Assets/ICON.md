# AniTV icon

## Current installed design

Current selection: the cartoon `anitv-cartoon-draft.png`, applied to both the executable/shortcut and taskbar. Rebuild with `tools/prepare-cartoon-icon.ps1` (default source), then `tools/build-icon.ps1`. To restore the less-cartoonish alternative, pass `-SourceName anitv-3d-draft.png`. Both source drafts remain unchanged.

### Previous selection

The user-selected cartoon oval television is now used by both surfaces. `tools/prepare-cartoon-icon.ps1` removes the exterior neutral checkerboard by edge-connected flood fill, preserves the dark screen, and centers the silhouette at 94% canvas width. This local processing was explicitly approved by the user. Source: `anitv-cartoon-draft.png`; transparent outputs: `anitv-icon.png`, `anitv-taskbar.png`; multi-resolution icon: `anitv.ico`. The earlier `anitv-3d-draft.png` remains available for rollback. The notes below describe historical designs.

## Separate Windows surfaces

- `anitv-icon.png` and `anitv.ico`: framed design for the executable and desktop shortcuts.
- `anitv-taskbar.png`: enlarged, unframed design for the running window/taskbar.
- WPF decodes the taskbar PNG at 256 x 256. Do not replace it with `BitmapImage(anitv.ico)`, which selects the first 16 x 16 ICO frame.
- `tools/build-icon.ps1` rebuilds only the executable/shortcut ICO. It does not modify the taskbar design.

Current taskbar revision (built-in image generation edit):

Edit target: the attached AniTV application icon. Enlarge the lavender cat-ear television and play triangle to occupy 94 percent of the square image width and height, leaving only 3 percent padding on each side. REMOVE the outer rounded-square border completely. Keep the recognizable cat-ear television silhouette, play triangle, purple/lavender palette and dark navy solid background. Simplify highlights and thicken the television outline slightly for excellent legibility at 16–32 pixels in a Windows taskbar. No lettering, no checkerboard, no mockup, no additional symbols. The central emblem must be visibly much larger than in the input.

The following prompts describe the earlier design used as the edit target.

Generated using the built-in image generation tool. PNG is the source asset;
`anitv.ico` contains 16, 24, 32, 48, 64, 128 and 256 px frames for Windows.
Regenerate the ICO with `powershell -File tools/build-icon.ps1`.

Initial prompt:

Use case: logo-brand. Asset: a single square Windows desktop application icon for AniTV, an anime video player with a dark violet UI. Create an original polished app emblem: a bold luminous lavender play triangle integrated with a minimal stylized anime cat-ear television silhouette, on a dark navy rounded-square tile. Centered, front facing, strong simple silhouette readable at 16 and 32 pixels, generous clear edges, restrained violet gradient and subtle dimensional highlights. Transparent background outside the rounded tile. No lettering, no tiny decorative details, no mockup, no surrounding scene, no watermark. Output one icon.

Final edit prompt (removed the generated checkerboard):

Edit this AniTV icon. Keep exactly the lavender cat-ear television with play triangle and dark navy tile design. Remove ALL checkerboard pattern: replace the entire outer checkerboard margin with a uniform solid dark navy #10121B background matching the tile, and enlarge the tile to nearly fill the square canvas with only 3 percent margin. No transparency, no checkerboard, no new details, no text. This is a square Windows icon raster asset.
