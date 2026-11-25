using System;
using UnityEngine;

namespace BodyCum
{
    // A frame can contain more than one pose when a plugin renders an early camera.
    // Capture this immediately after BakeMesh, together with the snapshot's draw matrix.
    internal sealed class SurfacePoseStamp
    {
        private struct BonePose
        {
            internal Transform Bone;
            internal bool Alive;
            internal Matrix4x4 Matrix;
        }

        private SkinnedMeshRenderer _renderer;
        private Mesh _mesh;
        private bool _meshAlive;
        private int _vertexCount;
        private int _subMeshCount;
        private int _blendShapeCount;
        private Matrix4x4 _rendererMatrix;
        private Transform _rootBone;
        private bool _rootAlive;
        private Matrix4x4 _rootMatrix;
        private BonePose[] _bones;
        private float[] _blendShapes;
        private SkinQuality _quality;
        private BlendWeights _blendWeights;

        private SurfacePoseStamp() { }

        internal void Refresh(SkinnedMeshRenderer renderer)
        {
            if (!renderer)
                throw new ArgumentNullException("renderer");
            _renderer = renderer;
            _mesh = renderer.sharedMesh;
            _meshAlive = _mesh;
            _vertexCount = _meshAlive ? _mesh.vertexCount : 0;
            _subMeshCount = _meshAlive ? _mesh.subMeshCount : 0;
            _blendShapeCount = _meshAlive ? _mesh.blendShapeCount : 0;
            _rendererMatrix = renderer.localToWorldMatrix;
            _rootBone = renderer.rootBone;
            _rootAlive = _rootBone;
            _rootMatrix = _rootAlive ? _rootBone.localToWorldMatrix : Matrix4x4.identity;
            _quality = renderer.quality;
            _blendWeights = QualitySettings.blendWeights;

            var bones = renderer.bones;
            var boneCount = bones == null ? 0 : bones.Length;
            if (_bones == null || _bones.Length != boneCount)
                _bones = new BonePose[boneCount];
            for (var index = 0; index < _bones.Length; index++)
            {
                var bone = bones[index];
                _bones[index] = new BonePose
                {
                    Bone = bone,
                    Alive = bone,
                    Matrix = bone ? bone.localToWorldMatrix : Matrix4x4.identity
                };
            }
            if (_blendShapes == null || _blendShapes.Length != _blendShapeCount)
                _blendShapes = new float[_blendShapeCount];
            for (var index = 0; index < _blendShapes.Length; index++)
                _blendShapes[index] = renderer.GetBlendShapeWeight(index);
        }

        internal static SurfacePoseStamp Capture(SkinnedMeshRenderer renderer)
        {
            var stamp = new SurfacePoseStamp();
            stamp.Refresh(renderer);
            return stamp;
        }

        internal bool Matches(SkinnedMeshRenderer renderer)
        {
            if (!renderer || !ReferenceEquals(renderer, _renderer) || renderer.quality != _quality ||
                QualitySettings.blendWeights != _blendWeights || !SameMatrix(renderer.localToWorldMatrix, _rendererMatrix))
                return false;

            var mesh = renderer.sharedMesh;
            if (!ReferenceEquals(mesh, _mesh) || (bool)mesh != _meshAlive ||
                (mesh && (mesh.vertexCount != _vertexCount || mesh.subMeshCount != _subMeshCount || mesh.blendShapeCount != _blendShapeCount)))
                return false;

            var root = renderer.rootBone;
            if (!ReferenceEquals(root, _rootBone) || (bool)root != _rootAlive ||
                (root && !SameMatrix(root.localToWorldMatrix, _rootMatrix)))
                return false;

            for (var index = 0; index < _blendShapes.Length; index++)
                if (renderer.GetBlendShapeWeight(index) != _blendShapes[index])
                    return false;

            // Unity 5.6 exposes a copying bones getter and no nonallocating GetBones API.
            // Compare each Transform identity, not the getter's temporary array identity.
            var bones = renderer.bones;
            if ((bones == null ? 0 : bones.Length) != _bones.Length)
                return false;
            for (var index = 0; index < _bones.Length; index++)
            {
                var bone = bones[index];
                var saved = _bones[index];
                if (!ReferenceEquals(bone, saved.Bone) || (bool)bone != saved.Alive ||
                    (bone && !SameMatrix(bone.localToWorldMatrix, saved.Matrix)))
                    return false;
            }
            return true;
        }

        private static bool SameMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            // Matrix4x4/Vector equality operators are approximate in Unity.
            // Exact component comparisons also reject NaN poses instead of caching them.
            return a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 && a.m03 == b.m03 &&
                   a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 && a.m13 == b.m13 &&
                   a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22 && a.m23 == b.m23 &&
                   a.m30 == b.m30 && a.m31 == b.m31 && a.m32 == b.m32 && a.m33 == b.m33;
        }
    }
}
