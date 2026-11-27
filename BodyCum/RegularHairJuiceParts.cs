using System.Globalization;
using UnityEngine;

namespace BodyCum
{
    internal static class RegularHairJuiceParts
    {
        private static readonly string[] Labels = { "Back", "Front", "Side", "Extra" };

        internal static bool TryGet(ChaControl character, int slot, out GameObject root, out string identity)
        {
            root = null;
            identity = null;
            var objects = character ? character.objHair : null;
            if (objects == null || slot < 0 || slot >= objects.Length || !objects[slot])
                return false;

            root = objects[slot];
            var info = GetInfo(character, slot, root);
            var bundle = info?.GetInfo(ChaListDefine.KeyType.MainAB);
            var asset = info?.GetInfo(ChaListDefine.KeyType.MainData);
            if (!string.IsNullOrEmpty(bundle) && !string.IsNullOrEmpty(asset))
            {
                identity = bundle + "\n" + asset;
                return true;
            }

            var parts = character.fileHair?.parts;
            var id = info?.Id ?? (parts != null && slot < parts.Length ? parts[slot]?.id : null);
            if (!id.HasValue) return false;
            identity = slot.ToString(CultureInfo.InvariantCulture) + "/" + id.Value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal static string GetLabel(ChaControl character, int slot, GameObject root)
        {
            var group = slot < Labels.Length ? Labels[slot] : (slot + 1).ToString(CultureInfo.InvariantCulture);
            var name = GetInfo(character, slot, root)?.GetInfo(ChaListDefine.KeyType.Name);
            return string.IsNullOrEmpty(name) ? group : group + "  " + name;
        }

        private static ListInfoBase GetInfo(ChaControl character, int slot, GameObject root)
        {
            var infos = character.infoHair;
            return infos != null && slot < infos.Length && infos[slot] != null
                ? infos[slot] : root.GetComponent<ListInfoComponent>()?.data;
        }
    }
}
