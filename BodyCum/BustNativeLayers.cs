using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    // Overlay-based skins (including ordinary KKUTS) still draw the game's liquid relief
    // beneath our color. Give that pass the same pattern and normal as the overlaid pigment.
    internal sealed partial class HairJuiceFeature
    {
        private const string BakeResource = "BodyCum.Resources.CustomJuiceColor.BustBake.bundle";
        private const string NormalTexture = "_Texture3";
        private const string NativeTiling = "_LiquidTiling";
        private Material _bustBake;
        private bool _bustBakeTried;
        private Camera _materialOverrideCamera;
        private readonly Dictionary<BustTextureKey, BustAtlas> _bustAtlases = new Dictionary<BustTextureKey, BustAtlas>();
        private readonly List<BustTextureKey> _oldBustAtlases = new List<BustTextureKey>();
        private readonly Dictionary<Renderer, List<RenderMaterialSlot>> _renderMaterialCopies = new Dictionary<Renderer, List<RenderMaterialSlot>>();
        private readonly List<Renderer> _oldMaterialRenderers = new List<Renderer>();
        private readonly List<Renderer> _overriddenRenderers = new List<Renderer>();

        private struct BustTextureKey : IEquatable<BustTextureKey>
        {
            internal Texture Source;
            internal Vector4 Transform;
            internal Vector4 Extra;

            public bool Equals(BustTextureKey other) =>
                ReferenceEquals(Source, other.Source) && Transform.Equals(other.Transform) && Extra.Equals(other.Extra);
            public override bool Equals(object other) => other is BustTextureKey && Equals((BustTextureKey)other);
            public override int GetHashCode()
            {
                unchecked { return (Source.GetHashCode() * 397 ^ Transform.GetHashCode()) * 397 ^ Extra.GetHashCode(); }
            }
        }

        private sealed class BustAtlas
        {
            internal RenderTexture Texture;
            internal int LastFrame;
        }

        private sealed class RenderMaterialSlot
        {
            internal int Index, LastFrame;
            internal Material Original, Copy;
        }

        internal void PrepareNativeLayers()
        {
            RestoreRenderMaterials();
            var frame = Time.frameCount;
            SuppressNativeClothes(frame);
            foreach (var stage in _colors.NativeSkinStages)
                stage.Apply(PrepareRenderMaterial(stage.Renderer, stage.Index, stage.Source, frame));
            foreach (var layer in _colors.SkinLayers)
            {
                var p = layer.FaceAndBelly;
                var source = layer.Source;
                var extra = new Vector4(p.z, p.w, layer.InnerBustGain.x, layer.InnerBustGain.y);
                if (extra == Vector4.zero || layer.BodyTiers.x <= 0f ||
                    !source || !source.HasProperty("_liquidftop") || !source.HasProperty(BodyLiquidTexture) ||
                    !source.GetTexture(BodyLiquidTexture) || !source.HasProperty("_liquidmask")) continue;
                if (!_bustBakeTried)
                {
                    _bustBakeTried = true;
                    var shader = LoadProjectionShader(BakeResource, "assets/bodycum/bust_pattern_bake.shader", "Hidden/BodyCum/BustPatternBake");
                    if (shader) _bustBake = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                }
                if (!_bustBake) break;
                var pattern = GetBustAtlas(layer.Pattern, layer.PatternTransform, extra, frame);
                if (!pattern) continue;
                Texture normal = source.HasProperty(NormalTexture) ? source.GetTexture(NormalTexture) : null;
                RenderTexture warpedNormal = null;
                if (normal)
                {
                    var tiling = source.HasProperty(NativeTiling) ? source.GetVector(NativeTiling) : new Vector4(0, 0, 1, 1);
                    var st = source.GetTextureScale(NormalTexture);
                    var offset = source.GetTextureOffset(NormalTexture);
                    var transform = new Vector4(st.x * tiling.z, st.y * tiling.w, offset.x + st.x * tiling.x, offset.y + st.y * tiling.y);
                    warpedNormal = GetBustAtlas(normal, transform, extra, frame);
                    if (!warpedNormal) continue;
                }
                var copy = PrepareRenderMaterial(layer.Renderer, layer.Index, source, frame);
                if (source.HasProperty(NativeTiling)) copy.SetVector(NativeTiling, new Vector4(0, 0, 1, 1));
                SetRawAtlas(copy, BodyLiquidTexture, pattern);
                if (warpedNormal) SetRawAtlas(copy, NormalTexture, warpedNormal);
            }
            TrimNativeLayerResources(frame);
        }

        private Material PrepareRenderMaterial(Renderer renderer, int index, Material source, int frame)
        {
            if (!_renderMaterialCopies.TryGetValue(renderer, out var slots))
                _renderMaterialCopies.Add(renderer, slots = new List<RenderMaterialSlot>());
            var slot = slots.Find(existing => existing.Index == index);
            if (slot == null)
            {
                slot = new RenderMaterialSlot { Index = index };
                slots.Add(slot);
            }
            if (!slot.Copy || slot.Original != source || slot.Copy.shader != source.shader)
            {
                if (slot.Copy) UnityEngine.Object.Destroy(slot.Copy);
                slot.Copy = new Material(source) { name = source.name, hideFlags = HideFlags.HideAndDontSave };
                slot.Original = source;
            }
            else slot.Copy.CopyPropertiesFromMaterial(source);
            slot.LastFrame = frame;
            return slot.Copy;
        }

        private static void SetRawAtlas(Material material, string property, Texture texture)
        {
            material.SetTexture(property, texture);
            material.SetTextureScale(property, Vector2.one);
            material.SetTextureOffset(property, Vector2.zero);
        }

        private RenderTexture GetBustAtlas(Texture source, Vector4 transform, Vector4 extra, int frame)
        {
            if (!source || source.dimension != UnityEngine.Rendering.TextureDimension.Tex2D) return null;
            var key = new BustTextureKey { Source = source, Transform = transform, Extra = extra };
            if (_bustAtlases.TryGetValue(key, out var cached) && cached.Texture && cached.Texture.IsCreated())
            {
                cached.LastFrame = frame;
                return cached.Texture;
            }
            if (cached != null) ReleaseBustAtlas(cached.Texture);
            // Retain the native texel density when a source atlas uses tiling. Both native
            // inputs get their own ST. This avoids changing marks on a different body part
            // that originally happened to sample the same source texel.
            var cap = Mathf.Min(SystemInfo.maxTextureSize, 4096);
            int width = Mathf.Clamp(Mathf.CeilToInt(source.width * Mathf.Abs(transform.x)), 1, cap);
            int height = Mathf.Clamp(Mathf.CeilToInt(source.height * Mathf.Abs(transform.y)), 1, cap);
            var texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "BodyCum body-space liquid", hideFlags = HideFlags.HideAndDontSave,
                filterMode = source.filterMode, wrapMode = TextureWrapMode.Clamp, anisoLevel = source.anisoLevel,
                useMipMap = true, autoGenerateMips = true
            };
            var active = RenderTexture.active;
            var srgb = GL.sRGBWrite;
            try
            {
                if (!texture.Create()) { ReleaseBustAtlas(texture); return null; }
                GL.sRGBWrite = false;
                _bustBake.SetVector("_BustBakeScale", extra);
                _bustBake.SetVector("_BustBakeTransform", transform);
                _bustBake.SetTexture("_BustBakeSource", source);
                Graphics.Blit(null, texture, _bustBake, 0);
            }
            catch
            {
                ReleaseBustAtlas(texture);
                throw;
            }
            finally { RenderTexture.active = active; GL.sRGBWrite = srgb; }
            _bustAtlases[key] = new BustAtlas { Texture = texture, LastFrame = frame };
            return texture;
        }

        private void BeginMaterialOverrides(Camera camera)
        {
            if (_materialOverrideCamera || !camera || camera.cameraType != CameraType.Game || _renderMaterialCopies.Count == 0) return;
            _materialOverrideCamera = camera;
            foreach (var pair in _renderMaterialCopies)
            {
                var renderer = pair.Key;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                var materials = renderer.sharedMaterials;
                var changed = false;
                foreach (var slot in pair.Value)
                    if (slot.Copy && slot.Index < materials.Length && materials[slot.Index] == slot.Original)
                    { materials[slot.Index] = slot.Copy; changed = true; }
                if (!changed) continue;
                renderer.sharedMaterials = materials;
                _overriddenRenderers.Add(renderer);
            }
        }

        private void EndMaterialOverrides(Camera camera)
        {
            if (_materialOverrideCamera == camera) RestoreRenderMaterials();
        }

        private void RestoreRenderMaterials()
        {
            foreach (var renderer in _overriddenRenderers)
            {
                if (!renderer) continue;
                var materials = renderer.sharedMaterials;
                var changed = false;
                foreach (var slot in _renderMaterialCopies[renderer])
                    if (slot.Index < materials.Length && materials[slot.Index] == slot.Copy)
                    { materials[slot.Index] = slot.Original; changed = true; }
                if (changed) renderer.sharedMaterials = materials;
            }
            _overriddenRenderers.Clear();
            _materialOverrideCamera = null;
        }

        private static void ReleaseBustAtlas(RenderTexture texture)
        {
            if (!texture) return;
            texture.Release();
            UnityEngine.Object.Destroy(texture);
        }

        private void TrimNativeLayerResources(int frame)
        {
            foreach (var pair in _renderMaterialCopies)
            {
                var slots = pair.Value;
                for (int i = slots.Count - 1; i >= 0; i--)
                    if (!pair.Key || slots[i].LastFrame != frame)
                    {
                        if (slots[i].Copy) UnityEngine.Object.Destroy(slots[i].Copy);
                        slots.RemoveAt(i);
                    }
                if (slots.Count == 0) _oldMaterialRenderers.Add(pair.Key);
            }
            foreach (var renderer in _oldMaterialRenderers) _renderMaterialCopies.Remove(renderer);
            _oldMaterialRenderers.Clear();
            foreach (var pair in _bustAtlases)
                if (pair.Value.LastFrame != frame)
                {
                    ReleaseBustAtlas(pair.Value.Texture);
                    _oldBustAtlases.Add(pair.Key);
                }
            foreach (var key in _oldBustAtlases) _bustAtlases.Remove(key);
            _oldBustAtlases.Clear();
        }

        private void ReleaseNativeLayerResources()
        {
            RestoreRenderMaterials();
            TrimNativeLayerResources(-1);
            if (_bustBake) UnityEngine.Object.Destroy(_bustBake);
            _bustBake = null;
            _bustBakeTried = false;
        }
    }
}
