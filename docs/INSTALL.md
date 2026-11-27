# Installing BodyCum

BodyCum runs in Koikatsu CharaStudio on Windows with Direct3D 11. Install [BepInEx 5](https://github.com/BepInEx/BepInEx/releases), the Koikatsu version of [KKAPI 1.42.1 or later](https://github.com/IllusionMods/IllusionModdingAPI/releases), and Extended Save from the [Koikatsu BepisPlugins package](https://github.com/IllusionMods/BepisPlugins/releases). Add [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) if you want the F1 menu.

## Install or update

1. Close CharaStudio.
2. Get `BodyCum.dll` from the [latest release](https://github.com/Naruu6/BodyCum/releases/latest). If you're updating, back up your old DLL first.
3. Put the DLL in `BepInEx/plugins` under your Koikatsu folder. Launch Studio, select a character, and open **Status → Juices**.

BodyCum works alongside the original [SkinEffects](https://github.com/ManlyMarco/Illusion_SkinEffects). If your copy of `KK_SkinEffects.dll` already has BodyCum's controls built in, restore the original SkinEffects DLL first.

## Use the controls

- **BodyCum** turns the effect on or off for the selected character. **Juice color**, **Opacity**, **Density**, and **Brightness** change how it looks.
- **Belly** and **Tongue** each have an amount setting. For **Hair**, **Accessory**, and **Clothes**, choose a slot, pick a level, and adjust **X**, **Y**, and **Size** to place the pattern. **All ON** and **All OFF** select or clear eligible slots together.
- **F1 → BodyCum → General → Enable BodyCum** applies to every character. Turning it off also restores stock clothing Juices.

Character settings and surface placements are saved with the scene. Global defaults and the F1 master switch live in `BepInEx/config/koikatsu.bodycum.cfg`.

When you open a saved scene, the effect appears as soon as the hair accessory can be rendered. When you add or change a character, the hair effect may take a moment to appear. Custom meshes with unusual UVs or transparency may look different.

## Roll back

Close CharaStudio and replace the installed DLL with your backup. To uninstall BodyCum, move its DLL out of `BepInEx/plugins`.
