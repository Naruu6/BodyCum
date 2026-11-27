# Installation and controls

## Requirements

- Koikatsu CharaStudio on Windows with Direct3D 11.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases).
- [KKAPI 1.42.1 or later](https://github.com/IllusionMods/IllusionModdingAPI/releases). Use the Koikatsu package.
- Extended Save (`ExtensibleSaveFormat.dll`) from the [Koikatsu BepisPlugins package](https://github.com/IllusionMods/BepisPlugins/releases).
- The F1 menu requires the optional [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases).

## Install or update

1. Close CharaStudio.
2. Download **BodyCum.dll** from the [1.0 release](https://github.com/Naruu6/BodyCum/releases/tag/v1.0.0).
3. Back up any previous `BodyCum.dll` and put the downloaded file in `<Koikatsu>/BepInEx/plugins`.
4. Relaunch CharaStudio, select a character, then open **Status → Juices**.

If you used an older modified `KK_SkinEffects.dll` with BodyCum features built in, restore stock SkinEffects before installing BodyCum. The original [SkinEffects plugin](https://github.com/ManlyMarco/Illusion_SkinEffects) can run alongside BodyCum.

## Use

| Control | Effect |
| --- | --- |
| BodyCum | Controls the selected character. Turning it off restores stock Juices on that character's skin. |
| Juice color, Opacity, Density, Brightness | Adjusts the appearance for the selected character. |
| Belly, Tongue | Adjusts the amount on the named area. |
| Hair, Accessory, Clothes | Choose OFF, x1, or x2 for the selected slots. Use each slot selector and its X, Y, and Size controls to place the pattern. |
| All ON / All OFF | Selects or clears all eligible slots for the selected character. |

**F1 → BodyCum → General → Enable BodyCum** controls BodyCum for all characters. Disabling it also restores stock clothing Juices. Character settings and eligible surface placements are stored in the Studio scene. Defaults and the master switch are stored in `BepInEx/config/koikatsu.bodycum.cfg`.

A new hair surface can take a moment to appear while its placement settles. Custom meshes with unusual UVs or transparency can render differently.

## Roll back

Close CharaStudio and replace the installed DLL with your backup. To remove BodyCum, move its DLL out of `BepInEx/plugins`.
