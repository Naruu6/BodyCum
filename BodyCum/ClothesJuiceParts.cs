using System.Globalization;
using UnityEngine;

namespace BodyCum
{
    internal static class ClothesJuiceParts
    {
        internal const int SlotCount = 9;

        private static readonly string[] Labels =
        {
            "Top", "Bottom", "Bra", "Panties", "Gloves", "Pantyhose", "Socks", "Indoor shoes", "Outdoor shoes"
        };

        internal static bool TryGet(ChaControl character, int slot, out GameObject root, out string identity)
        {
            root = null;
            identity = null;
            var objects = character ? character.objClothes : null;
            if (objects == null || slot < 0 || slot >= SlotCount || slot >= objects.Length || !objects[slot])
                return false;

            root = objects[slot];
            var clothes = character.nowCoordinate?.clothes;
            var parts = clothes?.parts;
            var id = parts != null && slot < parts.Length ? parts[slot]?.id : null;
            identity = GetIdentity(GetInfo(character.infoClothes, slot, root), slot, id);
            if (identity == null)
                return false;

            if (slot == (int)ChaFileDefine.ClothesKind.top)
            {
                var subParts = character.objParts;
                var subIds = clothes?.subPartsId;
                if (subParts != null)
                    for (var index = 0; index < subParts.Length; index++)
                    {
                        var subPart = subParts[index];
                        if (!subPart || !subPart.transform.IsChildOf(root.transform))
                            continue;
                        var subId = subIds != null && index < subIds.Length ? (int?)subIds[index] : null;
                        var subIdentity = GetIdentity(GetInfo(character.infoParts, index, subPart), index, subId);
                        if (subIdentity != null)
                            identity += "\npart " + index.ToString(CultureInfo.InvariantCulture) + "\n" + subIdentity;
                    }
            }

            return true;
        }

        internal static string GetLabel(ChaControl character, int slot, GameObject root)
        {
            var slotLabel = Labels[slot];
            var name = GetInfo(character.infoClothes, slot, root)?.GetInfo(ChaListDefine.KeyType.Name);
            return string.IsNullOrEmpty(name) ? slotLabel : slotLabel + "  " + name;
        }

        private static ListInfoBase GetInfo(ListInfoBase[] infos, int index, GameObject root)
        {
            var attached = root.GetComponent<ListInfoComponent>();
            if (attached && attached.data != null)
                return attached.data;
            return infos != null && index < infos.Length ? infos[index] : null;
        }

        private static string GetIdentity(ListInfoBase info, int index, int? fallbackId)
        {
            var bundle = info?.GetInfo(ChaListDefine.KeyType.MainAB);
            var asset = info?.GetInfo(ChaListDefine.KeyType.MainData);
            if (!string.IsNullOrEmpty(bundle) && !string.IsNullOrEmpty(asset))
                return bundle + "\n" + asset;

            var id = info?.Id ?? fallbackId;
            return id.HasValue ? (info?.Category ?? index).ToString(CultureInfo.InvariantCulture) + "/" +
                id.Value.ToString(CultureInfo.InvariantCulture) : null;
        }
    }
}
