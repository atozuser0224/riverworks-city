# UI assets

Riverworks ships a deliberately small, neutral subset of two official Kenney packs. The light grey surfaces and white glyphs are intended to be tinted by Unity uGUI, so the same files work with the compact charcoal/navy HUD and its muted brass accent without adding bright or bulky artwork.

## Sources and license

| Pack | Official page | Downloaded archive | SHA-256 | License |
| --- | --- | --- | --- | --- |
| UI Pack 2.0 | https://kenney.nl/assets/ui-pack | `Artifacts/Kenney/kenney_ui-pack.zip` | `A8A14A234911EB648C062622915C93E79E94E97CB7F9F375A70F6617F1174318` | Creative Commons Zero (CC0 1.0) |
| Game Icons | https://kenney.nl/assets/game-icons | `Artifacts/Kenney/kenney_game-icons.zip` | `7A86D8D58E0B851E22004B3C70BF90B003632BBF9AC633424DAA3BB17D9E7E4E` | Creative Commons Zero (CC0 1.0) |

Both official asset pages identify their packs as CC0. The original license files are preserved verbatim as `Assets/Resources/UI/Kenney/LICENSE_UI_PACK.txt` and `Assets/Resources/UI/Kenney/LICENSE_GAME_ICONS.txt`. CC0 permits personal, educational, and commercial use; attribution is appreciated by Kenney but is not required. The ignored `Artifacts/Kenney` directory contains the downloaded source archives and inspection copies and is not shipped with the game.

## Selected files and resource paths

The four surface files come from the UI Pack's default-resolution PNG set. The icons come from the Game Icons `White/1x` PNG set (50 x 50 px) and retain transparency so uGUI `Image.color` can tint them.

| Game resource path | Selected source file | Intended use |
| --- | --- | --- |
| `UI/Kenney/panel` | `PNG/Grey/Default/button_rectangle_flat.png` | compact panel background, 6 px sliced border |
| `UI/Kenney/button` | `PNG/Grey/Default/button_rectangle_line.png` | outlined compact button, 6 px sliced border |
| `UI/Kenney/icon_button` | `PNG/Grey/Default/button_square_line.png` | square icon button, 6 px sliced border |
| `UI/Kenney/divider` | `PNG/Extra/Default/divider.png` | thin separator |
| `UI/Kenney/Icons/menu` | `PNG/White/1x/menuList.png` | menu |
| `UI/Kenney/Icons/close` | `PNG/White/1x/cross.png` | close |
| `UI/Kenney/Icons/arrow_left` | `PNG/White/1x/arrowLeft.png` | previous/left |
| `UI/Kenney/Icons/arrow_right` | `PNG/White/1x/arrowRight.png` | next/right |
| `UI/Kenney/Icons/rotate` | `PNG/White/1x/return.png` | rotate/turn |
| `UI/Kenney/Icons/play` | `PNG/White/1x/forward.png` | run/play |
| `UI/Kenney/Icons/pause` | `PNG/White/1x/pause.png` | pause |
| `UI/Kenney/Icons/settings` | `PNG/White/1x/gear.png` | settings |
| `UI/Kenney/Icons/info` | `PNG/White/1x/information.png` | information/help |
| `UI/Kenney/Icons/home` | `PNG/White/1x/home.png` | home/city |
| `UI/Kenney/Icons/map` | `PNG/White/1x/menuGrid.png` | map/build grid |
| `UI/Kenney/Icons/confirm` | `PNG/White/1x/checkmark.png` | confirm/complete |

Use `HudAssets.Panel`, `HudAssets.Button`, `HudAssets.IconButton`, and `HudAssets.Divider` for surfaces. The compact HUD uses the flat `Panel` sprite for ordinary buttons to keep borders subtle. Load glyphs with `HudAssets.Icon(HudAssets.MenuIcon)` or another semantic constant. Missing resources return `null`, allowing the HUD to keep a text-only fallback.

`UiTextureImportSettings` preserves the source dimensions, turns mipmaps off, and imports this small set without texture compression. This keeps the thin glyphs and sliced borders sharp. The PNG source bytes remain unchanged.

## Planned assets (not yet installed)

These are approved directions, not shipped files. Nothing below is referenced by code yet, so the project keeps compiling without them. Versions are deliberately omitted: add each package through Unity Package Manager so the editor resolves the version that matches Unity 6000.5.5f1, then record the resolved version, source and license here.

| Asset | Source | Purpose | Integration point | Risk |
| --- | --- | --- | --- | --- |
| Cinemachine (registry) | Unity Package Manager | Cinematic layer only: town-hall arrival, industry fly-through, photo mode, screen impulse | Add a `CinemachineBrain` on the existing main camera, keep `OrbitCamera` authoritative for gameplay so `CameraRig.Camera` and the existing camera smoke tests stay valid | Medium: gameplay camera must remain the real camera |
| Cinemachine Spline/Dolly | Unity Package Manager (same package) | Train and remote-outpost fly-through for the larger map | New vcam only, activated by the same cue surface used by `CameraCinematics` | Low |
| `com.unity.profiling.core` | Unity Package Manager | Named counters/markers for the new ECS systems | `FactoryController` and `GameController` tick wrappers (Runtime layer only; Core stays Unity-free for the dotnet test suite) | None (editor/runtime instrumentation) |
| `com.unity.recorder` | Unity Package Manager | Deterministic trailer and verification captures | Editor menu only | None |
| Kenney audio packs | https://kenney.nl/assets (CC0 1.0) | Build, UI, ambience one-shots | `Soundscape` lookup table plus `Resources/Audio` | Low |
| Quaternius nature packs | https://quaternius.com (CC0 1.0) | Trees, rocks and ground props to enrich the low-poly board | Decorative prefabs placed by `BoardView` | Low |
| Feel 5.6.1 | Unity Asset Store (paid, local only) | Existing feedback layer | `Tools/Import-Feel.py` as documented in `FEEL_SETUP.ko.md` | Paid asset is never redistributed |

Deliberately excluded: TextMeshPro (the 11/22 px pixel-font contract and the label checks in `CompactHudSmokeTest` depend on legacy `Text`), URP/HDRP (all runtime materials are created from the built-in `Standard` shader), VFX Graph (requires a scriptable render pipeline), and a full DOTS/Burst conversion (Core is a Unity-free library verified by the dotnet suite in `Tests/`).

Installation procedure for each package: add it in Unity Package Manager, confirm `Packages/packages-lock.json` resolves, then run `Tools/Test.ps1 -Runtime -CompactUiRuntime -TutorialRuntime` and record the build GUID in `VERIFICATION.md`. For downloaded archives, store the SHA-256 and license file beside the imported asset, the same way the Kenney files are recorded above.
