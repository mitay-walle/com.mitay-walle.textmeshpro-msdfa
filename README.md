# TextMeshPro MSDFA

Proof-of-concept Unity package for TextMeshPro atlases with MSDFA/MTSDF-style data: MSDF in RGB plus SDF alpha, rendered through `TextMeshPro/MSDFA`.

It is experimental. The package patches embedded `com.unity.ugui`, so pin Unity/UGUI versions and treat the API/serialized data as unstable.

## Features

- `MSDFA Atlas` toggle in `Window > TextMeshPro > Font Asset Creator`.
- `MSDFA Atlas` field in TMP Font Asset Inspector > `Generation Settings`.
- RGBA32 distance-field atlas path and `TextMeshPro/MSDFA` shader.
- Burst-backed glyph rendering for supported TrueType `glyf` outlines.
- SDF fallback when the PoC outline path cannot render a glyph.

## Screenshots

![Font Asset Creator MSDFA Atlas toggle](Documentation~/images/tmp-msdfa-font-asset-creator.png)

![TMP Font Asset Inspector MSDFA Atlas field](Documentation~/images/tmp-msdfa-font-asset-inspector.png)

## Installation

Add the package from Git URL in Unity Package Manager:

```text
https://github.com/mitay-walle/com.mitay-walle.textmeshpro-msdfa.git#v0.1.0
```

Then run:

```text
Tools > TextMeshPro MSDFA > Embed UGUI And Apply Patch
```

The patcher embeds `com.unity.ugui`, applies `ugui-msdfa-package.patch`, and adds the `TMP_MSDFA_UGUI_PATCHED` define to TMP runtime/editor asmdefs. Git CLI must be available in `PATH`.

## Usage

1. Open `Window > TextMeshPro > Font Asset Creator`.
2. Select a source font.
3. Pick a distance-field render mode: `SDF`, `SDF8`, `SDF16`, `SDF32`, `SDFAA`, or `SDFAA_HINTED`.
4. Enable `MSDFA Atlas`.
5. Generate and save the font asset.

For existing font assets, enable `MSDFA Atlas` in the TMP Font Asset Inspector. The package switches compatible materials to `TextMeshPro/MSDFA`.

## Tested

Verified in this project with:

- Unity `6000.3.2f1` / Unity 6.3 LTS;
- Windows Editor, Android target;
- `com.unity.ugui` `2.0.0`, embedded and patched;
- `com.unity.burst` `1.8.27`;
- `com.unity.collections` `2.6.3`.

`package.json` declares Unity `2022.3`, but this PoC has only been checked on the version above.

## Memory Footprint

MSDFA uses `TextureFormat.RGBA32` for distance-field atlases. Standard TMP SDF usually uses `Alpha8`, so atlas memory is about 4x higher at the same resolution, mipmaps off.

| Atlas | SDF `Alpha8` | MSDFA `RGBA32` |
| --- | ---: | ---: |
| 512x512 | 0.25 MiB | 1 MiB |
| 1024x1024 | 1 MiB | 4 MiB |
| 2048x2048 | 4 MiB | 16 MiB |

Local stress result for glyph `V`, 1024x1024 atlas, 250 runs:

- MSDFA: `RGBA32`, `2.189 ms/run`;
- SDF: `Alpha8`, `0.521 ms/run`.

Dynamic generation can also allocate a temporary `Alpha8` scratch texture for fallback, source font bytes, and parsed glyph-shape cache per `TMP_FontAsset`. Profile target builds before production use.

## Limitations

- PoC, not production-ready.
- Patch compatibility depends on the tested `com.unity.ugui` source layout.
- Only distance-field TMP render modes use MSDFA.
- The outline parser targets TrueType `glyf`; unsupported fonts/glyphs fall back to SDF in the RGBA atlas.
- A cleaner long-term solution would be native TextCore glyph-outline access and `GlyphRenderMode.MSDF`/`GlyphRenderMode.MTSDF`.

## References

- [Unity Discussions: TextMeshPro/TextCore MSDF request](https://discussions.unity.com/t/2026-could-textmeshpro-textcore-ever-support-multi-channel-signed-distance-fields/1730024)
- [Chlumsky/msdf-atlas-gen](https://github.com/Chlumsky/msdf-atlas-gen)
- [Chlumsky/msdfgen](https://github.com/Chlumsky/msdfgen)
