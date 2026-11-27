using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LZ4;
using UnityPlugin;
using Object = UnityPlugin.Object;

// Reads the local Unity 5.6 shader and writes a standalone AssetBundle containing the
// same passes. The optional replacement blob is produced by the DXBC build tool.
internal static class PackSkinShader
{
    private const string JuiceVector = "_SkinEffectsJuiceColor";
    private const string DensityVector = "_SkinEffectsJuiceDensity";
    private const string BustWarpVector = "_BodyCumBustWarp";
    private const long ShaderPathId = 2L;
    private const long LocalFallbackPathId = 3L;
    private const uint Direct3D11 = 4;

    private sealed class ShaderSource
    {
        public readonly string InputName;
        public readonly string CustomName;
        public readonly string BundleName;
        public readonly string AssetAddress;
        public readonly long PathId;
        public readonly string BlobSha256;
        public readonly int LiquidProgramCount;
        public readonly bool HasLocalFallback;

        public ShaderSource(string inputName, string outputName, string fileName, long pathId,
            string blobSha256, int liquidProgramCount, bool hasLocalFallback)
        {
            InputName = inputName;
            CustomName = "SkinEffects/Custom Juice " + outputName;
            BundleName = "skineffects_" + fileName;
            AssetAddress = "assets/skineffects/" + fileName + ".shader";
            PathId = pathId;
            BlobSha256 = blobSha256;
            LiquidProgramCount = liquidProgramCount;
            HasLocalFallback = hasLocalFallback;
        }
    }

    private static ShaderSource FindSource(string shader)
    {
        switch (shader)
        {
            case "main_skin":
                return new ShaderSource("Shader Forge/main_skin", "main_skin", "main_skin", -6692073393702066725L,
                    "D973679D6F677ED51C13D33569B475F869082A4A1D25611B9BBB9F4B91C75E6F", 2, false);
            case "KKUTS":
                return new ShaderSource("KKUTS", "KKUTS", "kkuts", -3972704992416255154L,
                    "0A82D780DC31ECF03DB9FD3405C45539913D51A4421CADD2F9B3CF4FE9E6470B", 17, true);
            case "KKUTS_Tess":
                return new ShaderSource("KKUTS_Tess", "KKUTS_Tess", "kkuts_tess", -3596727507477231712L,
                    "B17B73D6D196A8DAEE2CF253B43CB911AE2B2EB831D6C028B1EB13220698D7D4", 17, true);
            default:
                throw new ArgumentException("Unsupported shader: " + shader);
        }
    }

    public static int Main(string[] args)
    {
        if (args.Length < 3 || args.Length > 5)
        {
            Console.Error.WriteLine("Usage: PackSkinShader.exe <SB3Utility folder> <source bundle> <output> [--extract|--copy|<patched blob>] [main_skin|KKUTS|KKUTS_Tess]");
            return 2;
        }
        var folders = new[] { Path.GetFullPath(args[0]), Path.Combine(Path.GetFullPath(args[0]), "plugins") };
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            var fileName = new AssemblyName(request.Name).Name + ".dll";
            foreach (var directory in folders)
            {
                var file = Path.Combine(directory, fileName);
                if (File.Exists(file))
                    return Assembly.LoadFrom(file);
            }
            return null;
        };
        try
        {
            var spec = FindSource(args.Length == 5 ? args[4] : "main_skin");
            var mode = args.Length >= 4 ? args[3] : "--copy";
            if (mode == "--extract")
                Extract(args[1], args[2], spec);
            else
                BuildBundle(args[1], args[2], mode == "--copy" ? null : mode, spec);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Extract(string input, string output, ShaderSource spec)
    {
        using (var original = new UnityParser(input))
            File.WriteAllBytes(output, ReadStockBlob(LoadStockShader(original.Cabinet, spec), spec));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildBundle(string input, string output, string patchedBlob, ShaderSource spec)
    {
        using (var original = new UnityParser(input))
        using (var standalone = new UnityParser(original))
        {
            var source = original.Cabinet;
            var cabinet = standalone.Cabinet;
            var shader = LoadStockShader(source, spec);
            var originalBlob = ReadStockBlob(shader, spec);
            var fallback = MoveFallback(source, cabinet, shader, spec.HasLocalFallback);

            shader.pathID = ShaderPathId;
            shader.file = cabinet;
            shader.m_ParsedForm.m_Name = spec.CustomName;
            if (patchedBlob != null)
                ApplyShaderPatch(shader, originalBlob, File.ReadAllBytes(patchedBlob), spec);

            var preload = new List<PPtr<Object>> { new PPtr<Object>(shader) };
            if (fallback != null)
                preload.Insert(0, new PPtr<Object>(fallback));

            var bundle = new AssetBundle(cabinet)
            {
                pathID = 1,
                m_Name = spec.BundleName,
                m_AssetBundleName = spec.BundleName,
                m_RuntimeCompatibility = source.Bundle.m_RuntimeCompatibility,
                m_Dependencies = new List<string>(),
                m_PreloadTable = preload,
                m_MainAsset = new AssetInfo(cabinet) { asset = new PPtr<Object>((Component)null) },
                m_Container = new List<KeyValuePair<string, AssetInfo>>
                {
                    new KeyValuePair<string, AssetInfo>(spec.AssetAddress, new AssetInfo(cabinet)
                    {
                        asset = new PPtr<Object>(shader),
                        preloadIndex = 0,
                        preloadSize = preload.Count
                    })
                }
            };
            cabinet.Components.Add(shader);
            if (fallback != null)
                cabinet.Components.Add(fallback);
            cabinet.Types.RemoveAll(type => type.typeId != (int)UnityClassID.Shader && type.typeId != (int)UnityClassID.AssetBundle);
            if (cabinet.Types.Count != 2 || cabinet.Components.Count != preload.Count + 1 || cabinet.Bundle != bundle)
                throw new InvalidDataException("Unexpected components in the shader bundle.");

            using (var buffer = new MemoryStream())
            {
                cabinet.WriteTo(buffer);
                WriteUnityFs(output, original.ExtendedSignature, buffer.ToArray());
            }
            VerifyBundle(output, shader.compressedBlob, patchedBlob != null, spec, fallback);
        }
    }

    private static Shader MoveFallback(AssetCabinet source, AssetCabinet cabinet, Shader shader, bool local)
    {
        var dependency = shader.m_Dependencies.Single();
        cabinet.References.Clear();
        if (local)
        {
            if (dependency.m_FileID != 0)
                throw new InvalidDataException("Expected a local KKUTS fallback dependency.");
            Shader fallback = source.LoadComponent(dependency.m_PathID);
            if (fallback == null || fallback.m_ParsedForm.m_Name != shader.m_ParsedForm.m_FallbackName ||
                fallback.m_Dependencies.Count != 0)
                throw new InvalidDataException("Unexpected KKUTS fallback shader.");
            fallback.pathID = LocalFallbackPathId;
            fallback.file = cabinet;
            dependency.m_PathID = LocalFallbackPathId;
            return fallback;
        }

        var external = source.References[dependency.m_FileID - 1];
        if (!string.Equals(external.assetPath, "resources/unity_builtin_extra", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unexpected main_skin fallback dependency.");
        cabinet.References.Add(external);
        dependency.m_FileID = 1;
        return null;
    }

    private static Shader LoadStockShader(AssetCabinet source, ShaderSource spec)
    {
        if (source.Version != "5.6.2f1")
            throw new InvalidDataException("Unexpected Unity version " + source.Version);
        Shader shader = source.LoadComponent(spec.PathId);
        if (shader == null || shader.m_ParsedForm.m_Name != spec.InputName || shader.platforms.Count != 1 || shader.platforms[0] != Direct3D11)
            throw new InvalidDataException("Expected the original Direct3D 11 shader: " + spec.InputName);
        return shader;
    }

    private static byte[] ReadStockBlob(Shader shader, ShaderSource spec)
    {
        var blob = LZ4Codec.Decode(shader.compressedBlob, (int)shader.offsets[0], (int)shader.compressedLengths[0], (int)shader.decompressedLengths[0]);
        using (var sha256 = SHA256.Create())
        {
            var hash = BitConverter.ToString(sha256.ComputeHash(blob)).Replace("-", "");
            if (hash != spec.BlobSha256)
                throw new InvalidDataException("The " + spec.InputName + " bytecode differs from the version verified for this patch.");
        }
        return blob;
    }

    private static void ApplyShaderPatch(Shader shader, byte[] originalBlob, byte[] blob, ShaderSource spec)
    {
        var parsed = shader.m_ParsedForm;
        var properties = parsed.m_PropInfo.m_Props;
        var vector = properties.Single(p => p.m_Name == "_LiquidTiling");
        properties.Add(CreateVectorProperty(vector, JuiceVector, "SkinEffects Custom Juice Color", 245f / 255f, 243f / 255f, 235f / 255f, .9f));
        properties.Add(CreateVectorProperty(vector, DensityVector, "SkinEffects Juice Density", 0f, 0f, 0f, 0f));
        properties.Add(CreateVectorProperty(vector, BustWarpVector, "BodyCum bust pattern stretch", 0f, 0f, 0f, 0f));
        var modifiedPrograms = ReadModifiedPrograms(originalBlob, blob);
        var liquidPrograms = new HashSet<int>();
        foreach (var pass in parsed.m_SubShaders.SelectMany(p => p.m_Passes))
        {
            if (!pass.m_NameIndices.Any(n => n.Key == "_liquidmask"))
                continue;
            var maskIndex = pass.m_NameIndices.Single(p => p.Key == "_liquidmask").Value;
            var programs = pass.progFragment.m_SubPrograms
                .Where(p => p.m_Parameters.m_TextureParams.Any(t => t.m_NameIndex == maskIndex))
                .ToArray();
            if (programs.Length == 0)
                continue;

            var nameIndex = pass.m_NameIndices.Max(p => p.Value) + 1;
            pass.m_NameIndices.Add(new KeyValuePair<string, int>(JuiceVector, nameIndex));
            pass.m_NameIndices.Add(new KeyValuePair<string, int>(DensityVector, nameIndex + 1));
            pass.m_NameIndices.Add(new KeyValuePair<string, int>(BustWarpVector, nameIndex + 2));
            pass.m_NameIndices.Sort((left, right) => StringComparer.Ordinal.Compare(left.Key, right.Key));
            var globalsIndex = pass.m_NameIndices.Single(p => p.Key == "$Globals").Value;
            var tilingIndex = pass.m_NameIndices.Single(p => p.Key == "_LiquidTiling").Value;
            foreach (var program in programs)
            {
                var index = (int)program.m_BlobIndex;
                liquidPrograms.Add(index);
                var globals = program.m_Parameters.m_ConstantBuffers.Single(b => b.m_NameIndex == globalsIndex);
                VerifyVectorParameter(blob, index, globals.m_Size, JuiceVector);
                VerifyVectorParameter(blob, index, globals.m_Size + 16, DensityVector);
                VerifyVectorParameter(blob, index, globals.m_Size + 32, BustWarpVector);
                AddVectorParameter(globals, tilingIndex, nameIndex);
                AddVectorParameter(globals, tilingIndex, nameIndex + 1);
                AddVectorParameter(globals, tilingIndex, nameIndex + 2);
            }
        }
        if (liquidPrograms.Count != spec.LiquidProgramCount || !liquidPrograms.SetEquals(modifiedPrograms))
            throw new InvalidDataException("The modified programs do not match the original liquid fragments of " + spec.InputName + ".");

        shader.compressedBlob = LZ4Codec.EncodeHC(blob, 0, blob.Length);
        shader.offsets[0] = 0;
        shader.compressedLengths[0] = (uint)shader.compressedBlob.Length;
        shader.decompressedLengths[0] = (uint)blob.Length;
    }

    private static SerializedProperty CreateVectorProperty(SerializedProperty template, string name, string description, params float[] defaults)
    {
        return new SerializedProperty
        {
            m_Name = name,
            m_Description = description,
            m_Attributes = new List<string>(),
            m_Type = template.m_Type,
            m_Flags = template.m_Flags,
            m_DefTexture = template.m_DefTexture.Clone(),
            m_DefValue = new List<float>(defaults)
        };
    }

    private static void AddVectorParameter(ConstantBuffer globals, int tilingIndex, int nameIndex)
    {
        var tiling = globals.m_VectorParams.Single(p => p.m_NameIndex == tilingIndex);
        if (tiling.m_ArraySize != 0 || tiling.m_Dim != 4 || globals.m_Size % 16 != 0)
            throw new InvalidDataException("Unexpected liquid vector type or constant buffer alignment.");
        using (var descriptor = new MemoryStream(new byte[16]))
        using (var writer = new BinaryWriter(descriptor))
        {
            writer.Write(nameIndex);
            writer.Write(globals.m_Size);
            writer.Write(tiling.m_ArraySize);
            writer.Write(tiling.m_Type);
            writer.Write(tiling.m_Dim);
            descriptor.Position = 0;
            globals.m_VectorParams.Add(new VectorParameter(descriptor));
        }
        globals.m_Size += 16;
    }

    private static HashSet<int> ReadModifiedPrograms(byte[] original, byte[] patched)
    {
        var count = ReadProgramCount(original);
        if (ReadProgramCount(patched) != count)
            throw new InvalidDataException("The patched blob has a different shader program count.");
        var changed = new HashSet<int>();
        for (var index = 0; index < count; index++)
        {
            var before = ReadProgram(original, index);
            var after = ReadProgram(patched, index);
            if (!before.SequenceEqual(after))
                changed.Add(index);
        }
        return changed;
    }

    private static int ReadProgramCount(byte[] blob)
    {
        if (blob.Length < 12)
            throw new InvalidDataException("Missing shader program directory.");
        var count = BitConverter.ToInt32(blob, 0);
        if (count < 1 || count > (blob.Length - 4) / 8)
            throw new InvalidDataException("Invalid shader program count.");
        return count;
    }

    private static ArraySegment<byte> ReadProgram(byte[] blob, int index)
    {
        var count = ReadProgramCount(blob);
        if (index < 0 || index >= count)
            throw new InvalidDataException("Invalid shader program index.");
        var offset = BitConverter.ToInt32(blob, 4 + index * 8);
        var length = BitConverter.ToInt32(blob, 8 + index * 8);
        if (offset < 4 + count * 8 || length < 1 || offset > blob.Length - length)
            throw new InvalidDataException("Invalid shader program range.");
        return new ArraySegment<byte>(blob, offset, length);
    }

    private static void VerifyVectorParameter(byte[] blob, int index, int offset, string property)
    {
        var program = ReadProgram(blob, index);
        var paddedNameLength = (property.Length + 3) & ~3;
        var start = program.Offset;
        var end = start + program.Count - paddedNameLength - 28;
        for (var name = start; name <= end; name += 4)
        {
            if (BitConverter.ToInt32(blob, name) != property.Length ||
                Encoding.ASCII.GetString(blob, name + 4, property.Length) != property)
                continue;
            var descriptor = name + 4 + paddedNameLength;
            if (BitConverter.ToInt32(blob, descriptor + 20) == offset)
                return;
        }
        throw new InvalidDataException("Missing compiled parameter " + property + " for shader program " + index + ".");
    }

    internal static void WriteUnityFs(string path, byte[] signature, byte[] serialized)
    {
        byte[] hash;
        using (var sha256 = SHA256.Create())
            hash = sha256.ComputeHash(serialized);
        var cabinetName = Encoding.ASCII.GetBytes("CAB-" + BitConverter.ToString(hash, 0, 16).Replace("-", "").ToLowerInvariant() + "\0");
        var data = LZ4Codec.EncodeHC(serialized, 0, serialized.Length);
        byte[] directory;
        using (var info = new MemoryStream())
        using (var writer = new BinaryWriter(info))
        {
            writer.Write(new byte[16]);
            WriteUInt32(writer, 1);
            WriteUInt32(writer, (uint)serialized.Length);
            WriteUInt32(writer, (uint)data.Length);
            writer.Write((byte)0);
            writer.Write((byte)3);
            WriteUInt32(writer, 1);
            WriteUInt64(writer, 0);
            WriteUInt64(writer, (ulong)serialized.Length);
            WriteUInt32(writer, 4);
            writer.Write(cabinetName);
            directory = info.ToArray();
        }
        var compressedDirectory = LZ4Codec.EncodeHC(directory, 0, directory.Length);
        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write(signature, 0, 26);
            WriteUInt64(writer, (ulong)(46 + compressedDirectory.Length + data.Length));
            WriteUInt32(writer, (uint)compressedDirectory.Length);
            WriteUInt32(writer, (uint)directory.Length);
            WriteUInt32(writer, 0x43);
            writer.Write(compressedDirectory);
            writer.Write(data);
        }
    }

    private static void VerifyBundle(string path, byte[] blob, bool hasPatch, ShaderSource spec, Shader fallback)
    {
        using (var parser = new UnityParser(path))
        {
            var cabinet = parser.Cabinet;
            var address = cabinet.Bundle.m_Container.Single();
            Shader shader = cabinet.LoadComponent(ShaderPathId);
            var expectedCount = fallback == null ? 2 : 3;
            if (cabinet.Components.Count != expectedCount || address.Key != spec.AssetAddress ||
                address.Value.asset.m_PathID != ShaderPathId || address.Value.preloadSize != expectedCount - 1 ||
                shader.m_ParsedForm.m_Name != spec.CustomName || shader.m_ParsedForm.m_PropInfo.m_Props.Any(p => p.m_Name == JuiceVector) != hasPatch ||
                shader.m_ParsedForm.m_PropInfo.m_Props.Any(p => p.m_Name == DensityVector) != hasPatch ||
                shader.m_ParsedForm.m_PropInfo.m_Props.Any(p => p.m_Name == BustWarpVector) != hasPatch ||
                !shader.compressedBlob.SequenceEqual(blob))
                throw new InvalidDataException("Round-trip validation of the standalone asset bundle failed.");
            if (fallback != null)
            {
                var dependency = shader.m_Dependencies.Single();
                Shader savedFallback = cabinet.LoadComponent(LocalFallbackPathId);
                if (dependency.m_FileID != 0 || dependency.m_PathID != LocalFallbackPathId ||
                    savedFallback == null || savedFallback.m_ParsedForm.m_Name != fallback.m_ParsedForm.m_Name ||
                    !savedFallback.compressedBlob.SequenceEqual(fallback.compressedBlob))
                    throw new InvalidDataException("Round-trip validation of the standalone fallback shader failed.");
            }
        }
        Console.WriteLine("Verified " + Path.GetFullPath(path));
    }

    private static void WriteUInt32(BinaryWriter writer, uint value)
    {
        writer.Write((byte)(value >> 24));
        writer.Write((byte)(value >> 16));
        writer.Write((byte)(value >> 8));
        writer.Write((byte)value);
    }

    private static void WriteUInt64(BinaryWriter writer, ulong value)
    {
        WriteUInt32(writer, (uint)(value >> 32));
        WriteUInt32(writer, (uint)value);
    }
}
