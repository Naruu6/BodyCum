using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private readonly List<SurfaceMesh> _strandParts = new List<SurfaceMesh>();

        // All selected slots of one character share the same frozen head frame. Draw only after
        // gathering them so both renderers see the same decision in this camera pass.
        private void PrepareStrandLayout()
        {
            if (_strandParts.Count == 0) return;
            var previous = _strandParts[0].StrandGroup;
            var same = previous != null && previous.Length == _strandParts.Count;
            for (var i = 0; i < _strandParts.Count; i++)
            {
                var part = _strandParts[i];
                same = same && previous[i] == part && part.StrandGroup == previous;
            }
            if (same) return;

            var group = _strandParts.ToArray();
            var readable = new List<SurfaceMesh>();
            var input = new List<HairStrandLayout.Part>();
            var nativeHair = new HashSet<Renderer>();
            var nativeDecor = new HashSet<Renderer>();
            var scanned = new HashSet<Transform>();
            foreach (var part in group)
            {
                part.StrandGroup = group;
                var materials = part.Renderer.sharedMaterials;
                part.StrandMaterials = materials;
                part.StrandShaders = new Shader[materials.Length];
                for (var m = 0; m < materials.Length; m++)
                    part.StrandShaders[m] = materials[m] ? materials[m].shader : null;
                try
                {
                    var geometry = part.Snapshot ? part.Snapshot : part.Source;
                    if (!geometry.isReadable) continue;
                    var triangles = geometry.triangles;
                    if (triangles.Length < 3) continue;
                    var rest = part.Source.isReadable ? part.Source.vertices : null;
                    var head = part.Coordinates != null ? part.Coordinates.ToArray() : new Vector3[geometry.vertexCount];
                    if (part.Coordinates == null)
                        for (var v = 0; v < head.Length; v++)
                            head[v] = part.Reference.MeshToAnchor.MultiplyPoint3x4(rest[v]);
                    if (scanned.Add(part.SlotRoot)) GetHairRenderers(part.SlotRoot, nativeHair, nativeDecor);
                    var kinds = GetHairTriangleKinds(geometry, triangles.Length / 3, materials,
                        nativeHair.Contains(part.Renderer), nativeDecor.Contains(part.Renderer));
                    input.Add(new HairStrandLayout.Part(part.Key, head, triangles, rest ?? head, geometry.uv, kinds, part.SlotKey));
                    readable.Add(part);
                }
                catch (UnityException error)
                {
                    _log.LogDebug("Using the original accessory placement for " + part.Source.name + ": " + error.Message);
                }
            }
            if (input.Count == 0) return;
            var phases = HairStrandLayout.Build(input);
            for (var i = 0; i < readable.Count; i++)
            {
                if (SameStrandPhases(readable[i].StrandCoordinates, phases[i])) continue;
                try
                {
                    SetStrandPhases(readable[i], phases[i]);
                }
                catch (UnityException error)
                {
                    _log.LogDebug("Could not set strand coordinates on " + readable[i].Source.name + ": " + error.Message);
                }
            }
        }

        private static bool SameStrandPhases(List<Vector4> current, float[] phases)
        {
            if (current == null || phases == null) return current == null && phases == null;
            if (current.Count != phases.Length) return false;
            for (var i = 0; i < phases.Length; i++)
                if (current[i].w != phases[i]) return false;
            return true;
        }

        private static void SetStrandPhases(SurfaceMesh part, float[] phases)
        {
            if (phases == null)
            {
                if (part.StrandRigidCopy) UnityEngine.Object.Destroy(part.StrandRigidCopy);
                part.StrandRigidCopy = null;
                part.StrandCoordinates = null;
                if (part.Snapshot) part.Snapshot.SetUVs(1, part.Coordinates);
                return;
            }

            var target = part.Snapshot;
            var createRigidCopy = !target && !part.StrandRigidCopy;
            if (createRigidCopy)
            {
                target = UnityEngine.Object.Instantiate(part.Source);
                target.name = "BodyCum accessory strand snapshot";
                target.hideFlags = HideFlags.HideAndDontSave;
            }
            else if (!target) target = part.StrandRigidCopy;
            var packed = new List<Vector4>(phases.Length);
            for (var i = 0; i < phases.Length; i++)
            {
                var q = part.Coordinates != null ? part.Coordinates[i] : Vector3.zero;
                packed.Add(new Vector4(q.x, q.y, q.z, phases[i]));
            }
            try { target.SetUVs(1, packed); }
            catch
            {
                if (createRigidCopy) UnityEngine.Object.Destroy(target);
                throw;
            }
            if (!part.Snapshot) part.StrandRigidCopy = target;
            part.StrandCoordinates = packed;
        }
    }
}
