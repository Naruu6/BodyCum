using System;
using System.Collections.Generic;
using System.Text;
using Studio;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private const int PlacementWarmupFrames = 30;
        private readonly Dictionary<OCIChar, SurfaceProjectionState> _surfaceStates = new Dictionary<OCIChar, SurfaceProjectionState>();
        private readonly Dictionary<Renderer, SurfaceMesh> _surfaceMeshes = new Dictionary<Renderer, SurfaceMesh>();
        private readonly Dictionary<Renderer, SurfaceFailure> _surfaceFailures = new Dictionary<Renderer, SurfaceFailure>();

        private sealed class SurfaceFailure
        {
            internal OCIChar Actor;
            internal Mesh Source;
            internal int RetryFrame;
        }

        private sealed class SurfaceMesh
        {
            internal OCIChar Actor;
            internal Mesh Source;
            internal Renderer Renderer;
            internal Transform SlotRoot;
            internal Shader[] StrandShaders;
            internal Material[] StrandMaterials;
            internal Mesh Snapshot;
            internal Matrix4x4 DrawMatrix;
            internal string Key;
            internal string SlotKey;
            internal SurfaceProjectionReference Reference;
            internal List<Vector3> Coordinates;
            internal List<Vector4> StrandCoordinates;
            internal Mesh StrandRigidCopy;
            internal SurfaceMesh[] StrandGroup;
            internal int LastFrame;
            internal int BakeFrame = -1;
            internal int BakeCount;
            internal SurfacePoseStamp Pose;
        }

        internal SurfaceProjectionState GetSurfaceState(OCIChar actor) =>
            actor != null && _surfaceStates.TryGetValue(actor, out var state) ? state : null;

        internal SurfaceProjectionState GetSurfaceStateForSave(OCIChar actor)
        {
            var state = GetSurfaceState(actor);
            var character = actor?.charInfo;
            if (state == null || !character)
                return state;
            var valid = new List<string>();
            if (character.objAccessory != null)
                for (var slot = 0; slot < character.objAccessory.Length; slot++)
                    if (TryGetAccessory(character, slot, out _, out var identity))
                        valid.Add(SurfaceSlotKey(false, slot, identity));
            if (character.objHair != null)
                for (var slot = 0; slot < character.objHair.Length; slot++)
                    if (RegularHairJuiceParts.TryGet(character, slot, out _, out var identity))
                        valid.Add(RegularHairSurfaceSlotKey(slot, identity));
            for (var slot = 0; slot < ClothesJuiceParts.SlotCount; slot++)
                if (ClothesJuiceParts.TryGet(character, slot, out _, out var identity))
                    valid.Add(SurfaceSlotKey(true, slot, identity));
            var obsolete = new List<string>();
            foreach (var key in state.References.Keys)
                if (!valid.Exists(prefix => key.StartsWith(prefix, StringComparison.Ordinal)))
                    obsolete.Add(key);
            foreach (var key in obsolete)
                state.References.Remove(key);
            // Saving commits the last valid displayed mapping, including initial warmup.
            // Do not capture the current pose again or repaint after a manual edit.
            foreach (var reference in state.References.Values)
                if (reference != null)
                    reference.CaptureUntil = -1;
            return state;
        }

        internal void SetSurfaceState(OCIChar actor, SurfaceProjectionState state)
        {
            ReleaseSurfaceMeshes(actor);
            if (state == null)
                _surfaceStates.Remove(actor);
            else
                _surfaceStates[actor] = state;
        }

        internal void CopySurfaceState(OCIChar original, OCIChar copy)
        {
            var state = GetSurfaceState(original);
            SetSurfaceState(copy, state?.Copy());
        }

        private static string SurfaceSlotKey(bool clothes, int slot, string identity) =>
            (clothes ? "clothes/" : "accessory/") + slot + "/" + identity + "\n";

        private static string RegularHairSurfaceSlotKey(int slot, string identity) =>
            "hair/" + slot + "/" + identity + "\n";

        private static string SurfaceRendererKey(string slotKey, Transform root, Renderer renderer, Mesh mesh)
        {
            var path = new StringBuilder();
            for (var current = renderer.transform; current && current != root; current = current.parent)
                path.Insert(0, current.name + "[" + current.GetSiblingIndex() + "]/");
            var siblings = renderer.GetComponents<Renderer>();
            return slotKey + path + renderer.GetType().Name + "/" + Array.IndexOf(siblings, renderer) + "/" +
                mesh.name + "/" + mesh.vertexCount + "/" + mesh.subMeshCount;
        }

        private bool TryGetSurfaceMesh(OCIChar actor, Renderer renderer, Transform root, Transform anchor,
            string slotKey, int frame, bool updateSnapshot, out SurfaceMesh surface)
        {
            surface = null;
            var source = GetMesh(renderer);
            var scale = renderer.transform.lossyScale;
            if (!source || source.vertexCount == 0 || Mathf.Abs(scale.x) < .000001f || Mathf.Abs(scale.y) < .000001f || Mathf.Abs(scale.z) < .000001f ||
                (_surfaceFailures.TryGetValue(renderer, out var failure) &&
                failure.Source == source && frame < failure.RetryFrame))
                return false;
            // Unity 5.6 BakeMesh already applies scale and parent shear to its output vertices.
            // Its coordinate frame contains translation and rotation only; applying localToWorld again double-scales them.
            var drawMatrix = renderer is SkinnedMeshRenderer
                ? Matrix4x4.TRS(renderer.transform.position, renderer.transform.rotation, Vector3.one)
                : renderer.localToWorldMatrix;
            if (_surfaceMeshes.TryGetValue(renderer, out surface) &&
                (surface.Source != source || surface.Actor != actor || surface.SlotKey != slotKey ||
                    surface.Reference.VertexCount != source.vertexCount))
            {
                DestroySurfaceMesh(surface);
                _surfaceMeshes.Remove(renderer);
                surface = null;
            }

            try
            {
                if (surface == null)
                {
                    var key = SurfaceRendererKey(slotKey, root, renderer, source);
                    surface = new SurfaceMesh { Actor = actor, Source = source, Key = key, SlotKey = slotKey, Renderer = renderer, SlotRoot = root };
                    var skinned = renderer as SkinnedMeshRenderer;
                    if (skinned)
                    {
                        surface.Snapshot = new Mesh { name = "BodyCum surface snapshot", hideFlags = HideFlags.HideAndDontSave };
                        surface.Snapshot.MarkDynamic();
                        skinned.BakeMesh(surface.Snapshot);
                        if (surface.Snapshot.vertexCount != source.vertexCount)
                            throw new InvalidOperationException("The skinned surface geometry is not ready.");
                        surface.BakeFrame = frame;
                        surface.BakeCount++;
                        surface.DrawMatrix = drawMatrix;
                        surface.Pose = SurfacePoseStamp.Capture(skinned);
                    }
                    var state = GetSurfaceState(actor);
                    if (state == null)
                        _surfaceStates.Add(actor, state = new SurfaceProjectionState());
                    PruneChangedSurfaceSlot(state, slotKey);
                    var topology = SurfaceTopology(surface.Snapshot ? surface.Snapshot : source);
                    state.References.TryGetValue(key, out var reference);
                    if (reference == null || reference.VertexCount != source.vertexCount || reference.TopologyHash != topology ||
                        reference.MeshName != source.name || (skinned && (reference.Coordinates == null || reference.Coordinates.Count != source.vertexCount)))
                    {
                        reference = new SurfaceProjectionReference
                        {
                            VertexCount = source.vertexCount, TopologyHash = topology, MeshName = source.name,
                            CaptureUntil = frame + PlacementWarmupFrames,
                            MeshToAnchor = anchor.worldToLocalMatrix * drawMatrix,
                            Minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
                            Maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity)
                        };
                        var minimum = reference.Minimum;
                        var maximum = reference.Maximum;
                        IncludeBounds(renderer.bounds, anchor.worldToLocalMatrix, ref minimum, ref maximum);
                        reference.Minimum = minimum;
                        reference.Maximum = maximum;
                        if (skinned)
                        {
                            var vertices = surface.Snapshot.vertices;
                            reference.Coordinates = new List<Vector3>(vertices.Length);
                            foreach (var vertex in vertices)
                                reference.Coordinates.Add(reference.MeshToAnchor.MultiplyPoint3x4(vertex));
                        }
                        state.References[key] = reference;
                    }
                    surface.Reference = reference;
                    surface.DrawMatrix = drawMatrix;
                    surface.Coordinates = reference.Coordinates;
                    if (surface.Snapshot)
                        surface.Snapshot.SetUVs(1, surface.Coordinates);
                    _surfaceMeshes.Add(renderer, surface);
                }
                var settling = surface.Reference.CaptureUntil >= frame;
                if ((updateSnapshot || settling) && surface.Snapshot &&
                    (surface.BakeFrame != frame || surface.Pose == null || !surface.Pose.Matches((SkinnedMeshRenderer)renderer)))
                {
                    ((SkinnedMeshRenderer)renderer).BakeMesh(surface.Snapshot);
                    if (surface.Snapshot.vertexCount != source.vertexCount)
                        throw new InvalidOperationException("The skinned surface geometry is not ready.");
                    // BakeMesh also copies the source UV channels. Restore only our private snapshot channel.
                    if (surface.StrandCoordinates != null)
                        surface.Snapshot.SetUVs(1, surface.StrandCoordinates);
                    else
                        surface.Snapshot.SetUVs(1, surface.Coordinates);
                    surface.BakeFrame = frame;
                    surface.BakeCount++;
                    surface.DrawMatrix = drawMatrix;
                    if (surface.Pose == null)
                        surface.Pose = SurfacePoseStamp.Capture((SkinnedMeshRenderer)renderer);
                    else
                        surface.Pose.Refresh((SkinnedMeshRenderer)renderer);
                }
                if (settling)
                {
                    var reference = surface.Reference;
                    reference.MeshToAnchor = anchor.worldToLocalMatrix * (surface.Snapshot ? surface.DrawMatrix : drawMatrix);
                    var minimum = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
                    var maximum = new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
                    IncludeBounds(renderer.bounds, anchor.worldToLocalMatrix, ref minimum, ref maximum);
                    reference.Minimum = minimum;
                    reference.Maximum = maximum;
                    if (surface.Snapshot)
                    {
                        var vertices = surface.Snapshot.vertices;
                        surface.Coordinates.Clear();
                        foreach (var vertex in vertices)
                            surface.Coordinates.Add(reference.MeshToAnchor.MultiplyPoint3x4(vertex));
                        surface.Snapshot.SetUVs(1, surface.Coordinates);
                    }
                }
                surface.LastFrame = frame;
                _surfaceFailures.Remove(renderer);
                return true;
            }
            catch (Exception error)
            {
                if (!_surfaceFailures.TryGetValue(renderer, out var previous) || previous.Source != source)
                    _log.LogWarning("Could not attach the surface effect to " + renderer.name + ": " + error.Message);
                _surfaceFailures[renderer] = new SurfaceFailure { Actor = actor, Source = source, RetryFrame = frame + 120 };
                if (surface != null)
                    DestroySurfaceMesh(surface);
                _surfaceMeshes.Remove(renderer);
                surface = null;
                return false;
            }
        }

        private static void PruneChangedSurfaceSlot(SurfaceProjectionState state, string slotKey)
        {
            var slash = slotKey.IndexOf('/', slotKey.IndexOf('/') + 1);
            var prefix = slotKey.Substring(0, slash + 1);
            var obsolete = new List<string>();
            foreach (var key in state.References.Keys)
                if (key.StartsWith(prefix, StringComparison.Ordinal) && !key.StartsWith(slotKey, StringComparison.Ordinal))
                    obsolete.Add(key);
            foreach (var key in obsolete)
                state.References.Remove(key);
        }

        private bool PrepareSurfaceSlot(OCIChar actor, GameObject root, Transform anchor, string slotKey, int frame)
        {
            // Capture all parts together. A later camera angle or hidden hair card must not choose a new pose.
            var ready = true;
            root.GetComponentsInChildren(true, _renderers);
            foreach (var renderer in _renderers)
                if (renderer && BelongsToSlot(renderer.transform, root.transform) && GetMesh(renderer) &&
                    TryGetSurfaceMesh(actor, renderer, root.transform, anchor, slotKey, frame, false, out var surface))
                {
                    if (surface.Reference.CaptureUntil >= frame)
                        ready = false;
                    else
                        _strandParts.Add(surface);
                }
            _renderers.Clear();
            return ready;
        }

        // Visibility must not repaint the other hair slots. Keep only placements that have already
        // settled while this slot was visible. Never bake or initialize a hidden skinned mesh here.
        private void CollectHiddenStrandParts(OCIChar actor, GameObject root, string slotKey, int frame)
        {
            root.GetComponentsInChildren(true, _renderers);
            foreach (var renderer in _renderers)
            {
                if (!renderer || !_surfaceMeshes.TryGetValue(renderer, out var surface) ||
                    surface.Actor != actor || surface.SlotKey != slotKey || surface.Source != GetMesh(renderer) ||
                    surface.StrandGroup == null || surface.Reference.CaptureUntil >= frame)
                    continue;
                surface.LastFrame = frame;
                _strandParts.Add(surface);
            }
            _renderers.Clear();
        }

        private static int SurfaceTopology(Mesh mesh)
        {
            unchecked
            {
                var hash = (mesh.vertexCount * 397) ^ mesh.subMeshCount;
                if (mesh.isReadable)
                    for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                        foreach (var index in mesh.GetIndices(submesh))
                            hash = (hash ^ index) * 16777619;
                return hash;
            }
        }

        private static Matrix4x4 SurfaceDrawProjection(SurfaceMesh surface, Renderer renderer, Matrix4x4 anchorProjection) =>
            surface.Snapshot ? anchorProjection : anchorProjection * surface.Reference.MeshToAnchor * renderer.worldToLocalMatrix;

        private static void DestroySurfaceMesh(SurfaceMesh surface)
        {
            if (surface.Snapshot)
                UnityEngine.Object.Destroy(surface.Snapshot);
            if (surface.StrandRigidCopy)
                UnityEngine.Object.Destroy(surface.StrandRigidCopy);
            surface.StrandGroup = null;
        }

        private void ReleaseSurfaceMeshes(OCIChar actor = null)
        {
            // Camera buffers can outlive the last render of a camera. Remove their mesh references
            // before scene replacement or global OFF retires the snapshots.
            foreach (var buffer in _buffers.Values)
                buffer.Clear();
            _expiredRenderers.Clear();
            foreach (var entry in _surfaceMeshes)
                if (actor == null || entry.Value.Actor == actor)
                {
                    DestroySurfaceMesh(entry.Value);
                    _expiredRenderers.Add(entry.Key);
                }
            foreach (var renderer in _expiredRenderers)
            {
                _surfaceMeshes.Remove(renderer);
                _surfaceFailures.Remove(renderer);
            }
            _expiredRenderers.Clear();
            if (actor == null)
                _surfaceFailures.Clear();
            else
            {
                foreach (var entry in _surfaceFailures)
                    if (entry.Value.Actor == actor)
                        _expiredRenderers.Add(entry.Key);
                foreach (var renderer in _expiredRenderers)
                    _surfaceFailures.Remove(renderer);
                _expiredRenderers.Clear();
            }
        }

        private void CleanupSurfaceMeshes(int frame)
        {
            _expiredRenderers.Clear();
            foreach (var entry in _surfaceFailures)
                if (!entry.Key)
                    _expiredRenderers.Add(entry.Key);
            foreach (var renderer in _expiredRenderers)
                _surfaceFailures.Remove(renderer);
            _expiredRenderers.Clear();
            foreach (var entry in _surfaceMeshes)
                if (!entry.Key || frame - entry.Value.LastFrame > 120)
                {
                    DestroySurfaceMesh(entry.Value);
                    _expiredRenderers.Add(entry.Key);
                }
            foreach (var renderer in _expiredRenderers)
            {
                _surfaceMeshes.Remove(renderer);
                _surfaceFailures.Remove(renderer);
            }
            _expiredRenderers.Clear();
            // Reference coordinates outlive transient draw meshes, so OFF/ON cannot re-project in a new pose.
        }
    }
}
