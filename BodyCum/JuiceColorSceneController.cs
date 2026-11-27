using System.Globalization;
using ExtensibleSaveFormat;
using KKAPI.Studio.SaveLoad;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace BodyCum
{
    internal sealed class JuiceColorSceneController : SceneCustomFunctionController
    {
        // Keep the integrated release's scene key so existing scenes and rollback builds can read the same records.
        internal const string DataId = "Marco.SkinEffects.CustomJuiceColor";

        protected override void OnSceneSave()
        {
            var colors = BodyCumPlugin.JuiceColors;
            var hair = BodyCumPlugin.HairJuices;
            var data = new PluginData { version = 1 };
            foreach (var entry in GetStudio().dicObjectCtrl)
            {
                if (!(entry.Value is OCIChar character))
                    continue;
                if (!BodyCumPlugin.IsCharacterEnabled(character))
                    data.data[PowerKey(entry.Key)] = new byte[] { 0 };
                JuiceColorSettings settings;
                if (colors.TryGetOverride(character, out settings))
                {
                    data.data[Key(entry.Key)] = new byte[]
                    {
                        // Preserve the legacy 7-byte record: fixed Custom ON, fixed Gloss OFF.
                        1,
                        settings.Color.r, settings.Color.g, settings.Color.b,
                        (byte)settings.Opacity, (byte)settings.Brightness,
                        0
                    };
                    if (settings.Density != 0)
                        data.data[DensityKey(entry.Key)] = new[] { (byte)settings.Density };
                }
                var belly = colors.GetBellyLevel(character);
                if (belly != 0)
                    data.data[BellyKey(entry.Key)] = new[] { belly };
                var tongue = colors.GetTongueLevel(character);
                if (tongue != 0)
                    data.data[TongueKey(entry.Key)] = new[] { tongue };
                var profile = hair.Get(character);
                if (profile != null && profile.HasValues)
                    data.data[HairKey(entry.Key)] = profile.Serialize();
                var regularHair = hair.GetHairProfile(character, true);
                if (regularHair != null && regularHair.HasValues)
                    data.data[RegularHairKey(entry.Key)] = regularHair.Serialize();
                var clothes = hair.GetClothes(character);
                if (clothes != null && clothes.HasValues)
                {
                    data.data[ClothesKey(entry.Key)] = clothes.Serialize(character.charInfo);
                    if (clothes.Level == 2)
                        data.data[ClothesLevelKey(entry.Key)] = new byte[] { 2 };
                    if (clothes.HasTransform)
                        data.data[ClothesTransformKey(entry.Key)] = clothes.SerializeTransform();
                }
                var surface = hair.GetSurfaceStateForSave(character)?.Serialize();
                if (surface != null)
                    data.data[SurfaceKey(entry.Key)] = surface;
            }
            SetExtendedData(data.data.Count == 0 ? null : data);
        }

        protected override void OnSceneLoad(SceneOperationKind operation, ReadOnlyDictionary<int, ObjectCtrlInfo> loadedItems)
        {
            var colors = BodyCumPlugin.JuiceColors;
            var hair = BodyCumPlugin.HairJuices;
            if (operation != SceneOperationKind.Import)
            {
                BodyCumPlugin.ClearCharacterPower();
                colors.ClearOverrides();
                hair.Clear();
            }
            if (operation == SceneOperationKind.Clear)
                return;

            var data = GetExtendedData();
            if (data == null || data.version != 1 || data.data == null)
                return;
            foreach (var entry in loadedItems)
            {
                if (!(entry.Value is OCIChar character))
                    continue;
                object value;
                if (data.data.TryGetValue(PowerKey(entry.Key), out value) && value is byte[] power && power.Length == 1 && power[0] == 0)
                    BodyCumPlugin.SetCharacterEnabled(character, false);
                if (data.data.TryGetValue(Key(entry.Key), out value) && value is byte[] bytes && bytes.Length == 7 && bytes[4] <= 100 && bytes[5] <= 200)
                {
                    colors.SetOverride(character, new JuiceColorSettings
                    {
                        Color = new Color32(bytes[1], bytes[2], bytes[3], 255),
                        Opacity = bytes[4],
                        Density = ReadDensity(data, entry.Key),
                        Brightness = bytes[5]
                    });
                }
                if (data.data.TryGetValue(BellyKey(entry.Key), out value) && value is byte[] belly && belly.Length == 1 && belly[0] <= 2)
                    colors.SetBellyLevel(character, belly[0]);
                if (data.data.TryGetValue(TongueKey(entry.Key), out value) && value is byte[] tongue && tongue.Length == 1 && tongue[0] <= 2)
                    colors.SetTongueLevel(character, tongue[0]);
                if (data.data.TryGetValue(HairKey(entry.Key), out value) && value is byte[] hairData)
                {
                    var profile = HairJuiceProfile.Deserialize(hairData);
                    if (profile != null)
                        hair.Set(character, profile);
                }
                if (data.data.TryGetValue(RegularHairKey(entry.Key), out value) && value is byte[] regularHairData)
                {
                    var profile = HairJuiceProfile.Deserialize(regularHairData);
                    if (profile != null)
                        hair.SetRegularHair(character, profile);
                }
                ClothesJuiceProfile clothes = null;
                if (data.data.TryGetValue(ClothesKey(entry.Key), out value) && value is byte[] clothesData)
                    clothes = ClothesJuiceProfile.Deserialize(clothesData);
                if (data.data.TryGetValue(ClothesTransformKey(entry.Key), out value) && value is byte[] transformData)
                {
                    var target = clothes ?? new ClothesJuiceProfile();
                    if (target.TryReadTransform(transformData))
                        clothes = target;
                }
                if (clothes != null)
                {
                    if (clothes.Level == 1 && data.data.TryGetValue(ClothesLevelKey(entry.Key), out value) &&
                        value is byte[] clothesLevel && clothesLevel.Length == 1 && clothesLevel[0] == 2)
                        clothes.Level = 2;
                    hair.SetClothes(character, clothes);
                }
                if (data.data.TryGetValue(SurfaceKey(entry.Key), out value) && value is byte[] surfaceData)
                {
                    var surface = SurfaceProjectionState.Deserialize(surfaceData);
                    if (surface != null)
                        hair.SetSurfaceState(character, surface);
                }
            }
        }

        protected override void OnObjectsCopied(ReadOnlyDictionary<int, ObjectCtrlInfo> copiedItems)
        {
            var colors = BodyCumPlugin.JuiceColors;
            var hair = BodyCumPlugin.HairJuices;
            var objects = GetStudio().dicObjectCtrl;
            foreach (var entry in copiedItems)
            {
                ObjectCtrlInfo original;
                JuiceColorSettings settings;
                if (!(entry.Value is OCIChar copy) || !objects.TryGetValue(entry.Key, out original) || !(original is OCIChar character))
                    continue;
                BodyCumPlugin.SetCharacterEnabled(copy, BodyCumPlugin.IsCharacterEnabled(character));
                if (colors.TryGetOverride(character, out settings))
                    colors.SetOverride(copy, settings);
                colors.SetBellyLevel(copy, colors.GetBellyLevel(character));
                colors.SetTongueLevel(copy, colors.GetTongueLevel(character));
                var profile = hair.Get(character);
                if (profile != null)
                    hair.Set(copy, profile.Copy());
                var regularHair = hair.GetHairProfile(character, true);
                if (regularHair != null)
                    hair.SetRegularHair(copy, regularHair.Copy());
                var clothes = hair.GetClothes(character);
                if (clothes != null)
                    hair.SetClothes(copy, clothes.Copy());
                hair.CopySurfaceState(character, copy);
            }
        }

        protected override void OnObjectDeleted(ObjectCtrlInfo objectCtrlInfo)
        {
            if (objectCtrlInfo is OCIChar character)
            {
                BodyCumPlugin.SetCharacterEnabled(character, true);
                BodyCumPlugin.JuiceColors.ResetOverride(character);
                BodyCumPlugin.JuiceColors.SetBellyLevel(character, 0);
                BodyCumPlugin.JuiceColors.SetTongueLevel(character, 0);
                BodyCumPlugin.HairJuices.Remove(character);
            }
        }

        private static string Key(int id) => id.ToString(CultureInfo.InvariantCulture);

        private static string PowerKey(int id) => Key(id) + ".power";
        private static string DensityKey(int id) => Key(id) + ".density";

        private static string BellyKey(int id) => Key(id) + ".belly";
        private static string TongueKey(int id) => Key(id) + ".tongue";

        private static string HairKey(int id) => Key(id) + ".hair";
        private static string RegularHairKey(int id) => Key(id) + ".regularHair";

        private static string ClothesKey(int id) => Key(id) + ".clothes";
        private static string ClothesLevelKey(int id) => Key(id) + ".clothesLevel";
        private static string ClothesTransformKey(int id) => Key(id) + ".clothesTransform";

        private static string SurfaceKey(int id) => Key(id) + ".surfaceAttachment";

        private static int ReadDensity(PluginData data, int id)
        {
            object value;
            return data.data.TryGetValue(DensityKey(id), out value) && value is byte[] density &&
                density.Length == 1 && density[0] <= 100 ? density[0] : 0;
        }
    }
}
