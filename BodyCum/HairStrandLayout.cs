using System;
using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    // Allocate one constant mask phase per overlapping ribbon, in the frozen head frame.
    // Texture coordinates and triangle topology of the accessory itself never change.
    internal static class HairStrandLayout
    {
        internal sealed class Part
        {
            internal readonly string Key;
            internal readonly Vector3[] Points, Rest;
            internal readonly Vector2[] Uv;
            internal readonly int[] Triangles;
            internal readonly sbyte[] HairKind;
            internal readonly string Slot;

            internal Part(string stableKey, Vector3[] referencePositions, int[] triangles,
                Vector3[] seamPositions = null, Vector2[] surfaceUv = null,
                sbyte[] triangleHairKind = null, string groupKey = null)
            {
                Key = stableKey ?? string.Empty;
                Points = referencePositions;
                Triangles = triangles;
                Rest = seamPositions;
                Uv = surfaceUv;
                HairKind = triangleHairKind != null && triangles != null && triangleHairKind.Length == triangles.Length / 3
                    ? triangleHairKind : null;
                Slot = groupKey ?? string.Empty;
            }
        }

        private const int Grid = 128;
        private const int RasterBudget = 14000000;
        private const float AccessoryOffsetScale = .25f;

        private struct Box
        {
            internal Vector3 Min, Max;
            internal void Include(Vector3 p)
            {
                Min.x = Math.Min(Min.x, p.x); Max.x = Math.Max(Max.x, p.x);
                Min.y = Math.Min(Min.y, p.y); Max.y = Math.Max(Max.y, p.y);
                Min.z = Math.Min(Min.z, p.z); Max.z = Math.Max(Max.z, p.z);
            }
        }

        private sealed class Island
        {
            internal int Part, Root, Color, Band;
            internal bool KnownHair, OtherMaterial, Legacy;
            internal Box Bounds;
            internal readonly List<int> Triangles = new List<int>();
            internal readonly Dictionary<int, float> Pixels = new Dictionary<int, float>();
        }

        private sealed class Sets
        {
            private readonly int[] parent;
            internal Sets(int count)
            {
                parent = new int[count];
                for (int i = 0; i < count; i++) parent[i] = i;
            }

            internal int Root(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }

            internal void Join(int a, int b)
            {
                a = Root(a); b = Root(b);
                if (a < b) parent[b] = a;
                else if (b < a) parent[a] = b;
            }
        }

        // A UV chart can be split at a normal/material boundary. An entire matching
        // edge in both bind position and UV0 identifies the two sides of that seam.
        // Welding by position alone would join independent hair strips at the scalp.
        private struct SeamVertex : IEquatable<SeamVertex>
        {
            private readonly Vector3 p;
            private readonly int u, v;
            internal SeamVertex(Vector3 point, Vector2 uv)
            {
                p = point;
                u = (int)Math.Round(uv.x * 100000.0);
                v = (int)Math.Round(uv.y * 100000.0);
            }
            public bool Equals(SeamVertex other)
            {
                return p.x == other.p.x && p.y == other.p.y && p.z == other.p.z && u == other.u && v == other.v;
            }
            public override bool Equals(object obj) { return obj is SeamVertex && Equals((SeamVertex)obj); }
            public override int GetHashCode()
            {
                unchecked { return (((p.x.GetHashCode() * 397 ^ p.y.GetHashCode()) * 397 ^ p.z.GetHashCode()) * 397 ^ u) * 397 ^ v; }
            }
        }

        private static void JoinSeam(Dictionary<long, int> edges, Sets sets, int owner, int a, int b)
        {
            if (a == b) return;
            long key = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            int previous;
            if (edges.TryGetValue(key, out previous)) sets.Join(previous, owner);
            else edges.Add(key, owner);
        }

        // The default Int64 hash XORs the endpoints. Most consecutively numbered
        // strip edges then collide in just a few buckets during slot initialization.
        private sealed class EdgeComparer : IEqualityComparer<long>
        {
            internal static readonly EdgeComparer Instance = new EdgeComparer();
            public bool Equals(long a, long b) { return a == b; }
            public int GetHashCode(long key)
            {
                unchecked { return ((int)(key >> 32) * 16777619) ^ (int)key; }
            }
        }

        private static void Stitch(Part part, Sets sets)
        {
            int n = part.Points.Length;
            if (part.Rest == null || part.Rest.Length != n || part.Uv == null || part.Uv.Length != n) return;
            var vertices = new Dictionary<SeamVertex, int>(n);
            int[] ids = new int[n];
            bool hasDuplicate = false;
            for (int i = 0; i < n; i++)
            {
                var key = new SeamVertex(part.Rest[i], part.Uv[i]);
                int previous;
                if (vertices.TryGetValue(key, out previous)) { ids[i] = previous; hasDuplicate = true; }
                else { ids[i] = i; vertices.Add(key, i); }
            }
            if (!hasDuplicate) return;
            var edges = new Dictionary<long, int>(EdgeComparer.Instance);
            var t = part.Triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = ids[t[i]], b = ids[t[i + 1]], c = ids[t[i + 2]];
                JoinSeam(edges, sets, t[i], a, b);
                JoinSeam(edges, sets, t[i], b, c);
                JoinSeam(edges, sets, t[i], c, a);
            }
        }

        private static void Collect(Part part, int partIndex, List<Island> islands)
        {
            int[] t = part.Triangles;
            var sets = new Sets(part.Points.Length);
            for (int i = 0; i < t.Length; i += 3)
            {
                int a = t[i], b = t[i + 1], c = t[i + 2];
                if ((uint)a >= part.Points.Length || (uint)b >= part.Points.Length || (uint)c >= part.Points.Length) return;
                sets.Join(a, b);
                sets.Join(a, c);
            }
            Stitch(part, sets);
            var local = new Dictionary<int, Island>();
            for (int i = 0; i < t.Length; i += 3)
            {
                int root = sets.Root(t[i]);
                Island island;
                if (!local.TryGetValue(root, out island))
                {
                    var p = part.Points[t[i]];
                    island = new Island { Part = partIndex, Root = root, Bounds = new Box { Min = p, Max = p } };
                    local.Add(root, island);
                    islands.Add(island);
                }
                island.Triangles.Add(i);
                if (part.HairKind != null)
                {
                    int kind = part.HairKind[i / 3];
                    island.KnownHair |= kind > 0;
                    island.OtherMaterial |= kind < 0;
                }
                for (int j = i; j < i + 3; j++) island.Bounds.Include(part.Points[t[j]]);
            }
        }

        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }

        // For an unlabelled material use actual surface area and boundary length.
        // A thin open ribbon retains a high perimeter-to-area ratio even curled up,
        // rotated sideways, or much smaller than another ribbon in this accessory.
        private static bool IsRibbon(Island island, Part part)
        {
            if (island.OtherMaterial) return false;
            if (island.KnownHair) return true;
            var edges = new HashSet<long>(EdgeComparer.Instance);
            var p = part.Rest != null && part.Rest.Length == part.Points.Length ? part.Rest : part.Points;
            double twiceArea = 0;
            foreach (int tri in island.Triangles)
            {
                int a = part.Triangles[tri], b = part.Triangles[tri+1], c = part.Triangles[tri+2];
                double x1 = p[b].x-p[a].x, y1 = p[b].y-p[a].y, z1 = p[b].z-p[a].z;
                double x2 = p[c].x-p[a].x, y2 = p[c].y-p[a].y, z2 = p[c].z-p[a].z;
                double nx=y1*z2-z1*y2, ny=z1*x2-x1*z2, nz=x1*y2-y1*x2;
                twiceArea += Math.Sqrt(nx*nx+ny*ny+nz*nz);
                ToggleEdge(edges,a,b); ToggleEdge(edges,b,c); ToggleEdge(edges,c,a);
            }
            if (edges.Count < 3 || twiceArea <= 0) return false;
            double perimeter = 0;
            foreach (long edge in edges)
            {
                Vector3 a = p[(int)(edge >> 32)], b = p[(int)edge];
                double dx = a.x-b.x, dy = a.y-b.y, dz = a.z-b.z;
                perimeter += Math.Sqrt(dx*dx+dy*dy+dz*dz);
            }
            // Avoid treating round or square ornament plates as unlabelled hair.
            return perimeter * perimeter >= 15 * twiceArea;
        }

        private static void ToggleEdge(HashSet<long> edges, int a, int b)
        {
            if(a == b) return;
            long edge = ((long)Math.Min(a, b) << 32) | (uint)Math.Max(a, b);
            if(!edges.Add(edge)) edges.Remove(edge);
        }

        internal static float[][] Build(IList<Part> parts)
        {
            var result = new float[parts.Count][];
            var bySlot = new SortedDictionary<string, List<int>>(StringComparer.Ordinal);
            for (int i = 0; i < parts.Count; i++)
            {
                var part = parts[i];
                if (part == null || part.Points == null || part.Triangles == null || part.Triangles.Length % 3 != 0) continue;
                List<int> members;
                if (!bySlot.TryGetValue(part.Slot, out members))
                    bySlot.Add(part.Slot, members = new List<int>());
                members.Add(i);
            }
            var groups = new List<List<Island>>();
            foreach (var group in bySlot.Values)
            {
                long vertices = 0, indices = 0;
                foreach (int i in group) { vertices += parts[i].Points.Length; indices += parts[i].Triangles.Length; }
                if (vertices > 300000 || indices > 1200000) continue;
                var islands = new List<Island>();
                foreach (int i in group) Collect(parts[i], i, islands);
                if (islands.Count > 0) groups.Add(SolveSlot(parts, islands));
            }
            ResolveOtherSlots(parts, groups);
            foreach (var group in groups)
                foreach (var ribbon in group)
                {
                    if (ribbon.Color == 0 && ribbon.Band == 0) continue;
                    var part = parts[ribbon.Part];
                    var phases = result[ribbon.Part] ?? (result[ribbon.Part] = new float[part.Points.Length]);
                    float phase = Phase(ribbon.Color);
                    // Distinct depth layers across separate slots can originally have
                    // the same local color. Give only the conflicting lock an offset;
                    // shifts are independent of unrelated slots' color counts.
                    if (ribbon.Band > 0)
                        phase = (float)((phase + ribbon.Band * 0.10355339059327376) % 0.25);
                    // Keep distinct samples on overlapping accessory strands with a smaller shift from the frontal projection.
                    if (part.Slot.StartsWith("accessory/", StringComparison.Ordinal))
                        phase *= AccessoryOffsetScale;
                    foreach (int tri in ribbon.Triangles)
                        for (int v = 0; v < 3; v++) phases[part.Triangles[tri+v]] = phase;
                }
            return result;
        }

        private static List<Island> SolveSlot(IList<Part> parts, List<Island> islands)
        {
            Box scene = islands[0].Bounds;
            for (int i = 1; i < islands.Count; i++)
            {
                scene.Include(islands[i].Bounds.Min);
                scene.Include(islands[i].Bounds.Max);
            }
            float width = scene.Max.x - scene.Min.x, height = scene.Max.y - scene.Min.y;
            var ribbons = new List<Island>();
            if (!Finite(width) || !Finite(height) || width <= 0.00001f || height <= 0.00001f) return ribbons;
            var extras = new List<Island>();
            foreach (var island in islands)
            {
                Box b = island.Bounds;
                float h = b.Max.y - b.Min.y;
                island.Legacy = h >= height * 0.14f && h >= (b.Max.x-b.Min.x) * 1.28f && h >= (b.Max.z-b.Min.z) * 1.05f;
                if (island.Legacy) ribbons.Add(island);
                else if (IsRibbon(island, parts[island.Part])) extras.Add(island);
            }
            if (ribbons.Count > 512) return new List<Island>();
            Comparison<Island> stableOrder = delegate(Island a, Island b) {
                int key = string.CompareOrdinal(parts[a.Part].Key, parts[b.Part].Key);
                if (key != 0) return key;
                key = a.Root.CompareTo(b.Root);
                return key != 0 ? key : a.Part.CompareTo(b.Part);
            };
            ribbons.Sort(stableOrder);
            int budget = RasterBudget;
            foreach (var ribbon in ribbons)
                if (!Raster(ribbon, parts[ribbon.Part], scene.Min, Grid / width, Grid / height, Grid, ribbon.Pixels, ref budget)) return new List<Island>();

            float depthTolerance = Math.Max(height, width) * 0.0008f;
            var used = new bool[513];
            budget = RasterBudget;
            for (int i = 1; i < ribbons.Count; i++)
            {
                Array.Clear(used, 0, i);
                for (int j = 0; j < i; j++)
                {
                    Island other = ribbons[j];
                    if (!used[other.Color] && Intersects(ribbons[i].Bounds, other.Bounds) &&
                        Overlaps(ribbons[i].Pixels, other.Pixels, depthTolerance, ref budget)) used[other.Color] = true;
                    if (budget < 0) return new List<Island>();
                }
                int color = 0;
                while (used[color]) color++;
                ribbons[i].Color = color;
            }
            // Add newer candidates after all legacy colors have been determined.
            // Reserve independent work budgets so dense short-hair geometry cannot
            // discard a previously successful long-hair assignment.
            extras.Sort(stableOrder);
            var local = new PairRaster(parts);
            foreach (var ribbon in extras)
            {
                if (ribbons.Count >= 512 || local.Budget <= 0) break;
                Array.Clear(used, 0, ribbons.Count + 1);
                foreach (var other in ribbons)
                {
                    if (!other.OtherMaterial && !used[other.Color] && local.Overlap(ribbon, other)) used[other.Color] = true;
                    if (local.Budget <= 0) break;
                }
                if (local.Budget <= 0) break;
                while (used[ribbon.Color]) ribbon.Color++;
                ribbons.Add(ribbon);
            }
            return ribbons;
        }

        private static bool Intersects(Box a, Box b)
        {
            return a.Min.x < b.Max.x && b.Min.x < a.Max.x && a.Min.y < b.Max.y && b.Min.y < a.Max.y;
        }

        private sealed class PairRaster
        {
            internal int Budget = RasterBudget;
            private readonly IList<Part> parts;
            private readonly Dictionary<int,float> first = new Dictionary<int,float>();
            private readonly Dictionary<int,float> second = new Dictionary<int,float>();
            internal PairRaster(IList<Part> parts) { this.parts = parts; }

            internal bool Overlap(Island a, Island b)
            {
                if (!Intersects(a.Bounds,b.Bounds)) return false;
                Box region = a.Bounds;
                region.Include(b.Bounds.Min); region.Include(b.Bounds.Max);
                float width = region.Max.x-region.Min.x, height = region.Max.y-region.Min.y;
                if (!Finite(width) || !Finite(height) || width <= 0.000001f || height <= 0.000001f) return false;
                // Sample in the bounds of this pair. A short bang or hairpin remains
                // resolvable when a ponytail (or another slot) is many times larger.
                const int resolution = 48;
                first.Clear(); second.Clear();
                Budget -= a.Triangles.Count + b.Triangles.Count;
                if (Budget <= 0 || !Raster(a,parts[a.Part],region.Min,resolution/width,resolution/height,resolution,first,ref Budget) ||
                    !Raster(b,parts[b.Part],region.Min,resolution/width,resolution/height,resolution,second,ref Budget)) return false;
                return Overlaps(first,second,Math.Max(width,height)*0.0008f,ref Budget);
            }
        }

        private static void ResolveOtherSlots(IList<Part> parts, List<List<Island>> groups)
        {
            if (groups.Count < 2) return;
            var raster = new PairRaster(parts);
            var bounds = new Box[groups.Count];
            int checks = 2000000;
            for(int i=0;i<groups.Count;i++)
            {
                var group = groups[i];
                if (group.Count == 0) continue;
                bounds[i] = group[0].Bounds;
                foreach (var ribbon in group)
                {
                    bounds[i].Include(ribbon.Bounds.Min);
                    bounds[i].Include(ribbon.Bounds.Max);
                }
                var neighbors = new List<Island>();
                for(int j=0;j<i;j++)
                {
                    if (--checks < 0) return;
                    if(groups[j].Count > 0 && Intersects(bounds[i],bounds[j])) neighbors.AddRange(groups[j]);
                }
                if (neighbors.Count == 0) continue;
                foreach (var ribbon in group)
                {
                    if (ribbon.OtherMaterial) continue;
                    var blocked = new HashSet<int>();
                    foreach (var other in neighbors)
                    {
                        if (--checks < 0) return;
                        // A band change preserves differences between original colors,
                        // including every valid intra-slot legacy conflict.
                        if (!other.OtherMaterial && ribbon.Color == other.Color && !blocked.Contains(other.Band) && raster.Overlap(ribbon, other))
                            blocked.Add(other.Band);
                        if (raster.Budget <= 0) return;
                    }
                    while (blocked.Contains(ribbon.Band)) ribbon.Band++;
                }
            }
        }

        private static float Phase(int color)
        {
            // The stock atlas itself repeats at quarter-U. Subdivide that interval
            // largest-gap-first, so phases distinct in UV also sample distinct motifs.
            float phase = 0, step = 0.125f;
            while (color > 0)
            {
                if ((color & 1) != 0) phase += step;
                color >>= 1;
                step *= 0.5f;
            }
            return phase;
        }

        private static bool Raster(Island island, Part part, Vector3 origin, float sx, float sy,
            int resolution, Dictionary<int,float> pixels, ref int budget)
        {
            var t = part.Triangles;
            foreach (int tri in island.Triangles)
            {
                Vector3 a = part.Points[t[tri]], b = part.Points[t[tri + 1]], c = part.Points[t[tri + 2]];
                float ax = (a.x - origin.x) * sx, ay = (a.y - origin.y) * sy;
                float bx = (b.x - origin.x) * sx, by = (b.y - origin.y) * sy;
                float cx = (c.x - origin.x) * sx, cy = (c.y - origin.y) * sy;
                float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
                if (Math.Abs(area) < 0.00001f) continue;
                int x0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ax, Math.Min(bx, cx)) - 0.5f));
                int x1 = Math.Min(resolution - 1, (int)Math.Floor(Math.Max(ax, Math.Max(bx, cx)) - 0.5f));
                int y0 = Math.Max(0, (int)Math.Ceiling(Math.Min(ay, Math.Min(by, cy)) - 0.5f));
                int y1 = Math.Min(resolution - 1, (int)Math.Floor(Math.Max(ay, Math.Max(by, cy)) - 0.5f));
                if (x0 > x1 || y0 > y1) continue;
                budget -= (x1 - x0 + 1) * (y1 - y0 + 1);
                if (budget < 0) return false;
                float inverse = 1 / area;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float px = x + 0.5f - ax, py = y + 0.5f - ay;
                        float u = (px * (cy - ay) - py * (cx - ax)) * inverse;
                        float v = ((bx - ax) * py - (by - ay) * px) * inverse;
                        if (u < 0 || v < 0 || u + v > 1) continue;
                        int key = y * resolution + x;
                        float z = a.z + u * (b.z - a.z) + v * (c.z - a.z), old;
                        if (!pixels.TryGetValue(key, out old) || z < old) pixels[key] = z;
                    }
            }
            return true;
        }

        private static bool Overlaps(Dictionary<int,float> a, Dictionary<int,float> b, float tolerance, ref int budget)
        {
            var small = a.Count <= b.Count ? a : b;
            var large = a.Count <= b.Count ? b : a;
            int needed = Math.Max(8, (int)Math.Ceiling(Math.Max(small.Count * 0.16, large.Count * 0.045)));
            if (needed > small.Count) return false;
            budget -= small.Count;
            if (budget < 0) return false;
            int hits = 0;
            foreach (var point in small)
            {
                float other;
                if (large.TryGetValue(point.Key, out other) && Math.Abs(other - point.Value) > tolerance && ++hits >= needed) return true;
            }
            return false;
        }
    }
}
