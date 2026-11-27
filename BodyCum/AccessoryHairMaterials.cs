using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private static bool SameStrandShaders(SurfaceMesh surface, Material[] sources)
        {
            var cached = surface.StrandShaders;
            if (cached == null || cached.Length != sources.Length) return false;
            for (var i = 0; i < sources.Length; i++)
                if (surface.StrandMaterials[i] != sources[i] || cached[i] != (sources[i] ? sources[i].shader : null)) return false;
            return true;
        }

        private static void GetHairRenderers(Transform root, HashSet<Renderer> hair, HashSet<Renderer> decorations)
        {
            foreach (var component in root.GetComponentsInChildren<ChaAccessoryComponent>(true))
                if (component.rendHair != null) hair.UnionWith(component.rendHair);
            foreach (var component in root.GetComponentsInChildren<ChaCustomHairComponent>(true))
            {
                if (component.rendHair != null) hair.UnionWith(component.rendHair);
                if (component.rendAccessory != null) decorations.UnionWith(component.rendAccessory);
            }
        }

        private static bool IsHairMaterial(Material material)
        {
            if (!material || !material.shader) return false;
            // Some accessory authors use the material "hair" with the ordinary opaque shader.
            // A looser material-name match would also classify hair ties as hair.
            if (string.Equals(material.name, "hair", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(material.name, "hair (Instance)", StringComparison.OrdinalIgnoreCase)) return true;
            var name = material.shader.name;
            for (var i = name.IndexOf("hair", StringComparison.OrdinalIgnoreCase); i >= 0;
                 i = name.IndexOf("hair", i + 4, StringComparison.OrdinalIgnoreCase))
            {
                // Match a shader word such as main_hair_front or HairFrontPlus, including a camel-case prefix.
                if (i == 0 || !char.IsLetter(name[i - 1]) || (char.IsUpper(name[i]) && char.IsLower(name[i - 1])))
                    return true;
            }
            return false;
        }

        private static sbyte[] GetHairTriangleKinds(Mesh mesh, int triangleCount, Material[] materials,
            bool hairRenderer, bool decorationRenderer)
        {
            var kinds = new sbyte[mesh.subMeshCount];
            var hasHairMaterial = false;
            for (var sub = 0; sub < kinds.Length && sub < materials.Length; sub++)
            {
                if (!IsHairMaterial(materials[sub])) continue;
                kinds[sub] = 1;
                hasHairMaterial = true;
            }
            if (!hasHairMaterial && !hairRenderer && !decorationRenderer) return null;

            var triangles = new sbyte[triangleCount];
            var offset = 0;
            for (var sub = 0; sub < kinds.Length; sub++)
            {
                if (mesh.GetTopology(sub) != MeshTopology.Triangles) return null;
                var count = (int)(mesh.GetIndexCount(sub) / 3);
                if (count > triangleCount - offset) return null;
                // A mixed renderer can contain hair, the scalp, and jewellery in separate submeshes.
                var kind = kinds[sub];
                if (decorationRenderer && !hairRenderer) kind = -1;
                else if (kind == 0 && hasHairMaterial) kind = -1;
                else if (kind == 0 && hairRenderer) kind = 1;
                for (var t = offset; t < offset + count; t++) triangles[t] = kind;
                offset += count;
            }
            return offset == triangleCount ? triangles : null;
        }
    }
}
