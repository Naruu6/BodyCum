using Studio;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        internal bool TryGetAllSlotSelection(OCIChar actor, out bool allSelected)
        {
            allSelected = false;
            var character = actor?.charInfo;
            if (!character)
                return false;

            var any = false;
            var complete = true;
            var accessory = Get(actor);
            var accessories = character.objAccessory;
            if (accessories != null)
                for (var slot = 0; slot < accessories.Length; slot++)
                {
                    if (!TryGetAccessory(character, slot, out _, out var identity))
                        continue;
                    any = true;
                    if (accessory == null || accessory.Level == 0 ||
                        !accessory.Slots.TryGetValue(slot, out var selected) || selected != identity)
                        complete = false;
                }

            var clothes = GetClothes(actor);
            for (var slot = 0; slot < ClothesJuiceParts.SlotCount; slot++)
            {
                if (!ClothesJuiceParts.TryGet(character, slot, out _, out var identity))
                    continue;
                any = true;
                if (clothes == null || clothes.Level == 0 ||
                    !clothes.Slots.TryGetValue(slot, out var selected) || selected != identity)
                    complete = false;
            }

            allSelected = any && complete;
            return any;
        }

        internal void SetAllSlots(OCIChar actor, bool enabled)
        {
            var character = actor?.charInfo;
            if (!character)
                return;

            // Only change selection and enable an OFF tier when selecting its first valid slot.
            // Keep the existing transforms and surface references on both paths.
            if (!enabled)
            {
                Get(actor)?.Slots.Clear();
                GetClothes(actor)?.Slots.Clear();
                return;
            }

            var accessories = character.objAccessory;
            if (accessories != null)
                for (var slot = 0; slot < accessories.Length; slot++)
                {
                    if (!TryGetAccessory(character, slot, out _, out var identity))
                        continue;
                    var profile = Edit(actor);
                    profile.Slots[slot] = identity;
                    if (profile.Level == 0)
                        profile.Level = 1;
                }

            for (var slot = 0; slot < ClothesJuiceParts.SlotCount; slot++)
            {
                if (!ClothesJuiceParts.TryGet(character, slot, out _, out var identity))
                    continue;
                var profile = EditClothes(actor);
                profile.Slots[slot] = identity;
                if (profile.Level == 0)
                    profile.Level = 1;
            }
        }
    }
}
