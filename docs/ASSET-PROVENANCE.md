# Rendering resources

BodyCum reads the body mask from the installed game while Studio is running. The 1.0 DLL also ships with the following resources.

| Resource | Where it comes from |
| --- | --- |
| `CustomJuiceColor.bundle` | Modified native Koikatsu skin shader data |
| `CustomJuiceColor.KKUTS_Tess.bundle` | Modified tessellation shader data from [nHaruka's KKUTS v2.3](https://ux.getuploader.com/nHaruka_KK/download/150) |
| `CustomJuiceColor.Hair.bundle` | BodyCum projection HLSL packaged with the Unity 5.6 shader serialization template |
| `CustomJuiceColor.Skin.bundle` | BodyCum skin projection HLSL packaged with the Unity 5.6 shader serialization template |
| `CustomJuiceColor.BustBake.bundle` | BodyCum pattern remapping HLSL packaged with the Unity 5.6 shader serialization template |
| `HairDirectionRegions.png` | BodyCum lookup image for two rotated regions of the body mask |

The game and KKUTS shader data carry their own terms. Redistribution permission for the modified bundles has yet to be verified.

See [resource-hashes.json](../resource-hashes.json) for file sizes and checksums, or the [build guide](BUILD.md) for the local build steps.
