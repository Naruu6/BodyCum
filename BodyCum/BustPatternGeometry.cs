using System.Collections.Generic;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class BustPatternGeometry
    {
        private const int RaySamples = 26;
        private const float RayStep = .04f;
        private const int InnerSample = 6;
        private const int OuterSample = 16;
        private const float OuterBlend = .7971939f;
        private readonly Dictionary<ChaControl, Measurement> _measurements = new Dictionary<ChaControl, Measurement>();
        private readonly List<ChaControl> _expired = new List<ChaControl>();
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private Mesh _baked;
        private float _nextTrim;

        private sealed class Measurement
        {
            internal Mesh Source;
            internal SurfacePoint[] Points;
            internal Vector4 Gain;
            internal float Size = float.NaN;
            internal float NextSample;
            internal int LastFrame;
        }

        private struct SurfacePoint
        {
            internal int A, B, C;
            internal float U, V;

            internal Vector3 Read(List<Vector3> vertices)
            {
                var origin = vertices[A];
                return origin + (vertices[B] - origin) * U + (vertices[C] - origin) * V;
            }
        }

        internal Vector4 GetGain(ChaControl character, int frame)
        {
            var renderer = character.rendBody as SkinnedMeshRenderer;
            var size = character.GetShapeBodyValue(4);
            if (!renderer || !renderer.sharedMesh) return Fallback(size);

            var now = Time.unscaledTime;
            if (now >= _nextTrim)
            {
                _nextTrim = now + 5f;
                foreach (var other in _measurements)
                    if (!other.Key || frame - other.Value.LastFrame > 600) _expired.Add(other.Key);
                foreach (var expired in _expired) _measurements.Remove(expired);
                _expired.Clear();
            }
            var mesh = renderer.sharedMesh;
            if (!_measurements.TryGetValue(character, out var state) || state.Source != mesh)
            {
                state = new Measurement { Source = mesh, Gain = Fallback(size), Points = FindPoints(mesh) };
                _measurements[character] = state;
            }
            state.LastFrame = frame;
            if (state.Points == null) return Fallback(size);
            if (state.Size == size && now < state.NextSample) return state.Gain;

            state.Size = size;
            state.NextSample = now + .65f;
            try
            {
                if (!_baked) _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                renderer.BakeMesh(_baked);
                _baked.GetVertices(_vertices);
                if (_vertices.Count != mesh.vertexCount) return state.Gain;
                // Unity 5.6 already includes lossy scale when baking; remove the Studio
                // character transform so resizing an entire character does not repaint it.
                var toCharacter = character.transform.worldToLocalMatrix *
                    Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one);
                var gain = Measure(state.Points, toCharacter);
                // Avoid replacing render textures for sub-pixel physics and float jitter.
                state.Gain = new Vector4(Steady(gain.x, state.Gain.x), Steady(gain.y, state.Gain.y),
                    Steady(gain.z, state.Gain.z), Steady(gain.w, state.Gain.w));
            }
            catch (UnityException) { state.Points = null; state.Gain = Fallback(size); }
            return state.Gain;
        }

        internal void Clear()
        {
            _measurements.Clear();
            _vertices.Clear();
            if (_baked) Object.Destroy(_baked);
            _baked = null;
        }

        private static float Steady(float value, float old)
        {
            if (value == 0f) return 0f;
            return Mathf.Abs(value - old) < .02f ? old : Mathf.Round(value * 64f) / 64f;
        }

        private static SurfacePoint[] FindPoints(Mesh mesh)
        {
            if (!mesh.isReadable) return null;
            var uv = mesh.uv;
            if (uv.Length != mesh.vertexCount) return null;
            var triangles = mesh.triangles;
            var points = new SurfacePoint[8 * (OuterSample + 1)];
            for (int ray = 0; ray < 8; ray++)
            {
                var center = new Vector2(ray < 4 ? .1107f : .2025f, .8453f);
                var direction = (ray & 2) == 0 ? new Vector2(.0455f, 0f) : new Vector2(0f, .053f);
                if ((ray & 1) == 0) direction = -direction;
                for (int step = 0; step <= OuterSample; step++)
                {
                    var position = center + direction * (step * RayStep);
                    if (!FindPoint(uv, triangles, position, out points[ray * (OuterSample + 1) + step]))
                        return null;
                }
            }
            return points;
        }

        private static bool FindPoint(Vector2[] uv, int[] triangles, Vector2 target, out SurfacePoint point)
        {
            point = default;
            float best = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                int a = triangles[i], b = triangles[i + 1], c = triangles[i + 2];
                var du = uv[b] - uv[a];
                var dv = uv[c] - uv[a];
                float det = du.x * dv.y - du.y * dv.x;
                var area = Mathf.Abs(det);
                if (area <= best || area < 1e-9f) continue;
                var delta = target - uv[a];
                var u = (delta.x * dv.y - delta.y * dv.x) / det;
                var v = (du.x * delta.y - du.y * delta.x) / det;
                if (u >= -.00001f && v >= -.00001f && u + v <= 1.00001f)
                {
                    point = new SurfacePoint { A = a, B = b, C = c, U = u, V = v };
                    best = area;
                }
            }
            return best > 0f;
        }

        private Vector4 Measure(SurfacePoint[] points, Matrix4x4 toCharacter)
        {
            var outer = Vector2.zero;
            var inner = Vector2.zero;
            for (int ray = 0; ray < 8; ray++)
            {
                int start = ray * (OuterSample + 1);
                var previous = points[start].Read(_vertices);
                float length = 0f, inside = 0f;
                for (int step = 1; step <= OuterSample; step++)
                {
                    var next = points[start + step].Read(_vertices);
                    length += toCharacter.MultiplyVector(next - previous).magnitude;
                    previous = next;
                    if (step == InnerSample) inside = length;
                }
                int axis = (ray & 2) == 0 ? 0 : 1;
                inner[axis] += RelativeGain(ray, inside, InnerSample) * .25f;
                outer[axis] += RelativeGain(ray, length, OuterSample) * .25f;
            }
            // Neutral pose and body variants can differ a few percent in arc length.
            if (outer.x <= .06f && outer.y <= .06f && inner.x <= .06f && inner.y <= .06f)
                return Vector4.zero;
            inner.x = Mathf.Max(0f, inner.x);
            inner.y = Mathf.Max(0f, inner.y);
            outer = inner + (outer - inner) / OuterBlend;
            return new Vector4(Mathf.Max(inner.x, outer.x), Mathf.Max(inner.y, outer.y), inner.x, inner.y);
        }

        private static float RelativeGain(int ray, float distance, int originalStep)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance <= 0f) return 0f;
            int first = ray * RaySamples;
            int baseIndex = 1;
            while (baseIndex < RaySamples - 1 && NeutralArc[first + baseIndex] < distance) baseIndex++;
            float lo = NeutralArc[first + baseIndex - 1], hi = NeutralArc[first + baseIndex];
            // Extrapolate the end of the reference path for extended sliders and external scaling.
            return (baseIndex - 1f + (distance - lo) / (hi - lo)) / originalStep - 1f;
        }

        private static Vector4 Fallback(float size)
        {
            var gain = ScaleForSize(size) - Vector2.one;
            return new Vector4(gain.x, gain.y, gain.x * (.1190f / .6654f), gain.y * (.1184f / .8895f));
        }

        // Conservative fallback for unreadable or incompatible meshes.
        private static readonly Vector2[] StockRatios = {
            Vector2.one, new Vector2(1.0734f, 1.0816f), new Vector2(1.1449f, 1.1697f),
            new Vector2(1.2144f, 1.2590f), new Vector2(1.2823f, 1.3444f),
            new Vector2(1.3484f, 1.4259f), new Vector2(1.4146f, 1.5097f),
            new Vector2(1.4785f, 1.6215f), new Vector2(1.5413f, 1.7165f),
            new Vector2(1.6038f, 1.8018f), new Vector2(1.6654f, 1.8895f)
        };

        internal static Vector2 ScaleForSize(float size)
        {
            if (!(size > .5f) || float.IsInfinity(size)) return Vector2.one;
            if (size >= 1f)
                return StockRatios[10] + (StockRatios[10] - StockRatios[9]) * ((size - 1f) * 20f);
            float index = (size - .5f) * 20f;
            int lo = (int)index;
            return Vector2.Lerp(StockRatios[lo], StockRatios[lo + 1], index - lo);
        }
    }
}
