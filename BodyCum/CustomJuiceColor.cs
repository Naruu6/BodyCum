using System;
using System.Collections.Generic;
using System.IO;
using BepInEx.Configuration;
using BepInEx.Logging;
using Studio;
using UnityEngine;
using UnityEngine.Rendering;
using KoikatsuStudio = Studio.Studio;

namespace BodyCum
{
    internal sealed partial class CustomJuiceColor
    {
        private const string Section = "Custom Juice Color";
        private const string DefaultHex = "#F5F3EB";
        private const string ResourcePrefix = "BodyCum.Resources.CustomJuiceColor";
        private const string ShaderNamePrefix = "SkinEffects/Custom Juice ";

        private readonly ConfigEntry<string> _color;
        private readonly ConfigEntry<int> _opacity;
        private readonly ConfigEntry<int> _density;
        private readonly ConfigEntry<int> _brightness;
        private readonly ManualLogSource _log;
        private readonly BustPatternGeometry _bustGeometry = new BustPatternGeometry();
        private readonly int _colorId = Shader.PropertyToID("_SkinEffectsJuiceColor");
        private readonly int _densityId = Shader.PropertyToID("_SkinEffectsJuiceDensity");
        private readonly int _bustWarpId = Shader.PropertyToID("_BodyCumBustWarp");
        private readonly int _maskId = Shader.PropertyToID("_liquidmask");
        private readonly int _faceId = Shader.PropertyToID("_liquidface");
        private readonly int _frontTopId = Shader.PropertyToID("_liquidftop");
        private readonly int _frontBottomId = Shader.PropertyToID("_liquidfbot");
        private readonly int _backTopId = Shader.PropertyToID("_liquidbtop");
        private readonly int _backBottomId = Shader.PropertyToID("_liquidbbot");

        private readonly Dictionary<Material, AppliedShader> _materials = new Dictionary<Material, AppliedShader>();
        private readonly List<Material> _expired = new List<Material>();
        private readonly Dictionary<OCIChar, JuiceColorSettings> _characterSettings = new Dictionary<OCIChar, JuiceColorSettings>();
        private readonly Dictionary<OCIChar, byte> _bellyLevels = new Dictionary<OCIChar, byte>();
        private readonly JuiceBodyClones _bodyClones = new JuiceBodyClones();
        private readonly Dictionary<string, ShaderVariant> _variants = new Dictionary<string, ShaderVariant>(StringComparer.Ordinal)
        {
            { "Shader Forge/main_skin", new ShaderVariant("main_skin", "", "main_skin") },
            { "KKUTS_Tess", new ShaderVariant("KKUTS_Tess", ".KKUTS_Tess", "kkuts_tess") }
        };
        private readonly HashSet<string> _reportedShaders = new HashSet<string>(StringComparer.Ordinal);
        private bool _reportedGraphicsDevice;
        private bool _settingsDirty = true;
        private JuiceColorSettings _defaults;

        private sealed class AppliedShader
        {
            internal Shader Original;
            internal Shader Replacement;
            internal int LastFrame;
            internal Vector4 Color;
            internal float Density;
            internal byte BellyLevel;
            internal Vector4 BustExtra;
        }

        private sealed class ShaderVariant
        {
            internal readonly string Name;
            internal readonly string Resource;
            internal readonly string Address;
            internal Shader Shader;
            internal bool LoadAttempted;

            internal ShaderVariant(string name, string resourceSuffix, string address)
            {
                Name = ShaderNamePrefix + name;
                Resource = ResourcePrefix + resourceSuffix + ".bundle";
                Address = "assets/skineffects/" + address + ".shader";
            }
        }

        internal CustomJuiceColor(ConfigFile config, ManualLogSource log)
        {
            _log = log;
            _color = config.Bind(Section, "Juice Color", DefaultHex,
                "RGB color in #RRGGBB format. Invalid values use " + DefaultHex + ".");
            _opacity = config.Bind(Section, "Juice Opacity", 90,
                new ConfigDescription("Opacity as a percentage.", new AcceptableValueRange<int>(0, 100)));
            _density = config.Bind(Section, "Juice Density", 0,
                new ConfigDescription("Strengthens the existing liquid marks while preserving their pattern and feathered edges.",
                    new AcceptableValueRange<int>(0, 100)));
            _brightness = config.Bind(Section, "Juice Brightness", 100,
                new ConfigDescription("Brightness as a percentage.", new AcceptableValueRange<int>(0, 200)));
            _color.SettingChanged += SettingsChanged;
            _opacity.SettingChanged += SettingsChanged;
            _density.SettingChanged += SettingsChanged;
            _brightness.SettingChanged += SettingsChanged;
        }

        private void SettingsChanged(object sender, EventArgs args) => _settingsDirty = true;

        internal JuiceColorSettings GetSettings(OCIChar character)
        {
            JuiceColorSettings settings;
            return character != null && _characterSettings.TryGetValue(character, out settings) ? settings : GetDefaults();
        }

        internal bool TryGetOverride(OCIChar character, out JuiceColorSettings settings) =>
            _characterSettings.TryGetValue(character, out settings);

        internal void SetOverride(OCIChar character, JuiceColorSettings settings) => _characterSettings[character] = settings;

        internal void ResetOverride(OCIChar character) => _characterSettings.Remove(character);

        internal byte GetBellyLevel(OCIChar character) =>
            character != null && _bellyLevels.TryGetValue(character, out var level) ? level : (byte)0;

        internal void SetBellyLevel(OCIChar character, byte level)
        {
            if (level == 0)
                _bellyLevels.Remove(character);
            else
                _bellyLevels[character] = level;
        }

        internal void ClearOverrides()
        {
            _characterSettings.Clear();
            _bellyLevels.Clear();
            _tongueLevels.Clear();
        }

        internal void UpdateMaterials()
        {
            SkinLayers.Clear();
            var clothes = BodyCumPlugin.HairJuices;
            var hasClothes = clothes != null && clothes.HasActiveClothes;
            if (!Singleton<KoikatsuStudio>.IsInstance())
            {
                RestoreMaterials();
                return;
            }

            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                if (!_reportedGraphicsDevice)
                {
                    _reportedGraphicsDevice = true;
                    _log.LogWarning("Custom Juice Color requires Direct3D 11.");
                }
                return;
            }

            var frame = Time.frameCount;
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            UpdateSkinLayers(frame, linear, hasClothes);
            QueueTongueLayers(linear);

            foreach (var entry in _materials)
                if (!entry.Key || entry.Value.LastFrame != frame)
                    _expired.Add(entry.Key);
            foreach (var material in _expired)
            {
                Restore(material, _materials[material]);
                _materials.Remove(material);
            }
            _expired.Clear();
        }

        private JuiceColorSettings GetDefaults()
        {
            if (!_settingsDirty)
                return _defaults;
            _settingsDirty = false;

            var value = _color.Value?.Trim();
            Color color;
            if (value == null || value.Length != 7 || value[0] != '#' || !ColorUtility.TryParseHtmlString(value, out color))
                ColorUtility.TryParseHtmlString(DefaultHex, out color);

            _defaults = new JuiceColorSettings
            {
                Color = color,
                Opacity = _opacity.Value,
                Density = _density.Value,
                Brightness = _brightness.Value
            };
            return _defaults;
        }

        private static Material GetMaterial(Renderer renderer, Material fallback) => renderer ? renderer.sharedMaterial : fallback;

        internal bool IsCustomMaterial(Material material) =>
            _materials.TryGetValue(material, out var applied) && material.shader == applied.Replacement;

        private bool ApplyIfActive(Material material, bool face, int frame, Vector4 color, float density,
            byte bellyLevel = 0, Vector4 bustExtra = default(Vector4), bool forceActive = false)
        {
            if (!material || !material.HasProperty(_maskId) || !material.GetTexture(_maskId))
                return false;

            var current = material.shader;
            if (!current)
                return false;

            var active = forceActive || bellyLevel != 0 || (face ? IsActive(material, _faceId) :
                IsActive(material, _frontTopId) || IsActive(material, _frontBottomId) ||
                IsActive(material, _backTopId) || IsActive(material, _backBottomId));
            if (!active)
                return false;

            AppliedShader applied;
            if (!_materials.TryGetValue(material, out applied) || current != applied.Replacement)
            {
                var name = current.name;
                ShaderVariant variant;
                if (!_variants.TryGetValue(name, out variant))
                    return false;

                if (!variant.LoadAttempted)
                    variant.Shader = LoadShader(variant);
                if (!variant.Shader)
                    return false;

                SetShader(material, variant.Shader);
                applied = new AppliedShader { Original = current, Replacement = variant.Shader, Density = -1f };
                _materials[material] = applied;
                if (_reportedShaders.Add(name))
                    _log.LogInfo("Custom Juice Color applied to skin using " + name + ".");
            }

            applied.LastFrame = frame;
            if (applied.Density < 0f || applied.BustExtra != bustExtra)
            {
                material.SetVector(_bustWarpId, bustExtra);
                applied.BustExtra = bustExtra;
            }
            if (applied.Density < 0f || applied.Color != color)
            {
                material.SetVector(_colorId, color);
                applied.Color = color;
            }
            if (applied.Density != density || applied.BellyLevel != bellyLevel)
            {
                material.SetVector(_densityId, new Vector4(density, bellyLevel, 0f, 0f));
                applied.Density = density;
                applied.BellyLevel = bellyLevel;
            }
            return true;
        }

        private static bool IsActive(Material material, int property) =>
            material.HasProperty(property) && material.GetFloat(property) > 0f;

        internal void RestoreMaterials()
        {
            NativeSkinStages.Clear();
            _bustGeometry.Clear();
            foreach (var entry in _materials)
                Restore(entry.Key, entry.Value);
            _materials.Clear();
            _bodyClones.Clear();
            _skinSlots.Clear();
            _materialOwners.Clear();
            _sharedMaterials.Clear();
            SkinLayers.Clear();
        }

        private void Restore(Material material, AppliedShader applied)
        {
            if (material && applied.Original && material.shader == applied.Replacement)
                SetShader(material, applied.Original);
        }

        private static void SetShader(Material material, Shader shader)
        {
            var renderQueue = material.renderQueue;
            var keywords = material.shaderKeywords;
            material.shader = shader;
            material.shaderKeywords = keywords;
            if (material.renderQueue != renderQueue)
                material.renderQueue = renderQueue;
        }

        private Shader LoadShader(ShaderVariant variant)
        {
            variant.LoadAttempted = true;
            AssetBundle bundle = null;
            try
            {
                using (var stream = typeof(CustomJuiceColor).Assembly.GetManifestResourceStream(variant.Resource))
                {
                    if (stream == null)
                        throw new IOException("The compiled juice shader resource is missing.");
                    using (var reader = new BinaryReader(stream))
                        bundle = AssetBundle.LoadFromMemory(reader.ReadBytes(checked((int)stream.Length)));
                }

                var shader = bundle ? bundle.LoadAsset<Shader>(variant.Address) : null;
                if (!shader || !shader.isSupported || shader.name != variant.Name)
                    throw new IOException("Unity could not load the compatible juice shader.");
                _log.LogDebug("Custom Juice Color loaded shader " + variant.Name + ".");
                return shader;
            }
            catch (Exception exception)
            {
                _log.LogWarning("Custom Juice Color could not load " + variant.Name + ": " + exception.Message);
                return null;
            }
            finally
            {
                if (bundle)
                    bundle.Unload(false);
            }
        }
    }
}
