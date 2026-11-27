# Rendering sources

BodyCum reads the installed game's live body mask at runtime. The 1.0.0 DLL embeds the following rendering resources.

| Resource | Input |
| --- | --- |
| `CustomJuiceColor.bundle` | Native Koikatsu skin shader data |
| `CustomJuiceColor.KKUTS_Tess.bundle` | nHaruka's KKUTS v2.3 tessellation shader data |
| `CustomJuiceColor.Hair.bundle` | BodyCum projection HLSL and the Unity 5.6 shader serialization template |
| `CustomJuiceColor.Skin.bundle` | BodyCum skin projection HLSL and the Unity 5.6 shader serialization template |
| `CustomJuiceColor.BustBake.bundle` | BodyCum pattern remapping HLSL and the Unity 5.6 shader serialization template |
| `HairDirectionRegions.png` | BodyCum region lookup for two rotated portions of the regular body mask |

The exact hashes and sizes are recorded in [resource-hashes.json](../resource-hashes.json). See the [build guide](BUILD.md) for generating or importing the resources and [third-party notices](../THIRD-PARTY-NOTICES.md) for credit and licensing information.
