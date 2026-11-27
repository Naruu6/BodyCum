using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.Rendering;
using KoikatsuStudio = Studio.Studio;
using SiruPart = ChaFileDefine.SiruParts;

namespace BodyCum
{
    internal sealed partial class CustomJuiceColor
    {
        private const string LiquidTexture = "_Texture2";
        private const string LiquidMask = "_liquidmask";
        private const string LiquidTiling = "_LiquidTiling";
        private readonly List<SkinSlot> _skinSlots = new List<SkinSlot>();
        private readonly Dictionary<Material, OCIChar> _materialOwners = new Dictionary<Material, OCIChar>();
        private readonly HashSet<Material> _sharedMaterials = new HashSet<Material>();
        private readonly List<Renderer> _ownershipRenderers = new List<Renderer>();
        private readonly Dictionary<int, Material> _nativeMaterials = new Dictionary<int, Material>();
        internal readonly List<SkinJuiceLayer> SkinLayers = new List<SkinJuiceLayer>();
        internal readonly List<NativeSkinStage> NativeSkinStages = new List<NativeSkinStage>();

        private struct SkinSlot
        {
            internal OCIChar Actor;
            internal Renderer Renderer;
            internal Material Material;
            internal int Index;
            internal bool Face;
            internal bool MainBody;
        }

        private void UpdateSkinLayers(int frame, bool linear, bool includeClothes)
        {
            CollectSkinSlots(includeClothes);
            NativeSkinStages.Clear();
            OCIChar measured = null;
            var bustExtra = Vector4.zero;
            foreach (var slot in _skinSlots)
            {
                if (!BodyCumPlugin.IsCharacterEnabled(slot.Actor))
                    continue;
                var settings = GetSettings(slot.Actor);
                var color = settings.ToShaderColor(linear);
                var density = settings.Density / 100f;
                var belly = slot.MainBody && slot.Actor.charInfo.sex == 1 ? GetBellyLevel(slot.Actor) : (byte)0;
                var extra = Vector4.zero;
                if (!slot.Face && slot.Actor.charInfo.sex == 1 && settings.Opacity != 0 &&
                    slot.Actor.GetSiruFlags(SiruPart.SiruFrontUp) > 0)
                {
                    if (measured != slot.Actor)
                    {
                        measured = slot.Actor;
                        bustExtra = _bustGeometry.GetGain(measured.charInfo, frame);
                    }
                    extra = bustExtra;
                }
                // A shared Material can't carry different overrides for different actors.
                var stage = new NativeSkinStage(slot.Renderer, slot.Material, slot.Index,
                    slot.Face ? (byte)0 : slot.Actor.GetSiruFlags(SiruPart.SiruFrontUp));
                if (!_sharedMaterials.Contains(slot.Material) &&
                    ApplyIfActive(slot.Material, slot.Face, frame, color, density, belly, extra, stage.HasActive))
                {
                    if (stage.NeedsOverride) NativeSkinStages.Add(stage);
                    continue;
                }
                if (settings.Opacity != 0)
                    QueueSkinLayer(slot, belly, color, density, extra);
            }
        }

        private void CollectSkinSlots(bool includeClothes)
        {
            _skinSlots.Clear();
            _materialOwners.Clear();
            _sharedMaterials.Clear();
            foreach (var item in KoikatsuStudio.Instance.dicObjectCtrl.Values)
            {
                if (!(item is OCIChar actor) || !actor.charInfo || !actor.charInfo.hiPoly)
                    continue;
                var character = actor.charInfo;
                AddSkinRenderer(actor, character.rendFace, true);
                AddSkinRenderer(actor, character.rendBody, false);
                var body = GetMaterial(character.rendBody, character.customMatBody);
                foreach (var copy in _bodyClones.GetCopies(character, body))
                    AddSkinSlot(actor, copy.Renderer, copy.Material, copy.Slot, false, false);
                if (includeClothes)
                    IndexClothesOwners(actor);
            }
        }

        private void AddSkinRenderer(OCIChar actor, Renderer renderer, bool face)
        {
            if (!renderer)
                return;
            var materials = renderer.sharedMaterials;
            for (var i = 0; i < materials.Length; i++)
                AddSkinSlot(actor, renderer, materials[i], i, face, !face);
        }

        private void AddSkinSlot(OCIChar actor, Renderer renderer, Material material, int index, bool face, bool mainBody)
        {
            if (!material || !material.shader)
                return;
            IndexOwner(actor, material);
            _skinSlots.Add(new SkinSlot { Actor = actor, Renderer = renderer, Material = material, Index = index, Face = face, MainBody = mainBody });
        }

        private void IndexClothesOwners(OCIChar actor)
        {
            var clothes = actor.charInfo.objClothes;
            if (clothes == null)
                return;
            foreach (var root in clothes)
            {
                if (!root)
                    continue;
                root.GetComponentsInChildren(false, _ownershipRenderers);
                foreach (var renderer in _ownershipRenderers)
                    foreach (var material in renderer.sharedMaterials)
                        if (material && material.shader && (_variants.ContainsKey(material.shader.name) || IsCustomMaterial(material)))
                            IndexOwner(actor, material);
                _ownershipRenderers.Clear();
            }
        }

        private void IndexOwner(OCIChar actor, Material material)
        {
            if (_materialOwners.TryGetValue(material, out var owner))
            {
                if (!ReferenceEquals(owner, actor))
                    _sharedMaterials.Add(material);
            }
            else
                _materialOwners.Add(material, actor);
        }

        private void QueueSkinLayer(SkinSlot slot, byte belly, Vector4 color, float density, Vector4 bustExtra)
        {
            var actor = slot.Actor;
            var face = slot.Face ? actor.GetSiruFlags(SiruPart.SiruKao) : 0;
            var regions = slot.Face ? Vector4.zero : new Vector4(actor.GetSiruFlags(SiruPart.SiruFrontUp),
                actor.GetSiruFlags(SiruPart.SiruFrontDown), actor.GetSiruFlags(SiruPart.SiruBackUp), actor.GetSiruFlags(SiruPart.SiruBackDown));
            if (face == 0 && belly == 0 && regions == Vector4.zero)
                return;

            var character = actor.charInfo;
            var original = slot.Face ? character.customMatFace : character.customMatBody;
            var primary = GetMaterial(slot.Face ? character.rendFace : character.rendBody, original);
            var pattern = ReadTexture(slot.Material, LiquidTexture, out var patternTransform, out var tiling) ??
                          ReadTexture(primary, LiquidTexture, out patternTransform, out tiling) ??
                          ReadTexture(original, LiquidTexture, out patternTransform, out tiling);
            var mask = ReadTexture(slot.Material, LiquidMask, out var maskTransform, out _) ??
                       ReadTexture(primary, LiquidMask, out maskTransform, out _) ??
                       ReadTexture(original, LiquidMask, out maskTransform, out _);
            if (!pattern || !mask)
            {
                var native = GetStockSkin(character, slot.Face);
                if (!pattern)
                    pattern = ReadTexture(native, LiquidTexture, out patternTransform, out tiling);
                if (!mask)
                    mask = ReadTexture(native, LiquidMask, out maskTransform, out _);
            }
            if (!pattern || !mask)
                return;

            var textureSpace = new Vector4(patternTransform.x * tiling.z, patternTransform.y * tiling.w,
                patternTransform.z + patternTransform.x * tiling.x, patternTransform.w + patternTransform.y * tiling.y);
            SkinLayers.Add(new SkinJuiceLayer
            {
                Actor = actor, Renderer = slot.Renderer, Index = slot.Index, Source = slot.Material, Color = color,
                Density = density, BodyTiers = regions, FaceAndBelly = new Vector4(face, belly, bustExtra.x, bustExtra.y),
                InnerBustGain = new Vector2(bustExtra.z, bustExtra.w),
                Pattern = pattern, PatternTransform = textureSpace, Mask = mask, MaskTransform = maskTransform
            });
        }

        internal Material GetStockSkin(ChaControl character, bool face)
        {
            var female = character.sex == 1;
            // Stock male faces have no verified native Juices mask. Use live inputs if present.
            if (face && !female)
                return null;
            var key = face ? 2 : character.sex;
            if (_nativeMaterials.TryGetValue(key, out var material) && (material || ReferenceEquals(material, null)))
                return material;

            var bundle = face ? "chara/bo_head_00.unity3d" : "chara/mm_base.unity3d";
            var asset = face ? "cf_m_face_00" : female ? "cf_m_body" : "cm_m_body";
            try
            {
                material = CommonLib.LoadAsset<Material>(bundle, asset, false, string.Empty);
            }
            catch (Exception error)
            {
                _log.LogWarning("BodyCum could not read the default liquid textures from " + asset + ": " + error.Message);
            }
            _nativeMaterials[key] = material;
            return material;
        }

        private static Texture ReadTexture(Material material, string property, out Vector4 transform, out Vector4 tiling)
        {
            transform = Vector4.zero;
            tiling = Vector4.zero;
            if (!material || !material.HasProperty(property))
                return null;
            var texture = material.GetTexture(property);
            if (!texture || texture.dimension != TextureDimension.Tex2D)
                return null;

            var scale = material.GetTextureScale(property);
            var offset = material.GetTextureOffset(property);
            transform = new Vector4(scale.x, scale.y, offset.x, offset.y);
            tiling = property == LiquidTexture && material.HasProperty(LiquidTiling)
                ? material.GetVector(LiquidTiling) : new Vector4(0f, 0f, 1f, 1f);
            return texture;
        }
    }

    internal struct SkinJuiceLayer
    {
        internal OCIChar Actor;
        internal Renderer Renderer;
        internal int Index;
        internal Material Source;
        internal Vector4 Color;
        internal float Density;
        internal Vector4 BodyTiers;
        internal Vector4 FaceAndBelly;
        internal Vector2 InnerBustGain;
        internal Texture Pattern;
        internal Vector4 PatternTransform;
        internal Texture Mask;
        internal Vector4 MaskTransform;
    }

    internal struct NativeSkinStage
    {
        private const string Breast = "_liquidftop";
        internal readonly Renderer Renderer;
        internal readonly Material Source;
        internal readonly int Index;
        private readonly byte _tier;

        internal NativeSkinStage(Renderer renderer, Material source, int index, byte tier)
        {
            Renderer = renderer;
            Source = source;
            Index = index;
            _tier = tier;
        }

        internal bool HasActive => _tier > 0;
        internal bool NeedsOverride => HasActive && Source.HasProperty(Breast) && Source.GetFloat(Breast) < _tier;
        internal void Apply(Material target) => target.SetFloat(Breast, _tier);
    }
}
