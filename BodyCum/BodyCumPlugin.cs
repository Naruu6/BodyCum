using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using KKAPI;
using KKAPI.Studio;
using KKAPI.Studio.SaveLoad;
using Studio;

namespace BodyCum
{
    [BepInPlugin(GUID, "BodyCum", Version)]
    [BepInProcess("CharaStudio")]
    [BepInDependency(KoikatuAPI.GUID, "1.42.1")]
    [BepInDependency("com.bepis.bepinex.extendedsave")]
    internal sealed class BodyCumPlugin : BaseUnityPlugin
    {
        internal const string GUID = "koikatsu.bodycum";
        internal const string Version = "1.0.0";
        private const string SkinEffectsGuid = "Marco.SkinEffects";
        private const string LegacyConfigSection = "Custom Juice Color";
        private static readonly HashSet<OCIChar> DisabledCharacters = new HashSet<OCIChar>();
        private static ConfigEntry<bool> _enabled;
        private bool _effectsActive;

        internal static ManualLogSource Log { get; private set; }
        internal static CustomJuiceColor JuiceColors { get; private set; }
        internal static HairJuiceFeature HairJuices { get; private set; }
        internal static bool IsEnabled => _enabled != null && _enabled.Value;

        internal static bool IsCharacterEnabled(OCIChar character) => character != null && !DisabledCharacters.Contains(character);

        internal static void SetCharacterEnabled(OCIChar character, bool enabled)
        {
            if (character == null)
                return;
            if (enabled)
                DisabledCharacters.Remove(character);
            else
                DisabledCharacters.Add(character);
        }

        internal static void ClearCharacterPower() => DisabledCharacters.Clear();

        private void Start()
        {
            if (!StudioAPI.InsideStudio)
                return;

            Log = Logger;
            if (Chainloader.PluginInfos.TryGetValue(SkinEffectsGuid, out var skinEffects) && skinEffects.Instance &&
                skinEffects.Instance.GetType().Assembly.GetType("KK_SkinEffects.CustomJuiceColor", false) != null)
            {
                Log.LogWarning("BodyCum is inactive because this KK_SkinEffects build already includes the Juices features. Restore the original SkinEffects release or remove the modified copy before using BodyCum.");
                return;
            }

            var bodyCumKeys = ReadDefaults(Config.ConfigFilePath);
            JuiceColors = new CustomJuiceColor(Config, Log);
            ImportDefaults(bodyCumKeys);
            HairJuices = new HairJuiceFeature(JuiceColors, Log);
            _enabled = Config.Bind("General", "Enable BodyCum", true,
                "Allow BodyCum effects in Studio. Turning this off shows native Juices for all characters without changing their individual switches or settings.");
            StudioSaveLoadApi.RegisterExtraBehaviour<JuiceColorSceneController>(JuiceColorSceneController.DataId);
            JuiceColorStudioUi.Install();
            Log.LogInfo("BodyCum Studio Juices controls are active.");
        }

        private void LateUpdate()
        {
            // Apply configuration changes on Unity's thread before the camera renders.
            if (!IsEnabled)
            {
                StopEffects();
                return;
            }

            if (!_effectsActive)
            {
                HairJuices.Enable();
                _effectsActive = true;
            }
            JuiceColors.UpdateMaterials();
        }

        private void OnDisable() => StopEffects();

        private void OnDestroy()
        {
            StopEffects();
            HairJuices?.Dispose();
        }

        private void StopEffects()
        {
            if (!_effectsActive)
                return;

            _effectsActive = false;
            HairJuices.Disable();
            JuiceColors.RestoreMaterials();
        }

        private void ImportDefaults(Dictionary<string, string> bodyCumKeys)
        {
            if (bodyCumKeys == null)
                return;
            var legacyPath = Path.Combine(Paths.ConfigPath, SkinEffectsGuid + ".cfg");
            var legacyDefaults = ReadDefaults(legacyPath);
            if (legacyDefaults == null || legacyDefaults.Count == 0)
                return;

            var imported = 0;
            foreach (var definition in Config.Keys)
            {
                if (definition.Section != LegacyConfigSection || bodyCumKeys.ContainsKey(definition.Key) ||
                    !legacyDefaults.TryGetValue(definition.Key, out var value))
                    continue;
                Config[definition].SetSerializedValue(value);
                imported++;
            }
            if (imported > 0)
                Log.LogInfo("Imported " + imported + " Juices defaults from the previous SkinEffects configuration.");
        }

        private static Dictionary<string, string> ReadDefaults(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(path))
                return result;

            try
            {
                // BepInEx exposes bound entries. Read the old section before Bind creates missing keys.
                var inSection = false;
                foreach (var rawLine in File.ReadAllLines(path))
                {
                    var line = rawLine.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        inSection = line.Substring(1, line.Length - 2).Trim() == LegacyConfigSection;
                        continue;
                    }
                    if (!inSection)
                        continue;
                    var separator = line.IndexOf('=');
                    if (separator > 0)
                        result[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim();
                }
                return result;
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Log.LogWarning("Could not read Juices defaults from " + path + ": " + error.Message);
                return null;
            }
        }
    }
}
