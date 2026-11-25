# BodyCum 1.0

![BodyCum cover](docs/images/bodycum-cover.jpg)

A BepInEx plugin for **Koikatsu CharaStudio** that adds per-character controls to **Status → Juices**.

[한국어 설치 안내](docs/INSTALL.ko.md) · [Build from source](docs/BUILD.md) · [Changelog](CHANGELOG.md)

## Current publication status

This repository is being prepared for source publication. **No prebuilt `BodyCum.dll` has been uploaded to Releases yet.** The current plugin version is **1.0.0**; assembly and file versions are **1.0.0.0**.

The four shader bundles and the edited `AccessoryJuiceMask.png` are intentionally excluded while their resource provenance is reviewed. A fresh checkout needs local resource inputs before it can build. See the [build guide](docs/BUILD.md) and [third-party notices](THIRD-PARTY-NOTICES.md).

## Features

- Per-character BodyCum ON/OFF and a separate F1 master switch.
- Color palette, opacity, density, and brightness controls.
- Belly, tongue, accessory, and clothing levels: OFF / x1 / x2.
- Accessory and clothing slot selection, X/Y positioning, and size controls.
- All slots selection for the current character's accessories and clothing.
- Surface placement that follows animation and saves with the scene.

## Requirements and use

- Koikatsu CharaStudio on Windows, using **Direct3D 11**.
- [BepInEx 5](https://github.com/BepInEx/BepInEx/releases).
- [KKAPI 1.42.1 or newer](https://github.com/IllusionMods/IllusionModdingAPI/releases), using the **KK** package.
- Extended Save (`ExtensibleSaveFormat.dll`) from the [KK BepisPlugins package](https://github.com/IllusionMods/BepisPlugins/releases).
- Optional: [ConfigurationManager](https://github.com/BepInEx/BepInEx.ConfigurationManager/releases) for the F1 settings interface.

When you have a locally built DLL, close Studio and place `BodyCum.dll` in `<game>/BepInEx/plugins`. Restart Studio, select a character, then open **Status → Juices**. The mask and shaders are embedded in the built DLL; there are no separate texture files to install.

The Juices BodyCum switch affects the selected character. F1 → BodyCum → General → Enable BodyCum affects all characters while preserving their individual settings. Configuration is stored in `BepInEx/config/koikatsu.bodycum.cfg`.

Original [SkinEffects](https://github.com/ManlyMarco/Illusion_SkinEffects) can coexist with BodyCum. If you use a modified `KK_SkinEffects.dll` that already contains these Juices features, restore the original SkinEffects or remove that modified copy first; BodyCum otherwise remains inactive.

## Compatibility and verification

Body and face overlays assume standard skin UVs. Unusual UVs, shader-deformed meshes, and custom transparency need in-game checking. Fixed front XY accessory projection can compress patterns on side-facing surfaces. Applying the effect to many high-polygon meshes can increase CPU use.

The existing 1.0.0 baseline passed local compilation and static compatibility checks. Native Studio UI, rendering, and complete scene save/reload checks were **not performed for that final baseline**.

## Credits

Derived from work based on ManlyMarco's [Illusion_SkinEffects](https://github.com/ManlyMarco/Illusion_SkinEffects). See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for the included notices and resource information.
