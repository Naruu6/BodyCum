using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private const string SkinShaderResource = "BodyCum.Resources.CustomJuiceColor.Skin.bundle";
        private const string SkinShaderAsset = "assets/skineffects/bodycum_skin.shader";
        private const string SkinShaderName = "SkinEffects/BodyCum Skin Projection";
        private const string SkinMaskTexture = "_SkinMask";
        private static readonly int InnerBustGain = Shader.PropertyToID("_InnerBustGain");
        private readonly Dictionary<Renderer, HairMaterials> _skinLayers = new Dictionary<Renderer, HairMaterials>();
        private Shader _skinShader;
        private bool _attemptedSkinShader;

        private bool RecordSkin(Camera camera, ref CommandBuffer buffer, int frame)
        {
            if (_colors.SkinLayers.Count == 0 || !EnsureSkinShader())
                return false;

            var drawn = false;
            foreach (var layer in _colors.SkinLayers)
            {
                var renderer = layer.Renderer;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !BodyCumPlugin.IsCharacterEnabled(layer.Actor))
                    continue;

                var mesh = GetMesh(renderer);
                if (!mesh || layer.Index >= mesh.subMeshCount)
                    continue;
                var material = GetDrawMaterial(_skinLayers, _skinShader, renderer, layer.Index, frame);
                material.SetVector(ProjectionX, layer.BodyTiers);
                material.SetVector(ProjectionY, layer.FaceAndBelly);
                material.SetVector(InnerBustGain, new Vector4(layer.InnerBustGain.x, layer.InnerBustGain.y, 0f, 0f));
                SetSkinTexture(material, PatternTexture, layer.Pattern, layer.PatternTransform);
                SetSkinTexture(material, SkinMaskTexture, layer.Mask, layer.MaskTransform);
                SetSource(material, layer.Source, layer.Color, 0, layer.Density);
                // Opaque materials can store alpha data without using it for visibility.
                if (layer.Source.GetTag("RenderType", false, "") == "Opaque" && layer.Source.renderQueue < (int)RenderQueue.AlphaTest)
                    material.SetVector(JuiceParameters, new Vector4(0f, layer.Density, 0f, 0f));

                buffer = buffer ?? CreateDrawBuffer(camera);
                buffer.DrawRenderer(renderer, material, layer.Index, 0);
                drawn = true;
            }
            return drawn;
        }

        private static void SetSkinTexture(Material material, string name, Texture texture, Vector4 transform)
        {
            material.SetTexture(name, texture);
            material.SetTextureScale(name, new Vector2(transform.x, transform.y));
            material.SetTextureOffset(name, new Vector2(transform.z, transform.w));
        }

        private bool EnsureSkinShader()
        {
            if (_attemptedSkinShader)
                return _skinShader;
            _attemptedSkinShader = true;
            _skinShader = LoadProjectionShader(SkinShaderResource, SkinShaderAsset, SkinShaderName);
            return _skinShader;
        }
    }
}
