# Rendering resource provenance

This source snapshot preserves the BodyCum 1.0 implementation. It does not
change the existing local DLL or strip features out of that DLL.

The development DLL embeds these five resources:

| Resource | Bytes | Origin recorded by the project |
| --- | ---: | --- |
| `CustomJuiceColor.bundle` | 21,419 | Modified native Koikatsu shader data |
| `CustomJuiceColor.KKUTS_Tess.bundle` | 97,341 | Modified nHaruka KKUTS v2.3 shader data |
| `CustomJuiceColor.Hair.bundle` | 12,622 | Projection shader using the game's shader serialization template |
| `CustomJuiceColor.Skin.bundle` | 13,372 | Projection shader using the game's shader serialization template |
| `AccessoryJuiceMask.png` | 1,532,352 | AI-assisted edit using an exported native body mask as reference |

The shader patch/build recipes are in `ShaderBuild` and the root build
scripts. The mask editing record is in
[ACCESSORY-MASK-PROMPTS.txt](ACCESSORY-MASK-PROMPTS.txt).

The installed KKUTS v2.3 archive identifies nHaruka as its author, but contains
no license/readme or redistribution terms in the inspected manifest. The
[creator's v2.3 download page](https://ux.getuploader.com/nHaruka_KK/download/150)
is the original reference. No blanket redistribution permission was verified
for the Koikatsu-derived resources either.

Before attaching the unchanged DLL or these resources to a public release,
record the applicable permissions or replace/rebuild the affected resources
under terms that permit the intended distribution. Removing loose bundles
from Git does not remove their embedded bytes from an existing DLL.

For local development, `Import-LocalResources.ps1` can read the five resources
from an existing, locally available BodyCum 1.0 DLL. Imported files remain
ignored by Git. Importing is a local preparation step and does not establish
permission to redistribute them.

Rebuilding shader bundles from other inputs is not a promise of byte-identical
output. The final UI-cleanup development DLL has SHA-256:

```text
70F1E19712A435709F7D9D58D036AAF98664CB0C59B406A1BFAA7A8378E7B1F3
```
