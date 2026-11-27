# Changelog

## 1.0 Final

- Hair accessory effects appear on the first rendered frame after a Studio scene loads. Older scenes with no saved placement calculate it on that frame, and scenes with a saved placement reuse it.
- Tested scene reloads with and without saved placement in CharaStudio. The recorded placement stayed stable after the effect first appeared.

## 1.0.0

- Overlapping strands on hair accessories get a small sideways shift so the pattern stays close to the original frontal placement.
- Hair effects appear after their placement settles. If the scene has a saved placement for the same mesh, BodyCum uses it again.
- Clothing settings follow the slot when you change outfits. Stock underwear uses its original cutout mask. While the F1 master switch is on, BodyCum handles the clothing effect.
- Regular hair, accessories, and clothes each have **Off**, **x1**, and **x2**. Slot selections, position, and size are saved for each character.

CharaStudio tests covered character loading, card swaps, and saved placement with two long-hair accessories.

The plugin ID is `koikatsu.bodycum`. The plugin version is `1.0.0` and the DLL file version is `1.0.0.0`.
