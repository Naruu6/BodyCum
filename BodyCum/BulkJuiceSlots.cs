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
            for (var group = 0; group < 2; group++)
            {
                var regularHair = group == 1;
                var hair = GetHairProfile(actor, regularHair);
                var count = (regularHair ? character.objHair : character.objAccessory)?.Length ?? 0;
                for (var slot = 0; slot < count; slot++)
                {
                    if (!TryGetHairSlot(character, slot, regularHair, out _, out var identity))
                        continue;
                    any = true;
                    if (hair == null || hair.Level == 0 ||
                        !hair.Slots.TryGetValue(slot, out var selected) || selected != identity)
                        complete = false;
                }
            }

            var clothes = GetClothes(actor);
            for (var slot = 0; slot < ClothesJuiceParts.SlotCount; slot++)
            {
                if (!ClothesJuiceParts.TryGet(character, slot, out _, out _))
                    continue;
                any = true;
                if (clothes == null || clothes.Level == 0 || !clothes.Slots.ContainsKey(slot))
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
                GetHairProfile(actor, true)?.Slots.Clear();
                GetClothes(actor)?.Slots.Clear();
                return;
            }

            for (var group = 0; group < 2; group++)
            {
                var regularHair = group == 1;
                var count = (regularHair ? character.objHair : character.objAccessory)?.Length ?? 0;
                for (var slot = 0; slot < count; slot++)
                {
                    if (!TryGetHairSlot(character, slot, regularHair, out _, out var identity))
                        continue;
                    var profile = EditHairProfile(actor, regularHair);
                    profile.Slots[slot] = identity;
                    if (profile.Level == 0)
                        profile.Level = 1;
                }
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
