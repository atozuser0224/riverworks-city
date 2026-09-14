# Pixel UI font

Riverworks uses **Galmuri11** as its primary UI font on every platform. Galmuri11 is a 12 px (9 pt) pixel font with all 11,172 modern precomposed Hangul syllables as well as Latin characters and symbols.

## Source and integrity

- Project: Galmuri by Lee Minseo (quiple)
- Release: `v2.40.4`
- Release commit: `bdb86ae89466a361eb8df861222736b81ff975ef`
- Project page: https://quiple.dev/font/galmuri
- Repository: https://github.com/quiple/galmuri
- Release archive: https://github.com/quiple/galmuri/releases/download/v2.40.4/Galmuri-v2.40.4.zip
- Release archive SHA-256: `C8B3D9861A62AE73C8B1178091401CD79994812437EF386413F6DD54856E60E7`
- Archive entry: `Galmuri11.ttf`
- Local file: `Assets/Resources/Fonts/Galmuri11.ttf`
- Font SHA-256: `E24256F42E43713D2EA086A1E1669D78B968F5B3CC547E5C157F0606FFA5DEF1`

The TTF is copied byte-for-byte from the official release archive. It is licensed under the SIL Open Font License 1.1. The archive's `LICENSE.txt` is copied byte-for-byte to `Assets/Resources/Fonts/Galmuri-OFL.txt` (SHA-256 `86A3EE9495F942F0243F18C103DA9FACA27ADB88142613EDB8BB852E56C892C1`). Existing Nanum Gothic licensing remains in `Assets/Resources/Fonts/OFL.txt` and `Assets/Resources/Fonts/ATTRIBUTION.md`.

## Unity integration

- Resource path: `Fonts/Galmuri11`
- Runtime API: `Riverworks.GameFont.Load()`
- Asset-only verification API: `Riverworks.GameFont.LoadBundledPixelFont()`

`GameFont.Load()` caches the result and selects Galmuri11 before all platform-specific fallbacks. The font importer uses its native 12 px design size with hinted raster rendering. The runtime applies point filtering to the dynamic atlas and reapplies it after atlas rebuilds.

Tests that require the pixel font asset should assert that `LoadBundledPixelFont()` returns a font before testing `Load()`. This prevents the normal OS, Nanum Gothic, or Unity built-in fallback from hiding a missing Galmuri asset.
