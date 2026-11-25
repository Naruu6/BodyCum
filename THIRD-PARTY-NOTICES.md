# Credits and third-party materials

## Source code

BodyCum was separated from a modified integration based on ManlyMarco's
[Illusion_SkinEffects 3.0.2](https://github.com/ManlyMarco/Illusion_SkinEffects).
The existing [GNU GPL version 3 license](LICENSE) is retained. Original
copyrights remain with their respective holders. BodyCum is not an official
ManlyMarco or IllusionMods release.

BodyCum's additions include character-specific controls, color and amount
settings, belly and tongue controls, accessory/clothing slot controls,
surface-following placement, and scene persistence. The GitHub preparation
does not change the 1.0 plugin implementation.

## External build and runtime components

The following projects are referenced from a separately installed game or
tool installation. Their binaries are not included in this source repository:

- [BepInEx](https://github.com/BepInEx/BepInEx)
- [Illusion Modding API / KKAPI](https://github.com/IllusionMods/IllusionModdingAPI)
- [BepisPlugins / Extended Save](https://github.com/IllusionMods/BepisPlugins)
- Unity and Koikatsu game assemblies
- SB3Utility and its dependencies, for local shader rebuilding
- Microsoft Roslyn or a .NET SDK, for compilation

The code license does not replace these components' own licenses.

## Rendering resources

The development DLL contains four shader bundles made using installed
Koikatsu and nHaruka KKUTS inputs, plus an accessory mask edited using an
exported game mask as a reference. The inspected files do not establish
redistribution permission for those inputs. This is an unresolved provenance
question, not a finding that the author has prohibited redistribution.

The five resource binaries are therefore not included in the public source
staging tree. The unchanged development DLL is also not staged as a public
release asset. See [resource provenance](docs/ASSET-PROVENANCE.md) and
[building](docs/BUILD.md).

## Documentation artwork

The cover is AI-generated artwork with separately typeset Gorvex lettering.
The font file is not included. The source-code license does not grant rights
to any third-party typeface, game assets, or character designs.
