using System.Collections.Generic;
using Studio;
using UnityEngine;
using TongueReference = ChaReference.RefObjKey;

namespace BodyCum
{
    internal sealed partial class CustomJuiceColor
    {
        private static readonly Vector4 TongueTextureWindow = new Vector4(.22f, .22f, .33f, .38f);
        private readonly Dictionary<OCIChar, byte> _tongueLevels = new Dictionary<OCIChar, byte>();
        private readonly List<Renderer> _tongueRenderers = new List<Renderer>();
        private readonly HashSet<Renderer> _queuedTongueRenderers = new HashSet<Renderer>();

        internal byte GetTongueLevel(OCIChar character) =>
            character != null && _tongueLevels.TryGetValue(character, out var level) ? level : (byte)0;

        internal void SetTongueLevel(OCIChar character, byte level)
        {
            if (level == 0)
                _tongueLevels.Remove(character);
            else
                _tongueLevels[character] = level;
        }

        private void QueueTongueLayers(bool linear)
        {
            foreach (var entry in _tongueLevels)
            {
                var actor = entry.Key;
                var character = actor.charInfo;
                if (!BodyCumPlugin.IsCharacterEnabled(actor) || !character || !character.hiPoly || !character.isActiveAndEnabled)
                    continue;
                var settings = GetSettings(actor);
                if (settings.Opacity == 0)
                    continue;

                var pattern = ReadTexture(GetMaterial(character.rendBody, character.customMatBody), LiquidTexture, out _, out _) ??
                    ReadTexture(character.customMatBody, LiquidTexture, out _, out _) ??
                    ReadTexture(GetStockSkin(character, false), LiquidTexture, out _, out _);
                if (!pattern)
                    continue;

                var layer = new SkinJuiceLayer
                {
                    Actor = actor,
                    Color = settings.ToShaderColor(linear),
                    Density = settings.Density / 100f,
                    FaceAndBelly = new Vector4(entry.Value, 0f, 0f, 0f),
                    Pattern = pattern,
                    PatternTransform = TongueTextureWindow,
                    // The native renderer already isolates the tongue. An all-white mask supplies
                    // the full UV surface to the existing red / red+green shader tiers.
                    Mask = Texture2D.whiteTexture,
                    MaskTransform = new Vector4(1f, 1f, 0f, 0f)
                };
                _queuedTongueRenderers.Clear();
                QueueTongueRoot(character.GetReferenceInfo(TongueReference.S_TongueF), layer);
                QueueTongueRoot(character.GetReferenceInfo(TongueReference.S_TongueB), layer);
                QueueTongueRoot(character.objTongueEx, layer);
                _queuedTongueRenderers.Clear();
            }
        }

        private void QueueTongueRoot(GameObject root, SkinJuiceLayer layer)
        {
            if (!root || !root.activeInHierarchy)
                return;
            root.GetComponentsInChildren(false, _tongueRenderers);
            foreach (var renderer in _tongueRenderers)
                // The extended prefab also contains a separate silhouette renderer.
                if (renderer && (renderer.name == "o_tang" || renderer.name == "n_tang") &&
                    _queuedTongueRenderers.Add(renderer))
                    QueueTongueRenderer(renderer, layer);
            _tongueRenderers.Clear();
        }

        private void QueueTongueRenderer(Renderer renderer, SkinJuiceLayer layer)
        {
            if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                return;
            layer.Renderer = renderer;
            var materials = renderer.sharedMaterials;
            for (var index = 0; index < materials.Length; index++)
            {
                var material = materials[index];
                if (!material || !material.shader)
                    continue;
                layer.Index = index;
                layer.Source = material;
                SkinLayers.Add(layer);
            }
        }
    }
}
