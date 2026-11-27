using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.Rendering;
using KoikatsuStudio = Studio.Studio;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        // ChaControl.UpdateClothesSiru writes these four regional levels.
        private static readonly int[] NativeClothesLevels =
        {
            Shader.PropertyToID("_liquidftop"), Shader.PropertyToID("_liquidfbot"),
            Shader.PropertyToID("_liquidbtop"), Shader.PropertyToID("_liquidbbot")
        };

        private readonly HashSet<Renderer> _scannedClothes = new HashSet<Renderer>();

        private void SuppressNativeClothes(int frame)
        {
            if (!KoikatsuStudio.IsInstance() || SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                return;

            foreach (var item in KoikatsuStudio.Instance.dicObjectCtrl.Values)
            {
                if (!(item is OCIChar actor) || !actor.charInfo || !actor.charInfo.isActiveAndEnabled)
                    continue;

                var character = actor.charInfo;
                var body = character.rendBody ? character.rendBody.sharedMaterial : character.customMatBody;
                var face = character.rendFace ? character.rendFace.sharedMaterial : character.customMatFace;
                _accessoryRoots.Clear();
                AddSlotRoots(character.objAccessory);
                AddSlotRoots(character.objHair);
                AddSlotRoots(character.objClothes);
                AddSlotRoots(character.objParts);
                _scannedClothes.Clear();
                SuppressClothesRoots(character, character.objClothes, body, face, frame);
                SuppressClothesRoots(character, character.objParts, body, face, frame);
            }
            _scannedClothes.Clear();
        }

        private void SuppressClothesRoots(ChaControl character, GameObject[] roots, Material body, Material face, int frame)
        {
            if (roots == null) return;
            foreach (var root in roots)
            {
                if (!root || !root.activeInHierarchy) continue;
                root.GetComponentsInChildren(false, _renderers);
                foreach (var renderer in _renderers)
                {
                    if (!renderer || !renderer.enabled || renderer == character.rendBody || renderer == character.rendFace ||
                        !BelongsToSlot(renderer.transform, root.transform) || !_scannedClothes.Add(renderer))
                        continue;

                    var materials = renderer.sharedMaterials;
                    for (var index = 0; index < materials.Length; index++)
                    {
                        var source = materials[index];
                        if (!IsGarmentMaterial(source, body, face) || !HasNativeClothesJuices(source))
                            continue;

                        // Use the same render-time material copies as the skin fixes. Unity and the
                        // Material Editor keep the unmodified material and levels between renders.
                        var display = PrepareRenderMaterial(renderer, index, source, frame);
                        foreach (var property in NativeClothesLevels)
                            if (display.HasProperty(property)) display.SetFloat(property, 0f);
                    }
                }
                _renderers.Clear();
            }
        }

        private static bool HasNativeClothesJuices(Material material)
        {
            foreach (var property in NativeClothesLevels)
                if (material.HasProperty(property) && material.GetFloat(property) > 0f) return true;
            return false;
        }
    }
}
