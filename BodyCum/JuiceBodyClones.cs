using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    // Some clothing renders a copy of the character's actual skin material over the body.
    internal sealed class JuiceBodyClones
    {
        private const float ScanInterval = 1f;
        private static readonly int MainTex = Shader.PropertyToID("_MainTex");
        private static readonly int LiquidTex = Shader.PropertyToID("_Texture2");
        private static readonly int LiquidNormal = Shader.PropertyToID("_Texture3");
        private static readonly int LiquidMask = Shader.PropertyToID("_liquidmask");

        private readonly Dictionary<ChaControl, CacheEntry> _cache = new Dictionary<ChaControl, CacheEntry>();
        private readonly List<ChaControl> _expired = new List<ChaControl>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<RendererMaterial> _matches = new List<RendererMaterial>();
        private float _nextTrim;

        // Consume the returned list before calling GetCopies again.
        internal List<RendererMaterial> GetCopies(ChaControl character, Material body)
        {
            _matches.Clear();
            if (!character || !body)
                return _matches;

            var textures = new BodyTextures(body);
            if (!textures.HasMain)
                return _matches;

            var now = Time.unscaledTime;
            if (now >= _nextTrim)
                Trim(now);

            CacheEntry entry;
            if (!_cache.TryGetValue(character, out entry))
            {
                entry = new CacheEntry();
                _cache.Add(character, entry);
            }

            entry.LastSeen = now;
            if (now >= entry.NextScan || entry.Body != body || !entry.Textures.SameAs(textures))
            {
                entry.Body = body;
                entry.Textures = textures;
                entry.NextScan = now + ScanInterval;
                Scan(character, entry);
            }

            foreach (var entryMaterial in entry.Matches)
            {
                var material = entryMaterial.Material;
                if (material && IsVisible(entryMaterial.Renderer) && entryMaterial.IsAttached() && textures.Matches(material))
                    _matches.Add(entryMaterial);
            }

            return _matches;
        }

        internal void Clear()
        {
            _cache.Clear();
            _matches.Clear();
        }

        internal static bool IsBodyCopy(Material material, Material body)
        {
            if (!material || !body || !IsBodyMaterial(material.name))
                return false;
            var textures = new BodyTextures(body);
            return textures.HasMain && textures.Matches(material);
        }

        private void Scan(ChaControl character, CacheEntry entry)
        {
            entry.Matches.Clear();
            character.GetComponentsInChildren(false, _renderers);
            foreach (var renderer in _renderers)
            {
                if (!IsVisible(renderer) || renderer == character.rendFace || renderer == character.rendBody)
                    continue;

                var materials = renderer.sharedMaterials;
                for (var index = 0; index < materials.Length; ++index)
                {
                    var material = materials[index];
                    if (material && IsBodyMaterial(material.name) && entry.Textures.Matches(material))
                        entry.Matches.Add(new RendererMaterial { Renderer = renderer, Material = material, Slot = index });
                }
            }
            _renderers.Clear();
        }

        private void Trim(float now)
        {
            _nextTrim = now + ScanInterval;
            foreach (var entry in _cache)
                if (!entry.Key || now - entry.Value.LastSeen > 5f * ScanInterval)
                    _expired.Add(entry.Key);
            foreach (var character in _expired)
                _cache.Remove(character);
            _expired.Clear();
        }

        private static bool IsVisible(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;

        private static bool IsBodyMaterial(string name)
        {
            const string prefix = "DoNotEdit_";
            int start = name.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 0;
            return IsMaterialName(name, start, "cf_m_body") || IsMaterialName(name, start, "cm_m_body");
        }

        private static bool IsMaterialName(string name, int start, string expected)
        {
            int end = start + expected.Length;
            return name.Length >= end && string.CompareOrdinal(name, start, expected, 0, expected.Length) == 0 &&
                (name.Length == end || name[end] == ' ');
        }

        private sealed class CacheEntry
        {
            internal Material Body;
            internal BodyTextures Textures;
            internal float NextScan;
            internal float LastSeen;
            internal readonly List<RendererMaterial> Matches = new List<RendererMaterial>();
        }

        internal struct RendererMaterial
        {
            internal Renderer Renderer;
            internal Material Material;
            internal int Slot;

            internal bool IsAttached()
            {
                if (Slot == 0)
                    return Renderer.sharedMaterial == Material;

                var materials = Renderer.sharedMaterials;
                return Slot < materials.Length && materials[Slot] == Material;
            }
        }

        private struct BodyTextures
        {
            private readonly Texture _main;
            private readonly Texture _liquid;
            private readonly Texture _normal;
            private readonly Texture _mask;

            internal BodyTextures(Material material)
            {
                _main = GetTexture(material, MainTex);
                _liquid = GetTexture(material, LiquidTex);
                _normal = GetTexture(material, LiquidNormal);
                _mask = GetTexture(material, LiquidMask);
            }

            internal bool HasMain => _main;

            internal bool SameAs(BodyTextures other) =>
                _main == other._main && _liquid == other._liquid && _normal == other._normal && _mask == other._mask;

            internal bool Matches(Material material)
            {
                var other = new BodyTextures(material);
                // A custom shader may hide the liquid uniforms. A copy still needs the
                // same skin texture, and any liquid texture available on both must agree.
                return _main == other._main && SameWhenPresent(_liquid, other._liquid) &&
                    SameWhenPresent(_normal, other._normal) && SameWhenPresent(_mask, other._mask);
            }

            private static bool SameWhenPresent(Texture left, Texture right) => !left || !right || left == right;

            private static Texture GetTexture(Material material, int property) =>
                material.HasProperty(property) ? material.GetTexture(property) : null;
        }
    }
}
