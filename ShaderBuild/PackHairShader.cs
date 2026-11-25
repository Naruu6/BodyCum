using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using LZ4;
using UnityPlugin;
using Object = UnityPlugin.Object;

// Author a two-program Unity 5.6 shader from HLSL and the local shader's serialization template.
internal static class PackHairShader
{
    private const long TemplatePathId = -6692073393702066725L;
    private const long ShaderPathId = 2;
    private static readonly string[] HairTextures = { "_HairSourceTex", "_HairAlphaTex", "_HairPatternTex" };
    private static readonly string[] HairVectors =
    {
        "_HairProjectionX", "_HairProjectionY", "_HairProjectionZ", "_HairJuiceColor",
        "_HairJuiceParams", "_HairSourceTex_ST", "_HairAlphaTex_ST", "_HairAlphaParams", "_HairPatternTex_ST"
    };

    public static int Main(string[] args)
    {
        bool skin = args.Length == 5 && args[4] == "--skin";
        if (args.Length != 4 && !skin)
        {
            Console.Error.WriteLine("Usage: PackHairShader.exe <SB3Utility folder> <mm_base.unity3d> <source HLSL> <output bundle> [--skin]");
            return 2;
        }
        var folders = new[] { Path.GetFullPath(args[0]), Path.Combine(Path.GetFullPath(args[0]), "plugins") };
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            var name = new AssemblyName(request.Name).Name + ".dll";
            foreach (var folder in folders)
            {
                var file = Path.Combine(folder, name);
                if (File.Exists(file))
                    return Assembly.LoadFrom(file);
            }
            return null;
        };

        try
        {
            BuildBundle(args[1], args[2], args[3], skin);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BuildBundle(string input, string hlslPath, string output, bool skin)
    {
        var shaderName = skin ? "SkinEffects/BodyCum Skin Projection" : "SkinEffects/Hair Juice Projection";
        var address = skin ? "assets/skineffects/bodycum_skin.shader" : "assets/skineffects/hair_juice.shader";
        var bundleName = skin ? "skineffects_bodycum_skin" : "skineffects_hair_juice";
        // Skin retains its original C9 mask ST. Hair alone adds C9 dimensions and C10 local offsets.
        var vectors = skin ? HairVectors.Concat(new[] { "_SkinMask_ST" }).ToArray() :
            HairVectors.Concat(new[] { "_HairProjectionDimensions", "_HairProjectionOffsets" }).ToArray();
        var textures = skin ? HairTextures.Concat(new[] { "_SkinMask" }).ToArray() : HairTextures;
        var sourceCode = File.ReadAllText(hlslPath);
        var vertexCode = Dxbc.Compile("#define HAIR_VERTEX\n" + sourceCode, "vs_4_0");
        var pixelCode = Dxbc.Compile(sourceCode, "ps_4_0");
        using (var original = new UnityParser(input))
        using (var standalone = new UnityParser(original))
        {
            var source = original.Cabinet;
            var cabinet = standalone.Cabinet;
            if (source.Version != "5.6.2f1")
                throw new InvalidDataException("Expected the local Unity 5.6.2f1 shader bundle.");
            Shader shader = source.LoadComponent(TemplatePathId);
            if (shader == null || shader.m_ParsedForm.m_Name != "Shader Forge/main_skin" ||
                shader.platforms.Count != 1 || shader.platforms[0] != 4)
                throw new InvalidDataException("Could not find the Direct3D 11 serialization template.");

            var raw = LZ4Codec.Decode(shader.compressedBlob, (int)shader.offsets[0], (int)shader.compressedLengths[0], (int)shader.decompressedLengths[0]);
            var pass = shader.m_ParsedForm.m_SubShaders.SelectMany(s => s.m_Passes)
                .Single(p => p.progVertex.m_SubPrograms.Any(s => s.m_BlobIndex == 4));
            var vertex = pass.progVertex.m_SubPrograms.Single(s => s.m_BlobIndex == 4);
            var pixel = pass.progFragment.m_SubPrograms.Single(s => s.m_BlobIndex == 8);
            if (!skin)
                ValidateSurfaceChannels(vertex, vertexCode);
            var vectorType = pixel.m_Parameters.m_ConstantBuffers.First().m_VectorParams.First().m_Type;
            var blob = WritePrograms(GetProgram(raw, 4), GetProgram(raw, 12), vertexCode, pixelCode, vectors, textures);

            var names = pass.m_NameIndices.ToDictionary(pair => pair.Key, pair => pair.Value);
            var nextIndex = names.Values.Max() + 1;
            foreach (var name in vectors.Concat(textures))
                names.Add(name, nextIndex++);
            pass.m_NameIndices = names.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToList();

            vertex.m_BlobIndex = 0;
            vertex.m_GlobalKeywordIndices.Clear();
            pixel.m_BlobIndex = 1;
            pixel.m_GlobalKeywordIndices.Clear();
            pass.progVertex.m_SubPrograms.Clear();
            pass.progVertex.m_SubPrograms.Add(vertex);
            pass.progFragment.m_SubPrograms.Clear();
            pass.progFragment.m_SubPrograms.Add(pixel);
            SetupPixelBindings(pixel.m_Parameters, names, vectorType, cabinet.VersionNumber, vectors, textures);
            ValidatePixelSamplerBindings(GetProgram(blob, 1), pixel.m_Parameters, names, textures);

            SetOverlayState(pass, skin);
            var subShader = shader.m_ParsedForm.m_SubShaders.Single(s => s.m_Passes.Contains(pass));
            subShader.m_Passes.Clear();
            subShader.m_Passes.Add(pass);
            var parsed = shader.m_ParsedForm;
            parsed.m_SubShaders.Clear();
            parsed.m_SubShaders.Add(subShader);
            SetShaderProperties(parsed, vectors, textures);
            parsed.m_Name = shaderName;
            parsed.m_CustomEditorName = string.Empty;
            parsed.m_FallbackName = string.Empty;
            parsed.m_Dependencies.Clear();
            shader.m_Dependencies.Clear();
            cabinet.References.Clear();
            shader.pathID = ShaderPathId;
            shader.file = cabinet;
            shader.compressedBlob = LZ4Codec.EncodeHC(blob, 0, blob.Length);
            shader.offsets[0] = 0;
            shader.compressedLengths[0] = (uint)shader.compressedBlob.Length;
            shader.decompressedLengths[0] = (uint)blob.Length;

            var bundle = new AssetBundle(cabinet)
            {
                pathID = 1,
                m_Name = bundleName,
                m_AssetBundleName = bundleName,
                m_RuntimeCompatibility = source.Bundle.m_RuntimeCompatibility,
                m_Dependencies = new List<string>(),
                m_PreloadTable = new List<PPtr<Object>> { new PPtr<Object>(shader) },
                m_MainAsset = new AssetInfo(cabinet) { asset = new PPtr<Object>((Component)null) },
                m_Container = new List<KeyValuePair<string, AssetInfo>>
                {
                    new KeyValuePair<string, AssetInfo>(address,
                        new AssetInfo(cabinet) { asset = new PPtr<Object>(shader), preloadIndex = 0, preloadSize = 1 })
                }
            };
            cabinet.Components.Add(shader);
            cabinet.Types.RemoveAll(type => type.typeId != (int)UnityClassID.Shader && type.typeId != (int)UnityClassID.AssetBundle);
            if (cabinet.Types.Count != 2 || cabinet.Components.Count != 2 || cabinet.Bundle != bundle)
                throw new InvalidDataException("Unexpected components in the hair shader bundle.");

            using (var bytes = new MemoryStream())
            {
                cabinet.WriteTo(bytes);
                PackSkinShader.WriteUnityFs(output, original.ExtendedSignature, bytes.ToArray());
            }
        }

        using (var check = new UnityParser(output))
        {
            Shader packed = check.Cabinet.LoadComponent(ShaderPathId);
            var pass = packed.m_ParsedForm.m_SubShaders.Single().m_Passes.Single();
            if (check.Cabinet.Bundle.m_Container.Single().Key != address ||
                !vectors.Concat(textures).SequenceEqual(packed.m_ParsedForm.m_PropInfo.m_Props.Select(p => p.m_Name)) ||
                packed.m_ParsedForm.m_Name != shaderName || pass.progVertex.m_SubPrograms.Single().m_BlobIndex != 0 ||
                pass.progFragment.m_SubPrograms.Single().m_BlobIndex != 1)
                throw new InvalidDataException("Projection shader bundle did not round-trip.");
            if (!skin)
                ValidateSurfaceChannels(pass.progVertex.m_SubPrograms.Single(), vertexCode);
            var readback = LZ4Codec.Decode(packed.compressedBlob, (int)packed.offsets[0],
                (int)packed.compressedLengths[0], (int)packed.decompressedLengths[0]);
            var names = pass.m_NameIndices.ToDictionary(pair => pair.Key, pair => pair.Value);
            ValidatePixelSamplerBindings(GetProgram(readback, 1),
                pass.progFragment.m_SubPrograms.Single().m_Parameters, names, textures);
        }
        Console.WriteLine("Verified " + Path.GetFullPath(output));
    }

    private static void ValidateSurfaceChannels(SerializedSubProgram vertex, byte[] code)
    {
        // Unity mesh channel 4 is UV1, bound to shader channel 5 (TEXCOORD1).
        // Keep the native binding tail, but reject templates that would silently
        // feed zeros to the frozen three-dimensional surface coordinates.
        if ((vertex.m_Channels.m_SourceMap & (1 << 4)) == 0 ||
            !vertex.m_Channels.m_Channels.Any(channel => channel.source == 4 && channel.target == 5))
            throw new InvalidDataException("The projection vertex program does not bind mesh UV1.");

        var signature = Dxbc.FindChunk(code, "ISGN");
        if (signature < 0)
            throw new InvalidDataException("The projection vertex input signature is missing.");
        var count = BitConverter.ToInt32(code, signature + 8);
        for (var index = 0; index < count; index++)
        {
            var entry = signature + 16 + index * 24;
            var name = signature + 8 + BitConverter.ToInt32(code, entry);
            var end = Array.IndexOf(code, (byte)0, name);
            if (end < 0)
                throw new InvalidDataException("The projection vertex input signature is invalid.");
            if (Encoding.ASCII.GetString(code, name, end - name) == "TEXCOORD" &&
                BitConverter.ToInt32(code, entry + 4) == 1 &&
                (code[entry + 20] & 7) == 7 && (code[entry + 21] & 7) == 7)
                return;
        }
        throw new InvalidDataException("The projection vertex shader must read TEXCOORD1.xyz.");
    }

    private static void SetShaderProperties(SerializedShader shader, string[] vectors, string[] textures)
    {
        var oldProperties = shader.m_PropInfo.m_Props;
        var vector = oldProperties.Single(p => p.m_Name == "_LiquidTiling");
        var texture = oldProperties.Single(p => p.m_Name == "_MainTex");
        var properties = new List<SerializedProperty>();
        foreach (var parameter in vectors)
        {
            float[] defaults = new float[4];
            if (parameter == "_HairJuiceColor")
                defaults = new[] { 1f, 1f, 1f, 0f };
            else if (parameter.EndsWith("_ST", StringComparison.Ordinal))
                defaults = new[] { 1f, 1f, 0f, 0f };
            properties.Add(BuildProperty(vector, parameter, defaults));
        }
        for (int slot = 0; slot < textures.Length; slot++)
        {
            var property = BuildProperty(texture, textures[slot], new float[4]);
            // Projection patterns and region masks contribute no coverage until supplied at runtime.
            if (slot >= 2)
                property.m_DefTexture.m_DefaultName = "black";
            properties.Add(property);
        }
        shader.m_PropInfo.m_Props = properties;
    }

    private static SerializedProperty BuildProperty(SerializedProperty template, string name, float[] values)
    {
        return new SerializedProperty
        {
            m_Name = name,
            m_Description = name,
            m_Attributes = new List<string>(),
            m_Type = template.m_Type,
            m_Flags = template.m_Flags,
            m_DefTexture = template.m_DefTexture.Clone(),
            m_DefValue = new List<float>(values)
        };
    }

    private static void SetupPixelBindings(SerializedProgramParameters parameters, Dictionary<string, int> names,
        sbyte vectorType, uint version, string[] vectors, string[] textures)
    {
        parameters.m_VectorParams.Clear();
        parameters.m_MatrixParams.Clear();
        parameters.m_TextureParams.Clear();
        parameters.m_BufferParams.Clear();
        parameters.m_UAVParams.Clear();
        var globals = parameters.m_ConstantBuffers.Single(b => b.m_NameIndex == names["$Globals"]);
        globals.m_VectorParams.Clear();
        globals.m_MatrixParams.Clear();
        globals.m_Size = 16 * vectors.Length;
        for (int i = 0; i < vectors.Length; i++)
        {
            using (var stream = new MemoryStream(new byte[16]))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write(names[vectors[i]]);
                writer.Write(16 * i);
                writer.Write(0);
                writer.Write(vectorType);
                writer.Write((sbyte)4);
                stream.Position = 0;
                globals.m_VectorParams.Add(new VectorParameter(stream));
            }
        }
        parameters.m_ConstantBuffers.Clear();
        parameters.m_ConstantBuffers.Add(globals);
        var binding = parameters.m_ConstantBufferBindings.Single(b => b.m_NameIndex == globals.m_NameIndex);
        binding.m_Index = 0;
        parameters.m_ConstantBufferBindings.Clear();
        parameters.m_ConstantBufferBindings.Add(binding);
        for (int slot = 0; slot < textures.Length; slot++)
            parameters.m_TextureParams.Add(TextureBinding(names[textures[slot]], slot, version));
    }

    private static TextureParameter TextureBinding(int nameIndex, int slot, uint version)
    {
        using (var stream = new MemoryStream(new byte[16]))
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(nameIndex);
            writer.Write(slot);
            writer.Write(slot);
            writer.Write((sbyte)2);
            stream.Position = 0;
            return new TextureParameter(stream, version);
        }
    }

    private static void SetOverlayState(SerializedPass pass, bool skin)
    {
        // Preserve the template's literal-value markers and its double-sided LessEqual test.
        var state = pass.m_State;
        state.zWrite.val = 0;
        if (!skin)
        {
            // The CPU-skinned overlay must pass the source surface's depth despite CPU/GPU rounding.
            // A small decal bias avoids coplanar depth competition without disabling occlusion.
            state.offsetFactor.val = -1;
            state.offsetUnits.val = -1;
        }
        state.rtBlend0.srcBlend.val = 5; // SrcAlpha.
        state.rtBlend0.destBlend.val = 10; // OneMinusSrcAlpha.
        state.rtBlend0.srcBlendAlpha.val = 0;
        state.rtBlend0.destBlendAlpha.val = 1;
        state.rtBlend0.colMask.val = 14; // Preserve destination alpha.
    }

    private static byte[] GetProgram(byte[] blob, int index)
    {
        var offset = BitConverter.ToInt32(blob, 4 + index * 8);
        var length = BitConverter.ToInt32(blob, 8 + index * 8);
        if (length < 40 || offset < 0 || offset > blob.Length - length)
            throw new InvalidDataException("Invalid serialization template program.");
        var result = new byte[length];
        Buffer.BlockCopy(blob, offset, result, 0, length);
        return result;
    }

    private static byte[] WritePrograms(byte[] vertexTemplate, byte[] pixelTemplate, byte[] vertexCode, byte[] pixelCode,
        string[] vectors, string[] textures)
    {
        var vertexCodeOffset = 54;
        if (Encoding.ASCII.GetString(vertexTemplate, vertexCodeOffset, 4) != "DXBC" ||
            BitConverter.ToInt32(vertexTemplate, 4) != 15 || BitConverter.ToInt32(pixelTemplate, 4) != 17)
            throw new InvalidDataException("Unexpected Direct3D 11 serialization template headers.");
        int vertexBindings = (vertexCodeOffset + BitConverter.ToInt32(vertexTemplate, vertexCodeOffset + 24) + 3) & ~3;
        using (var vertex = new MemoryStream())
        using (var pixel = new MemoryStream())
        using (var output = new MemoryStream())
        using (var vertexWriter = new BinaryWriter(vertex))
        using (var pixelWriter = new BinaryWriter(pixel))
        using (var outputWriter = new BinaryWriter(output))
        {
            WriteProgramHeader(vertexWriter, vertexTemplate, vertexCode, 0, 2);
            vertexWriter.Write(vertexTemplate, vertexBindings, vertexTemplate.Length - vertexBindings);

            WriteProgramHeader(pixelWriter, pixelTemplate, pixelCode, (byte)textures.Length, 1);
            WritePixelBindings(pixelWriter, vectors, textures);

            const int DirectorySize = 20;
            outputWriter.Write(2);
            outputWriter.Write(DirectorySize);
            outputWriter.Write((int)vertex.Length);
            outputWriter.Write(DirectorySize + (int)vertex.Length);
            outputWriter.Write((int)pixel.Length);
            outputWriter.Write(vertex.ToArray());
            outputWriter.Write(pixel.ToArray());
            return output.ToArray();
        }
    }

    private static void WriteProgramHeader(BinaryWriter writer, byte[] template, byte[] code, byte textures, byte buffers)
    {
        writer.Write(template, 0, 24);
        writer.Write(0); // No keyword variants.
        writer.Write(code.Length + 6);
        writer.Write(new[] { (byte)1, textures, buffers, textures, (byte)0, (byte)0 });
        writer.Write(code);
        Align(writer);
    }

    private static void WritePixelBindings(BinaryWriter writer, string[] vectors, string[] textures)
    {
        writer.Write(0); // Mesh channel mask.
        writer.Write(0); // Mesh channel bindings.
        writer.Write(2); // Empty root parameter group and $Globals.
        WriteBufferHeader(writer, string.Empty, 0, 0);
        WriteBufferHeader(writer, "$Globals", 16 * vectors.Length, vectors.Length);
        for (int i = 0; i < vectors.Length; i++)
        {
            WriteName(writer, vectors[i]);
            writer.Write(0); // Float.
            writer.Write(1); // Rows.
            writer.Write(4); // Components.
            writer.Write(0); // No array.
            writer.Write(0); // No matrix transpose.
            writer.Write(16 * i);
        }
        writer.Write(textures.Length + 1);
        for (int slot = 0; slot < textures.Length; slot++)
            WriteBinding(writer, textures[slot], 0, slot, 2, slot);
        WriteBinding(writer, "$Globals", 1, 0, 0, 0);
    }

    private static void ValidatePixelSamplerBindings(byte[] program, SerializedProgramParameters parameters,
        Dictionary<string, int> names, string[] textures)
    {
        int codeOffset = -1;
        for (int i = 0; i <= Math.Min(256, program.Length - 32); i++)
            if (Encoding.ASCII.GetString(program, i, 4) == "DXBC") { codeOffset = i; break; }
        if (codeOffset < 0) throw new InvalidDataException("Pixel program bytecode is missing.");
        int codeLength = BitConverter.ToInt32(program, codeOffset + 24);
        if (codeLength < 32 || codeOffset > program.Length - codeLength)
            throw new InvalidDataException("Pixel program bytecode length is invalid.");
        using (var stream = new MemoryStream(program, false))
        using (var reader = new BinaryReader(stream))
        {
            stream.Position = (codeOffset + codeLength + 3) & ~3;
            if (reader.ReadInt32() != 0 || reader.ReadInt32() != 0)
                throw new InvalidDataException("Unexpected pixel mesh-channel bindings.");
            int groups = reader.ReadInt32();
            if (groups < 0 || groups > 16) throw new InvalidDataException("Invalid pixel buffer count.");
            for (int group = 0; group < groups; group++)
            {
                ReadBindingName(reader); reader.ReadInt32(); int count = reader.ReadInt32();
                if (count < 0 || count > 256) throw new InvalidDataException("Invalid pixel parameter count.");
                for (int entry = 0; entry < count; entry++)
                {
                    ReadBindingName(reader);
                    for (int field = 0; field < 6; field++) reader.ReadInt32();
                }
            }
            if (reader.ReadInt32() != textures.Length + 1 || parameters.m_TextureParams.Count != textures.Length)
                throw new InvalidDataException("Raw and serialized pixel resource counts differ.");
            var found = new HashSet<string>(StringComparer.Ordinal);
            for (int entry = 0; entry < textures.Length + 1; entry++)
            {
                string name = ReadBindingName(reader);
                int kind = reader.ReadInt32(), slot = reader.ReadInt32(), encoded = reader.ReadInt32();
                if (!found.Add(name)) throw new InvalidDataException("Duplicate pixel resource binding.");
                if (name == "$Globals")
                {
                    if (kind != 1 || slot != 0 || encoded != 0)
                        throw new InvalidDataException("Invalid raw pixel constant-buffer binding.");
                    continue;
                }
                int expected = Array.IndexOf(textures, name);
                if (expected < 0 || kind != 0 || slot != expected || encoded != (2 | (expected << 8)))
                    throw new InvalidDataException("Invalid raw texture/sampler pair for " + name + ".");
                var serialized = parameters.m_TextureParams.Single(p => p.m_NameIndex == names[name]);
                if (serialized.m_Index != slot || serialized.m_SamplerIndex != (encoded >> 8) ||
                    serialized.m_Dim != (encoded & 255) || serialized.m_MultiSampled)
                    throw new InvalidDataException("Raw and serialized texture/sampler pairs differ for " + name + ".");
            }
            if (stream.Position != stream.Length)
                throw new InvalidDataException("Unexpected trailing raw pixel binding data.");
        }
    }

    private static string ReadBindingName(BinaryReader reader)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > 16384 || count > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid raw binding name length.");
        var bytes = reader.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException();
        reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
        return Encoding.ASCII.GetString(bytes);
    }

    private static void WriteBufferHeader(BinaryWriter writer, string name, int length, int parameters)
    {
        WriteName(writer, name);
        writer.Write(length);
        writer.Write(parameters);
    }

    private static void WriteBinding(BinaryWriter writer, string name, int kind, int slot, int dimension, int samplerIndex)
    {
        if (dimension < 0 || dimension > 255 || samplerIndex < 0 || samplerIndex > 15)
            throw new InvalidDataException("Invalid Direct3D texture/sampler binding.");
        WriteName(writer, name);
        writer.Write(kind);
        writer.Write(slot);
        // Unity 5.6's raw D3D resource word contains BOTH the texture dimension
        // and the sampler index. Omitting the upper byte assigns every texture s0.
        writer.Write(dimension | (samplerIndex << 8));
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        var bytes = Encoding.ASCII.GetBytes(name);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        Align(writer);
    }

    private static void Align(BinaryWriter writer)
    {
        while ((writer.BaseStream.Position & 3) != 0)
            writer.Write((byte)0);
    }
}
