# BodyCum 1.0

![BodyCum cover](docs/images/bodycum-cover.jpg)

BodyCum is a BepInEx plugin that adds per-character controls to **Status → Juices** in Koikatsu CharaStudio. It lets you adjust effect color, opacity, density, and brightness, and apply the effect to selected hair, accessory, and clothing slots.

**[Download BodyCum.dll](https://github.com/Naruu6/BodyCum/releases/tag/v1.0.0)**

1. Close CharaStudio.
2. Place `BodyCum.dll` in `BepInEx/plugins` under your Koikatsu installation.
3. Launch CharaStudio, select a character, and open **Status → Juices**.

BodyCum requires Windows, Direct3D 11, BepInEx 5, KKAPI 1.42.1 or later, and Extended Save. The [installation and controls guide](docs/INSTALL.md) lists the dependencies and explains how the settings apply.

## Features

- Per-character BodyCum ON/OFF and a separate F1 master switch.
- OFF, x1, and x2 for Belly, Tongue, regular hair, accessories, and clothing.
- Individual and bulk slot selection, plus X/Y position and size controls.
- Surface-following placement for accessories and clothing, with placement stored in the scene.
- Smaller horizontal shifts between overlapping hair accessory strands to keep the original frontal pattern recognizable.

## Source and credits

See the [changelog](CHANGELOG.md), [build guide](docs/BUILD.md), and [third-party notices](THIRD-PARTY-NOTICES.md). BodyCum was developed from work based on ManlyMarco's [Illusion_SkinEffects](https://github.com/ManlyMarco/Illusion_SkinEffects). The source is available under the [GNU General Public License v3](LICENSE).
