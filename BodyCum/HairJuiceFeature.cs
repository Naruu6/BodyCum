using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Logging;
using Studio;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private const CameraEvent DrawEvent = CameraEvent.AfterForwardAlpha;
        private const string ShaderResource = "BodyCum.Resources.CustomJuiceColor.Hair.bundle";
        private const string ShaderAsset = "assets/skineffects/hair_juice.shader";
        private const string ShaderName = "SkinEffects/Hair Juice Projection";
        private const string MainTexture = "_HairSourceTex";
        private const string AlphaTexture = "_HairAlphaTex";
        private const string PatternTexture = "_HairPatternTex";
        private const string BodyLiquidTexture = "_Texture2";
        private const float HairCoverageHeightMultiplier = 2f;
        private const float AccessoryCoverageScale = 2.82842712474f;
        private const float ProjectionOffsetX = .0018f;
        private const float ProjectionOffsetY = .0025f;
        private static readonly int ProjectionX = Shader.PropertyToID("_HairProjectionX");
        private static readonly int ProjectionY = Shader.PropertyToID("_HairProjectionY");
        private static readonly int ProjectionZ = Shader.PropertyToID("_HairProjectionZ");
        private static readonly int ProjectionDimensions = Shader.PropertyToID("_HairProjectionDimensions");
        private static readonly int ProjectionOffsets = Shader.PropertyToID("_HairProjectionOffsets");
        private static readonly int JuiceColor = Shader.PropertyToID("_HairJuiceColor");
        private static readonly int JuiceParameters = Shader.PropertyToID("_HairJuiceParams");
        private static readonly int AlphaParameters = Shader.PropertyToID("_HairAlphaParams");

        private readonly CustomJuiceColor _colors;
        private readonly ManualLogSource _log;
        private readonly Dictionary<OCIChar, HairJuiceProfile> _profiles = new Dictionary<OCIChar, HairJuiceProfile>();
        private readonly Dictionary<OCIChar, HairJuiceProfile> _regularHairProfiles = new Dictionary<OCIChar, HairJuiceProfile>();
        private readonly Dictionary<Camera, CommandBuffer> _buffers = new Dictionary<Camera, CommandBuffer>();
        private readonly Dictionary<Renderer, HairMaterials> _layers = new Dictionary<Renderer, HairMaterials>();
        private readonly HashSet<Transform> _accessoryRoots = new HashSet<Transform>();
        private readonly List<AccessorySelection> _selectedAccessories = new List<AccessorySelection>();
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Renderer> _expiredRenderers = new List<Renderer>();
        private readonly List<Camera> _expiredCameras = new List<Camera>();
        private Shader _shader;
        private bool _attemptedLoad;
        private bool _subscribed;
        private int _nextCleanup;
        private Plane[] _surfaceFrustum;

        private sealed class HairMaterials
        {
            internal readonly Dictionary<int, Material> Materials = new Dictionary<int, Material>();
            internal int LastFrame;
        }

        private struct BodyPattern
        {
            internal Texture Texture;
            internal Vector2 Scale;
            internal Vector2 Offset;
        }

        private struct AccessorySelection
        {
            internal GameObject Root;
            internal string SlotKey;
        }

        internal HairJuiceFeature(CustomJuiceColor colors, ManualLogSource log)
        {
            _colors = colors;
            _log = log;
        }

        internal HairJuiceProfile Get(OCIChar character) => GetHairProfile(character, false);
        internal HairJuiceProfile Edit(OCIChar character) => EditHairProfile(character, false);
        internal void Set(OCIChar character, HairJuiceProfile profile) => _profiles[character] = profile;
        internal void SetRegularHair(OCIChar character, HairJuiceProfile profile) => _regularHairProfiles[character] = profile;

        internal HairJuiceProfile GetHairProfile(OCIChar character, bool regularHair)
        {
            var profiles = regularHair ? _regularHairProfiles : _profiles;
            return character != null && profiles.TryGetValue(character, out var profile) ? profile : null;
        }

        internal HairJuiceProfile EditHairProfile(OCIChar character, bool regularHair)
        {
            var existing = GetHairProfile(character, regularHair);
            if (existing != null) return existing;
            var profile = new HairJuiceProfile();
            (regularHair ? _regularHairProfiles : _profiles).Add(character, profile);
            return profile;
        }

        internal static bool TryGetHairSlot(ChaControl character, int slot, bool regularHair, out GameObject root, out string identity) =>
            regularHair ? RegularHairJuiceParts.TryGet(character, slot, out root, out identity) :
                TryGetAccessory(character, slot, out root, out identity);

        internal void Remove(OCIChar character)
        {
            _profiles.Remove(character);
            _regularHairProfiles.Remove(character);
            _clothesProfiles.Remove(character);
            ReleaseSurfaceMeshes(character);
            _surfaceStates.Remove(character);
        }

        internal void Clear()
        {
            _profiles.Clear();
            _regularHairProfiles.Clear();
            _clothesProfiles.Clear();
            _surfaceStates.Clear();
            ReleaseDrawResources();
        }

        internal void Enable()
        {
            if (_subscribed)
                return;
            // Prime native skinned geometry before visibility/culling, then verify the final pose
            // again immediately before drawing. The pose stamp reuses identical work between hooks.
            Camera.onPreCull += OnCamera;
            Camera.onPreRender += OnCamera;
            Camera.onPreRender += BeginMaterialOverrides;
            Camera.onPostRender += EndMaterialOverrides;
            _subscribed = true;
        }

        internal void Disable()
        {
            if (_subscribed)
            Camera.onPreCull -= OnCamera;
            Camera.onPreRender -= OnCamera;
            Camera.onPreRender -= BeginMaterialOverrides;
            Camera.onPostRender -= EndMaterialOverrides;
            ReleaseNativeLayerResources();
            _subscribed = false;
            ReleaseDrawResources();
            ReleaseDirectionMask();
        }

        internal static bool TryGetAccessory(ChaControl character, int slot, out GameObject root, out string identity)
        {
            root = null;
            identity = null;
            var objects = character ? character.objAccessory : null;
            if (objects == null || slot < 0 || slot >= objects.Length || !objects[slot])
                return false;

            root = objects[slot];
            var info = GetAccessoryInfo(character, slot, root);
            var bundle = info?.GetInfo(ChaListDefine.KeyType.MainAB);
            var asset = info?.GetInfo(ChaListDefine.KeyType.MainData);
            if (!string.IsNullOrEmpty(bundle) && !string.IsNullOrEmpty(asset))
            {
                identity = bundle + "\n" + asset;
                return true;
            }

            var parts = character.nowCoordinate?.accessory?.parts;
            if (parts == null || slot >= parts.Length || parts[slot] == null)
                return false;
            identity = parts[slot].type.ToString(CultureInfo.InvariantCulture) + "/" +
                parts[slot].id.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        internal static string GetAccessoryLabel(ChaControl character, int slot, GameObject root)
        {
            var name = GetAccessoryInfo(character, slot, root)?.GetInfo(ChaListDefine.KeyType.Name);
            return (slot + 1).ToString(CultureInfo.InvariantCulture) + "  " + (string.IsNullOrEmpty(name) ? root.name : name);
        }

        private static ListInfoBase GetAccessoryInfo(ChaControl character, int slot, GameObject root)
        {
            var info = character.infoAccessory;
            return info != null && slot < info.Length && info[slot] != null ? info[slot] : root.GetComponent<ListInfoComponent>()?.data;
        }

        private void OnCamera(Camera camera)
        {
            if (!camera || camera.cameraType != CameraType.Game)
                return;

            var frame = Time.frameCount;
            if (frame >= _nextCleanup)
                Cleanup(frame);

            CommandBuffer buffer;
            _buffers.TryGetValue(camera, out buffer);
            buffer?.Clear();
            _surfaceFrustum = GeometryUtility.CalculateFrustumPlanes(camera);
            var hasDraw = RecordSkin(camera, ref buffer, frame);
            hasDraw |= RecordHairSlots(camera, ref buffer, frame, _profiles, false);
            hasDraw |= RecordHairSlots(camera, ref buffer, frame, _regularHairProfiles, true);
            hasDraw |= RecordClothes(camera, ref buffer, frame);
            if (buffer != null && !hasDraw)
            {
                camera.RemoveCommandBuffer(DrawEvent, buffer);
                buffer.Release();
                _buffers.Remove(camera);
            }
        }

        private bool RecordHairSlots(Camera camera, ref CommandBuffer buffer, int frame,
            Dictionary<OCIChar, HairJuiceProfile> profiles, bool regularHair)
        {
            var hasDraw = false;
            foreach (var entry in profiles)
            {
                var character = entry.Key.charInfo;
                var profile = entry.Value;
                if (!character || !character.isActiveAndEnabled || !BodyCumPlugin.IsCharacterEnabled(entry.Key) ||
                    profile.Level == 0 || profile.Slots.Count == 0)
                    continue;
                var settings = _colors.GetSettings(entry.Key);
                if (settings.Opacity == 0)
                    continue;

                var head = character.GetReferenceInfo(ChaReference.RefObjKey.a_n_head);
                if (!head)
                    continue;

                if (!TryGetBodyPattern(character, out var pattern))
                    continue;

                // Extend the original texture coordinates downwards without stretching the drops.
                pattern.Offset.y -= pattern.Scale.y * (HairCoverageHeightMultiplier - 1f);
                pattern.Scale.y *= HairCoverageHeightMultiplier;
                // Expand around the existing projection center while keeping texture units per metre.
                var originalScale = pattern.Scale;
                pattern.Scale *= AccessoryCoverageScale;
                pattern.Offset += (originalScale - pattern.Scale) * .5f;
                var projection = GetLocalProjection(profile);
                var color = settings.ToShaderColor(QualitySettings.activeColorSpace == ColorSpace.Linear);
                _accessoryRoots.Clear();
                AddSlotRoots(character.objAccessory);
                if (regularHair)
                    AddSlotRoots(character.objHair);
                _strandParts.Clear();
                _selectedAccessories.Clear();
                var placementReady = true;
                foreach (var slot in profile.Slots)
                {
                    GameObject root;
                    string identity;
                    if (!TryGetHairSlot(character, slot.Key, regularHair, out root, out identity) || identity != slot.Value)
                        continue;
                    var slotKey = regularHair ? RegularHairSurfaceSlotKey(slot.Key, identity) : SurfaceSlotKey(false, slot.Key, identity);
                    if (!root.activeInHierarchy)
                    {
                        CollectHiddenStrandParts(entry.Key, root, slotKey, frame);
                        continue;
                    }
                    if (!EnsureShader())
                        break;
                    placementReady &= PrepareSurfaceSlot(entry.Key, root, head.transform, slotKey, frame);
                    _selectedAccessories.Add(new AccessorySelection { Root = root, SlotKey = slotKey });
                }
                // Build overlapping strand offsets together once the new surfaces settle.
                // Existing surfaces keep drawing their last completed layout while they wait.
                if (placementReady)
                    PrepareStrandLayout();
                foreach (var selection in _selectedAccessories)
                {
                    buffer = buffer ?? CreateDrawBuffer(camera);
                    hasDraw |= RecordSlot(buffer, camera, entry.Key, selection.Root, head.transform,
                        selection.SlotKey, projection, pattern, color, profile.Level, settings.Density / 100f, frame);
                }
                _strandParts.Clear();
                _selectedAccessories.Clear();
            }
            return hasDraw;
        }

        private CommandBuffer CreateDrawBuffer(Camera camera)
        {
            var buffer = new CommandBuffer { name = "SkinEffects Juices Projection" };
            _buffers.Add(camera, buffer);
            camera.AddCommandBuffer(DrawEvent, buffer);
            return buffer;
        }

        private void AddSlotRoots(GameObject[] roots)
        {
            if (roots == null)
                return;
            foreach (var root in roots)
                if (root)
                    _accessoryRoots.Add(root.transform);
        }

        private bool TryGetBodyPattern(ChaControl character, out BodyPattern pattern)
        {
            var body = character.rendBody;
            return TryGetBodyPattern(body ? body.sharedMaterial : null, out pattern) ||
                   TryGetBodyPattern(character.customMatBody, out pattern) ||
                   TryGetBodyPattern(_colors.GetStockSkin(character, false), out pattern);
        }

        private static bool TryGetBodyPattern(Material source, out BodyPattern pattern)
        {
            pattern = default;
            if (!source || !source.HasProperty(BodyLiquidTexture))
                return false;

            var texture = source.GetTexture(BodyLiquidTexture);
            if (!texture || texture.dimension != TextureDimension.Tex2D)
                return false;

            pattern = new BodyPattern
            {
                Texture = texture,
                Scale = source.GetTextureScale(BodyLiquidTexture),
                Offset = source.GetTextureOffset(BodyLiquidTexture)
            };
            return true;
        }

        internal static Matrix4x4 GetProjection(Transform head, HairJuiceProfile profile)
        {
            return GetLocalProjection(profile) * head.worldToLocalMatrix;
        }

        private static Matrix4x4 GetLocalProjection(HairJuiceProfile profile)
        {
            var worldToHead = Matrix4x4.identity;
            var size = profile.Size / 100f;
            var originalHeight = .32f * size;
            var height = originalHeight * HairCoverageHeightMultiplier;
            var center = new Vector3(profile.Horizontal * ProjectionOffsetX, .015f + profile.Vertical * ProjectionOffsetY, -.10f);
            center.y -= (height - originalHeight) * .5f;
            var x = worldToHead.GetRow(0);
            var y = worldToHead.GetRow(1);
            var z = worldToHead.GetRow(2);
            x.w -= center.x;
            y.w -= center.y;
            z.w -= center.z;
            worldToHead.SetRow(0, x / (originalHeight * AccessoryCoverageScale));
            worldToHead.SetRow(1, y / (height * AccessoryCoverageScale));
            worldToHead.SetRow(2, z / (.42f * size * AccessoryCoverageScale));
            return worldToHead;
        }

        private bool RecordSlot(CommandBuffer buffer, Camera camera, OCIChar actor, GameObject root, Transform anchor,
            string slotKey, Matrix4x4 projection,
            BodyPattern pattern, Vector4 color, int level, float density, int frame,
            Func<Material, bool> includeMaterial = null, bool underwear = false)
        {
            root.GetComponentsInChildren(false, _renderers);
            var drawn = false;
            var hairSurface = slotKey.StartsWith("accessory/", StringComparison.Ordinal) ||
                slotKey.StartsWith("hair/", StringComparison.Ordinal);
            var adaptiveHair = hairSurface;
            var dimensions = Vector4.one;
            if (adaptiveHair)
            {
                var x = Mathf.Abs(projection.m00);
                var y = Mathf.Abs(projection.m11);
                var z = Mathf.Abs(projection.m22);
                adaptiveHair = x > .000001f && y > .000001f && z > .000001f &&
                    !float.IsInfinity(x) && !float.IsInfinity(y) && !float.IsInfinity(z);
                if (adaptiveHair) dimensions = new Vector4(1f / x, 1f / y, 1f / z, .15f);
            }
            var directionMask = adaptiveHair && pattern.Texture.width == 2048 && pattern.Texture.height == 2048
                && pattern.Texture.wrapMode == TextureWrapMode.Repeat ? GetDirectionMask() : null;
            foreach (var renderer in _renderers)
            {
                if (!renderer || !renderer.enabled || (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    !BelongsToSlot(renderer.transform, root.transform) ||
                    !GeometryUtility.TestPlanesAABB(_surfaceFrustum, renderer.bounds))
                    continue;
                var mesh = GetMesh(renderer);
                if (!mesh)
                    continue;
                var sources = renderer.sharedMaterials;
                if (!Array.Exists(sources, source => source && source.shader && (includeMaterial == null || includeMaterial(source))) ||
                    !TryGetSurfaceMesh(actor, renderer, root.transform, anchor, slotKey, frame, true, out var surface))
                    continue;
                var pendingPlacement = surface.Reference.CaptureUntil >= frame;
                if (hairSurface && (pendingPlacement || surface.StrandGroup == null))
                    continue;
                if (adaptiveHair && surface.StrandGroup != null && !SameStrandShaders(surface, sources))
                    surface.StrandGroup = null;
                var hasStrandOffsets = adaptiveHair && !pendingPlacement && surface.StrandCoordinates != null &&
                    pattern.Texture.wrapMode == TextureWrapMode.Repeat;
                var drawProjection = pendingPlacement
                    ? projection * anchor.worldToLocalMatrix
                    : SurfaceDrawProjection(surface, renderer, projection);
                for (var index = 0; index < Math.Min(mesh.subMeshCount, sources.Length); index++)
                {
                    var source = sources[index];
                    if (!source || !source.shader || (includeMaterial != null && !includeMaterial(source)))
                        continue;
                    var material = GetDrawMaterial(_layers, _shader, renderer, index, frame);
                    material.SetVector(ProjectionX, drawProjection.GetRow(0));
                    material.SetVector(ProjectionY, drawProjection.GetRow(1));
                    material.SetVector(ProjectionZ, drawProjection.GetRow(2));
                    material.SetTexture(PatternTexture, pattern.Texture);
                    material.SetTextureScale(PatternTexture, pattern.Scale);
                    material.SetTextureOffset(PatternTexture, pattern.Offset);
                    SetSource(material, source, color, level, density, underwear);
                    var parameters = material.GetVector(JuiceParameters);
                    if (adaptiveHair)
                    {
                        material.SetVector(ProjectionDimensions, dimensions);
                        material.SetVector(ProjectionOffsets, new Vector4(projection.m03, projection.m13, 0f,
                            hasStrandOffsets ? 1f : 0f));
                        parameters.z += 4f;
                        if (directionMask)
                        {
                            material.SetTexture(HairDirectionMask, directionMask);
                            parameters.z += 8f;
                        }
                    }
                    if (surface.Snapshot && !pendingPlacement) parameters.z += 2f;
                    material.SetVector(JuiceParameters, parameters);
                    if (surface.Snapshot)
                    {
                        buffer.DrawMesh(surface.Snapshot, surface.DrawMatrix, material, index, 0);
                    }
                    else if (hasStrandOffsets && surface.StrandRigidCopy)
                        buffer.DrawMesh(surface.StrandRigidCopy, renderer.localToWorldMatrix, material, index, 0);
                    else
                        buffer.DrawRenderer(renderer, material, index, 0);
                    drawn = true;
                }
            }
            _renderers.Clear();
            return drawn;
        }

        private bool BelongsToSlot(Transform renderer, Transform root)
        {
            for (var current = renderer; current && current != root; current = current.parent)
                if (_accessoryRoots.Contains(current))
                    return false;
            return true;
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            var skinned = renderer as SkinnedMeshRenderer;
            if (skinned)
                return skinned.sharedMesh;
            return renderer is MeshRenderer ? renderer.GetComponent<MeshFilter>()?.sharedMesh : null;
        }

        private static Material GetDrawMaterial(Dictionary<Renderer, HairMaterials> layers, Shader shader, Renderer renderer, int index, int frame)
        {
            HairMaterials shaders;
            if (!layers.TryGetValue(renderer, out shaders))
            {
                shaders = new HairMaterials();
                layers.Add(renderer, shaders);
            }
            shaders.LastFrame = frame;
            Material material;
            if (!shaders.Materials.TryGetValue(index, out material))
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                shaders.Materials.Add(index, material);
            }
            return material;
        }

        private static void SetSource(Material material, Material source, Vector4 color, int level, float density, bool underwear = false)
        {
            var baseName = source.HasProperty("_MainTex") ? "_MainTex" : "_BaseMap";
            var baseTexture = CopyTexture(material, MainTexture, source, baseName);
            var alphaTexture = CopyTexture(material, AlphaTexture, source, "_AlphaMask");
            var tag = source.GetTag("RenderType", false, "");
            var cutout = tag == "TransparentCutout" || source.renderQueue < (int)RenderQueue.Transparent;
            var cutoff = cutout && source.HasProperty("_Cutoff") ? Mathf.Clamp01(source.GetFloat("_Cutoff")) : 0f;
            // The native front-hair shader draws from texture alpha. _Color.a is not its opacity.
            if (!cutout && source.shader.name != "Shader Forge/main_hair_front" && source.HasProperty("_Color"))
                color.w *= Mathf.Clamp01(source.GetColor("_Color").a);
            material.SetVector(JuiceColor, color);
            material.SetVector(JuiceParameters, new Vector4(level, density, baseTexture ? 1f : 0f, cutoff));
            if (underwear && source.shader.name == "Shader Forge/main_opaque" &&
                source.HasProperty("_alpha_a") && source.HasProperty("_alpha_b"))
            {
                // Native main_opaque uses independently enabled R/G cutout channels.
                material.SetVector(AlphaParameters, new Vector4(source.GetFloat("_alpha_a"), source.GetFloat("_alpha_b"), 0f, 1f));
            }
            else
                material.SetVector(AlphaParameters, alphaTexture ? new Vector4(1f, 0f, 0f, 0f) : Vector4.zero);
        }

        private static Texture CopyTexture(Material destination, string target, Material source, string property)
        {
            var texture = source.HasProperty(property) ? source.GetTexture(property) : null;
            destination.SetTexture(target, texture ? texture : Texture2D.whiteTexture);
            destination.SetTextureScale(target, texture ? source.GetTextureScale(property) : Vector2.one);
            destination.SetTextureOffset(target, texture ? source.GetTextureOffset(property) : Vector2.zero);
            return texture;
        }

        private bool EnsureShader()
        {
            if (_attemptedLoad)
                return _shader;
            _attemptedLoad = true;
            _shader = LoadProjectionShader(ShaderResource, ShaderAsset, ShaderName);
            return _shader;
        }

        private Shader LoadProjectionShader(string resource, string address, string expectedName)
        {
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
            {
                _log.LogWarning("BodyCum projection requires Direct3D 11.");
                return null;
            }

            AssetBundle bundle = null;
            try
            {
                using (var stream = typeof(HairJuiceFeature).Assembly.GetManifestResourceStream(resource))
                {
                    if (stream == null)
                        throw new IOException("The embedded projection shader is missing.");
                    using (var reader = new BinaryReader(stream))
                        bundle = AssetBundle.LoadFromMemory(reader.ReadBytes(checked((int)stream.Length)));
                }
                var shader = bundle ? bundle.LoadAsset<Shader>(address) : null;
                if (!shader || !shader.isSupported || shader.name != expectedName)
                    throw new IOException("Unity could not load the compatible projection shader.");
                return shader;
            }
            catch (Exception error)
            {
                _log.LogWarning(expectedName + " could not load: " + error.Message);
                return null;
            }
            finally
            {
                if (bundle)
                    bundle.Unload(false);
            }
        }

        private void Cleanup(int frame)
        {
            _nextCleanup = frame + 120;
            CleanupLayers(_layers, frame);
            CleanupLayers(_skinLayers, frame);
            CleanupSurfaceMeshes(frame);

            foreach (var entry in _buffers)
                if (!entry.Key)
                    _expiredCameras.Add(entry.Key);
            foreach (var camera in _expiredCameras)
            {
                _buffers[camera].Release();
                _buffers.Remove(camera);
            }
            _expiredCameras.Clear();
        }

        private void CleanupLayers(Dictionary<Renderer, HairMaterials> layers, int frame)
        {
            foreach (var entry in layers)
                if (!entry.Key || frame - entry.Value.LastFrame > 120)
                    _expiredRenderers.Add(entry.Key);
            foreach (var renderer in _expiredRenderers)
            {
                DestroyMaterials(layers[renderer]);
                layers.Remove(renderer);
            }
            _expiredRenderers.Clear();
        }

        private void ReleaseDrawResources()
        {
            ReleaseSurfaceMeshes();
            foreach (var entry in _buffers)
            {
                if (entry.Key)
                    entry.Key.RemoveCommandBuffer(DrawEvent, entry.Value);
                entry.Value.Release();
            }
            _buffers.Clear();
            foreach (var entry in _layers)
                DestroyMaterials(entry.Value);
            _layers.Clear();
            foreach (var entry in _skinLayers)
                DestroyMaterials(entry.Value);
            _skinLayers.Clear();
            _accessoryRoots.Clear();
        }

        private static void DestroyMaterials(HairMaterials shaders)
        {
            foreach (var material in shaders.Materials.Values)
                UnityEngine.Object.Destroy(material);
        }
    }
}
