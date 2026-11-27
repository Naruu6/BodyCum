# Changelog

## 1.0.0

- Reduced the horizontal pattern offset on overlapping hair accessory strands to one quarter of its previous value. This keeps the result closer to the frontal placement.
- Hair and accessory patterns first appear when surface placement is ready. Existing scene coordinates are reused when the mesh matches.
- Clothing slot selections follow the slot when its item changes. Stock bras and underwear use the original material's two-channel cutout rules. BodyCum controls the displayed clothing effect while the F1 master switch is enabled.
- Regular hair, accessories, and clothing each have OFF, x1, and x2 levels. Their position and size controls are stored per character in the scene.
- The established BodyCum ID and scene data remain compatible with earlier versions.

The release was verified in CharaStudio with two long-hair accessories. The tests covered character loading and replacement, restoration of saved placement, and the absence of a second pattern shift after the effect first appeared.

Plugin version `1.0.0`. Assembly and file version `1.0.0.0`. Plugin ID `koikatsu.bodycum`.
