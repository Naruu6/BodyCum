using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private const float ClothesPatternSpan = .42f;

        private readonly Dictionary<OCIChar, ClothesJuiceProfile> _clothesProfiles = new Dictionary<OCIChar, ClothesJuiceProfile>();
        private readonly List<Renderer> _nativeClothesRenderers = new List<Renderer>();

        internal ClothesJuiceProfile GetClothes(OCIChar character) =>
            character != null && _clothesProfiles.TryGetValue(character, out var profile) ? profile : null;

        internal ClothesJuiceProfile EditClothes(OCIChar character)
        {
            var profile = GetClothes(character);
            if (profile == null)
            {
                profile = new ClothesJuiceProfile();
                _clothesProfiles.Add(character, profile);
            }
            return profile;
        }

        internal void SetClothes(OCIChar character, ClothesJuiceProfile profile) => _clothesProfiles[character] = profile;

        internal bool HasActiveClothes
        {
            get
            {
                foreach (var entry in _clothesProfiles)
                    if (BodyCumPlugin.IsCharacterEnabled(entry.Key) && entry.Value.Level != 0 && entry.Value.Slots.Count != 0)
                        return true;
                return false;
            }
        }

        internal void ApplyNativeClothes(OCIChar studioCharacter, Action<Material> apply)
        {
            var profile = GetClothes(studioCharacter);
            var character = studioCharacter.charInfo;
            if (profile == null || profile.Level != 1 || profile.HasTransform || profile.Slots.Count == 0 ||
                !character || !character.isActiveAndEnabled)
                return;

            var body = character.rendBody ? character.rendBody.sharedMaterial : character.customMatBody;
            var face = character.rendFace ? character.rendFace.sharedMaterial : character.customMatFace;
            _accessoryRoots.Clear();
            AddSlotRoots(character.objAccessory);
            AddSlotRoots(character.objClothes);
            foreach (var slot in profile.Slots)
            {
                if (!TryGetClothesRoot(character, slot, out var root))
                    continue;
                root.GetComponentsInChildren(false, _nativeClothesRenderers);
                foreach (var renderer in _nativeClothesRenderers)
                {
                    if (!renderer || !renderer.enabled || !BelongsToSlot(renderer.transform, root.transform))
                        continue;
                    // Studio gives each renderer its own first material when setting native Juices.
                    var source = renderer.sharedMaterial;
                    if (IsGarmentMaterial(source, body, face) && TryGetBodyPattern(source, out _))
                        apply(source);
                }
                _nativeClothesRenderers.Clear();
            }
        }

        private bool RecordClothes(Camera camera, ref CommandBuffer buffer, int frame)
        {
            var drawn = false;
            foreach (var entry in _clothesProfiles)
            {
                var profile = entry.Value;
                var character = entry.Key.charInfo;
                if (!character || !character.isActiveAndEnabled || !BodyCumPlugin.IsCharacterEnabled(entry.Key) ||
                    profile.Level == 0 || profile.Slots.Count == 0)
                    continue;
                var settings = _colors.GetSettings(entry.Key);
                if (settings.Opacity == 0 || !TryGetBodyPattern(character, out var bodyPattern))
                    continue;

                var body = character.rendBody ? character.rendBody.sharedMaterial : character.customMatBody;
                var face = character.rendFace ? character.rendFace.sharedMaterial : character.customMatFace;
                bool IncludeMaterial(Material source) => IsGarmentMaterial(source, body, face) && !_colors.IsCustomMaterial(source);

                var color = settings.ToShaderColor(QualitySettings.activeColorSpace == ColorSpace.Linear);
                var bodyRoot = character.objBodyBone ? character.objBodyBone.transform : character.transform;
                _accessoryRoots.Clear();
                AddSlotRoots(character.objAccessory);
                AddSlotRoots(character.objClothes);
                foreach (var slot in profile.Slots)
                {
                    var slotKey = SurfaceSlotKey(true, slot.Key, slot.Value);
                    if (!TryGetClothesRoot(character, slot, out var root) ||
                        !GetClothesProjection(entry.Key, root, bodyRoot, slotKey, source => IsGarmentMaterial(source, body, face),
                            profile, frame, out var projection, out var span))
                        continue;
                    if (!EnsureShader())
                        return drawn;

                    // Calibrate neutral droplet size to the unscaled garment, then let Size scale the projection.
                    var pattern = bodyPattern;
                    pattern.Scale *= span / ClothesPatternSpan;
                    pattern.Offset += (bodyPattern.Scale - pattern.Scale) * .5f;
                    buffer = buffer ?? CreateDrawBuffer(camera);
                    drawn |= RecordSlot(buffer, camera, entry.Key, root, bodyRoot, slotKey, projection, pattern, color, profile.Level,
                        settings.Density / 100f, frame, IncludeMaterial);
                }
            }
            return drawn;
        }

        private bool GetClothesProjection(OCIChar actor, GameObject root, Transform anchor, string slotKey, Func<Material, bool> includeMaterial,
            ClothesJuiceProfile profile, int frame, out Matrix4x4 projection, out float span)
        {
            projection = Matrix4x4.identity;
            var min = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
            root.GetComponentsInChildren(false, _renderers);
            foreach (var renderer in _renderers)
            {
                if (!renderer || !BelongsToSlot(renderer.transform, root.transform) || !GetMesh(renderer))
                    continue;

                if (Array.Exists(renderer.sharedMaterials, includeMaterial.Invoke) &&
                    TryGetSurfaceMesh(actor, renderer, root.transform, anchor, slotKey, frame, false, out var surface))
                {
                    min = Vector3.Min(min, surface.Reference.Minimum);
                    max = Vector3.Max(max, surface.Reference.Maximum);
                }
            }
            _renderers.Clear();

            span = 0f;
            if (float.IsInfinity(min.x))
                return false;

            var center = (min + max) * .5f;
            center.x += profile.Horizontal * ProjectionOffsetX;
            center.y += profile.Vertical * ProjectionOffsetY;
            var size = max - min;
            span = Mathf.Max(.04f, Mathf.Max(size.x, size.y) * 1.16f);
            var depth = Mathf.Max(.04f, size.z * 1.16f);
            var scale = profile.Size / 100f;
            for (var axis = 0; axis < 3; axis++)
            {
                var row = projection.GetRow(axis);
                row.w -= center[axis];
                projection.SetRow(axis, row / ((axis == 2 ? depth : span) * scale));
            }
            return true;
        }

        private static void IncludeBounds(Bounds bounds, Matrix4x4 worldToAnchor, ref Vector3 min, ref Vector3 max)
        {
            var lower = bounds.min;
            var upper = bounds.max;
            for (var corner = 0; corner < 8; corner++)
            {
                var local = worldToAnchor.MultiplyPoint3x4(new Vector3(
                    (corner & 1) == 0 ? lower.x : upper.x,
                    (corner & 2) == 0 ? lower.y : upper.y,
                    (corner & 4) == 0 ? lower.z : upper.z));
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        private static bool TryGetClothesRoot(ChaControl character, KeyValuePair<int, string> slot, out GameObject root) =>
            ClothesJuiceParts.TryGet(character, slot.Key, out root, out var identity) && identity == slot.Value && root.activeInHierarchy;

        private static bool IsGarmentMaterial(Material source, Material body, Material face) =>
            source && source.shader && source != body && source != face && !JuiceBodyClones.IsBodyCopy(source, body);
    }
}
