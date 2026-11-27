using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class PatchKkutsJuiceShader
{
    private const string TessHash = "B17B73D6D196A8DAEE2CF253B43CB911AE2B2EB831D6C028B1EB13220698D7D4";
    private const string RegularHash = "0A82D780DC31ECF03DB9FD3405C45539913D51A4421CADD2F9B3CF4FE9E6470B";
    private const string PropertyName = "_SkinEffectsJuiceColor";
    private const string DensityPropertyName = "_SkinEffectsJuiceDensity";
    private const uint Div = 14;
    private const uint Dp3 = 16;
    private const uint Mov = 54;
    private const uint Movc = 55;
    private const uint Mul = 56;
    private const uint Lt = 49;
    private const uint Mad = 50;
    private const uint Add = 0;
    private const uint Saturate = 0x2000;
    private static readonly uint[] BasePatternTextures = { 14, 15, 14, 15 };
    private static readonly uint[] AddPatternTextures = { 10, 9, 11, 11, 10, 12, 12, 10, 11, 11, 11, 12, 12 };

    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("Usage: PatchKkutsJuiceShader.exe <original D3D11 shader blob> <patched blob>");
            return 2;
        }
        try
        {
            var input = File.ReadAllBytes(arguments[0]);
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            Check(hash == TessHash || hash == RegularHash, "Unsupported KKUTS v2.3 shader blob.");

            var output = Patch(input, hash == TessHash);
            File.WriteAllBytes(arguments[1], output);
            Console.WriteLine("Patched 17 KKUTS ForwardBase and ForwardAdd fragments.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    private static byte[] Patch(byte[] input, bool tessellation)
    {
        int count = Dxbc.ReadInt(input, 0);
        var entries = new Variant[count];
        for (int i = 0; i < count; i++)
        {
            int offset = Dxbc.ReadInt(input, 4 + i * 8);
            int length = Dxbc.ReadInt(input, 8 + i * 8);
            var bytes = new byte[length];
            Buffer.BlockCopy(input, offset, bytes, 0, length);
            entries[i] = new Variant(i, offset, bytes);
        }

        int firstBase = tessellation ? 16 : 12;
        int firstAdd = tessellation ? 41 : 29;
        using (var result = new MemoryStream(input.Length + 8192))
        using (var writer = new BinaryWriter(result))
        {
            writer.Write(input, 0, 4 + count * 8);
            foreach (var entry in entries.OrderBy(item => item.Offset))
            {
                var data = entry.Data;
                var variant = entry.Index;
                var forwardBase = IsWithin(variant, firstBase, firstBase + BasePatternTextures.Length - 1);
                var forwardAdd = IsWithin(variant, firstAdd, firstAdd + AddPatternTextures.Length - 1);
                if (forwardBase || forwardAdd)
                {
                    var patternTexture = forwardBase ? BasePatternTextures[variant - firstBase] : AddPatternTextures[variant - firstAdd];
                    data = PatchVariant(data, variant, forwardBase, patternTexture);
                }
                int offset = (int)result.Position;
                writer.Write(data);
                long end = result.Position;
                result.Position = 4 + variant * 8;
                writer.Write(offset);
                writer.Write(data.Length);
                result.Position = end;
            }
            return result.ToArray();
        }
    }

    private static bool IsWithin(int value, int minimum, int maximum)
    {
        return value >= minimum && value <= maximum;
    }

    private static byte[] PatchVariant(byte[] data, int variant, bool forwardBase, uint patternTexture)
    {
        int shaderOffset = FindName(data, "DXBC", 0);
        Check(shaderOffset > 10, "Expected DXBC for variant " + variant);
        int encodedLengthOffset = shaderOffset - 10;
        int oldShaderSize = Dxbc.ReadInt(data, shaderOffset + 24);
        Check(Dxbc.ReadInt(data, encodedLengthOffset) == oldShaderSize + 6,
            "Unexpected Unity shader code length for variant " + variant);
        int globalsNameOffset = Align4(shaderOffset + oldShaderSize) + 28;
        Check(FindName(data, "$Globals", globalsNameOffset) == globalsNameOffset,
            "Unexpected Unity parameter layout for variant " + variant);
        int sizeOffset = globalsNameOffset + 8;
        int originalGlobalsSize = Dxbc.ReadInt(data, sizeOffset);
        Check(originalGlobalsSize >= 1680 && originalGlobalsSize <= 1824 && originalGlobalsSize % 16 == 0,
            "Unexpected Unity global constant buffer size for variant " + variant);
        int countOffset = sizeOffset + 4;
        int parameterCount = Dxbc.ReadInt(data, countOffset);
        int parameterEnd = countOffset + 4;
        for (int i = 0; i < parameterCount; ++i)
            parameterEnd = Align4(parameterEnd + 4 + Dxbc.ReadInt(data, parameterEnd)) + 24;
        Check(FindName(data, "UnityPerCamera", parameterEnd + 4) == parameterEnd + 4,
            "Unexpected end of the Unity global constant buffer for variant " + variant);

        var oldShader = new byte[oldShaderSize];
        Buffer.BlockCopy(data, shaderOffset, oldShader, 0, oldShader.Length);
        uint colorRow = (uint)originalGlobalsSize / 16;
        var newShader = Dxbc.EditProgram(oldShader, program => InjectInstructions(program, colorRow, variant, forwardBase, patternTexture));
        Check(Dxbc.Disassemble(newShader).Contains("dcl_constantbuffer CB0[" + (colorRow + 3) + "], immediateIndexed"),
            "Could not validate patched Direct3D instructions for variant " + variant);

        using (var result = new MemoryStream(data.Length + 512))
        using (var writer = new BinaryWriter(result))
        {
            writer.Write(data, 0, encodedLengthOffset);
            writer.Write(newShader.Length + 6);
            writer.Write(data, encodedLengthOffset + 4, 6);
            writer.Write(newShader);
            writer.Write(data, shaderOffset + oldShaderSize, sizeOffset - shaderOffset - oldShaderSize);
            writer.Write(originalGlobalsSize + 48);
            writer.Write(parameterCount + 3);
            writer.Write(data, countOffset + 4, parameterEnd - countOffset - 4);
            WriteVectorParameter(writer, PropertyName, originalGlobalsSize);
            WriteVectorParameter(writer, DensityPropertyName, originalGlobalsSize + 16);
            WriteVectorParameter(writer, "_BodyCumBustWarp", originalGlobalsSize + 32);
            writer.Write(data, parameterEnd, data.Length - parameterEnd);
            Console.WriteLine("Variant {0}: {1} -> {2} DXBC bytes; color cbuffer row {3}", variant, oldShader.Length, newShader.Length, colorRow);
            return result.ToArray();
        }
    }

    private static uint[] InjectInstructions(uint[] program, uint colorRow, int variant, bool forwardBase, uint patternTexture)
    {
        var instructions = GetInstructionOffsets(program);
        int buffer = instructions.Single(p => program[p] == 0x04000059 && program[p + 2] == 0);
        int temps = instructions.Single(p => program[p] == 0x02000068);
        uint work = program[temps + 1];
        uint pigment = work + 1;
        uint output = work + 2;
        program[temps + 1] += 3 + AbdomenJuiceMask.TemporaryCount;
        program[buffer + 3] = colorRow + 3;

        int tintIndex = instructions.FindIndex(p => program[p] == 0x0F000032 && program[p + 3] == 0x00100FF6 &&
            program[p + 4] == 0 && program[p + 6] == 0x3EB33334 && program[p + 7] == 0x3EE7E7E6 &&
            program[p + 8] == 0x3F1B7B7B && program[p + 11] == 0x3F000000 && program[p + 12] == 0x3ECB4B4E);
        Check(tintIndex >= 0, "No native liquid tint for variant " + variant);
        int tint = instructions[tintIndex];
        int lighting = instructions[tintIndex + 1];
        int rim = instructions[tintIndex + 9];
        int blend = instructions[tintIndex + 11];
        uint tintRegister = program[tint + 2];
        Check(program[lighting] == 0x09000032 && program[lighting + 2] == tintRegister &&
            program[rim] == 0x0A000032 && program[rim + 2] == tintRegister && program[rim + 5] == 0x00208556 &&
            program[blend] == 0x09000032 && program[blend + 3] == 0x00100FF6 && program[blend + 4] == 0 &&
            program[blend + 6] == tintRegister && program[blend + 2] == program[blend + 8] &&
            program[instructions[tintIndex + 12] + 2] == tintRegister,
            "Unexpected native liquid lighting for variant " + variant);

        int lastMaskIndex = instructions.FindLastIndex(tintIndex - 1, p => program[p] == 0x07002034 &&
            program[p + 1] == 0x00100082 && program[p + 2] == 0 && program[p + 3] == 0x0010003A && program[p + 4] == 0);
        Check(lastMaskIndex >= 0, "No native liquid coverage for variant " + variant);
        int normal = instructions[lastMaskIndex + 1];
        Check(program[normal] == 0x09000032 && program[normal + 3] == 0x00100FF6 && program[normal + 4] == 0,
            "Unexpected liquid normal interpolation for variant " + variant);
        int rgb = instructions[instructions.Count - 3];
        int alpha = instructions[instructions.Count - 2];
        Check(program[rgb] == (forwardBase ? 0x09000032 : 0x07002000) && program[rgb + 1] == 0x00102072 &&
            program[rgb + 2] == 0 && program[alpha] == 0x05000036 && program[alpha + 1] == 0x00102082 &&
            program[alpha + 4] == (forwardBase ? 0x3F800000 : 0u) && program[instructions[instructions.Count - 1]] == 0x0100003E,
            "Unexpected pixel output for variant " + variant);

        var result = new List<uint>(program.Length + 200);
        var sampleRegister = work + 3;
        var nativeSample = AbdomenJuiceMask.FindNativeSample(program, patternTexture, patternTexture, normal);
        var liquidEnd = AbdomenJuiceMask.EmitBodyPattern(result, program, nativeSample, colorRow + 1, sampleRegister);
        result.AddRange(program.Skip(liquidEnd).Take(normal - liquidEnd));
        var coverage = AbdomenJuiceMask.ExtendCoverage(result, colorRow + 1, sampleRegister);

        // Remap the completed five-zone, two-tier mask monotonically. For m in [0,1],
        // m*(1+k*m)/(1+k*m*m) fixes both endpoints and keeps the original slope at m=0.
        // Select m directly at zero density to retain bit-identical existing coverage.
        Emit(result, Mov | Saturate, Dest(work, 1), ColorComponent(colorRow + 1, 0));
        Emit(result, Mul, Dest(work, 4), Component(work, 0), FloatLiteral(24f));
        Emit(result, Mul, Dest(work, 4), Component(work, 2), coverage);
        Emit(result, Mad, Dest(work, 2), Component(work, 2), coverage, coverage);
        Emit(result, Mad, Dest(work, 4), Component(work, 2), coverage, FloatLiteral(1f));
        Emit(result, Div, Dest(work, 2), Component(work, 1), Component(work, 2));
        Emit(result, Movc, Dest(work, 2), Component(work, 0), Component(work, 1), coverage);
        Emit(result, Mul, Dest(work, 2), Component(work, 1), ColorComponent(colorRow, 3, absolute: true));

        // Keep wet normals tied to native coverage and opacity so the pigment boost does not
        // deepen the relief or move KKUTS toon shade boundaries further.
        Emit(result, Lt, Dest(work, 4), Immediate(0), ColorComponent(colorRow, 3));
        Emit(result, Mul, Dest(work, 8), Component(0, 3), ColorComponent(colorRow, 3, absolute: true));
        Emit(result, Mul, Dest(work, 8), Component(work, 3), FloatLiteral(.35f));
        Emit(result, Movc, Dest(work, 8), Component(work, 2), Component(work, 3), Immediate(0));
        program[normal + 4] = work;
        result.AddRange(program.Skip(normal).Take(tint - normal));

        if (forwardBase)
        {
            // Use the native liquid light as luminance. A bounded light response keeps the selected
            // pigment visible through KKUTS's multichannel character shading and darker ramps.
            Emit(result, Dp3 | Saturate, Dest(work, 1), program.Skip(lighting + 5).Take(2).ToArray(),
                VectorLiteral(.2126f, .7152f, .0722f));
            Emit(result, Mad, Dest(work, 1), Component(work, 0), FloatLiteral(.38f), FloatLiteral(.62f));
            Emit(result, Mul, Dest(pigment, 7), ColorRgb(colorRow), Replicate(work, 0));
            Emit(result, Movc, Dest(work, 1), Component(work, 2), program.Skip(lighting + 7).Take(2).ToArray(), Immediate(0));
        }
        result.AddRange(program.Skip(instructions[tintIndex + 2]).Take(rim - instructions[tintIndex + 2]));

        if (forwardBase)
        {
            // Native specular and rim feed a bounded highlight on the pigment alone.
            Emit(result, Mul, Dest(work, 8), program.Skip(rim + 3).Take(2).ToArray(), program.Skip(rim + 5).Take(3).ToArray());
            Emit(result, Movc, Dest(work, 8), Component(work, 2), Component(work, 3), Immediate(0));
            Emit(result, Add | Saturate, Dest(work, 1), Component(work, 0), Component(work, 3));
            Emit(result, Mul, Dest(work, 1), Component(work, 0), FloatLiteral(.35f));
            Emit(result, Add, Dest(output, 7), NegateRgb(pigment), VectorLiteral(1f, 1f, 1f));
            Emit(result, Mad | Saturate, Dest(pigment, 7), Replicate(work, 0), Rgb(output), Rgb(pigment));
        }

        // Skip the old in-shader color blend. Leave the existing shaded skin, alpha and clip intact.
        result.AddRange(program.Skip(instructions[tintIndex + 12]).Take(rgb - instructions[tintIndex + 12]));
        var outputOperand = program.Skip(rgb + 1).Take(2).ToArray();
        program[rgb + 1] = Dest(output, 7)[0];
        program[rgb + 2] = output;
        result.AddRange(program.Skip(rgb).Take(alpha - rgb));
        if (forwardBase)
        {
            Emit(result, Add, Dest(pigment, 7), NegateRgb(output), Rgb(pigment));
            Emit(result, Mad, Dest(pigment, 7), Replicate(work, 1), Rgb(pigment), Rgb(output));
        }
        else
        {
            // The base pass supplies the pigment. Additive lights retain their dry contribution
            // in proportion to the uncovered skin so they cannot recolor opaque liquid again.
            Emit(result, Mad, Dest(work, 8), Component(work, 1), FloatLiteral(-1f), FloatLiteral(1f));
            Emit(result, Mul, Dest(pigment, 7), Rgb(output), Replicate(work, 3));
        }
        Emit(result, Lt, Dest(work, 1), Immediate(0), Component(work, 1));
        Emit(result, Movc, outputOperand, Replicate(work, 0), Rgb(pigment), Rgb(output));
        result.AddRange(program.Skip(alpha));
        return result.ToArray();
    }

    private static List<int> GetInstructionOffsets(uint[] program)
    {
        var offsets = new List<int>();
        for (int cursor = 2; cursor < program.Length;)
        {
            offsets.Add(cursor);
            int length = (int)((program[cursor] >> 24) & 127);
            Check(length > 0 && cursor + length <= program.Length, "Invalid Direct3D instruction length.");
            cursor += length;
        }
        return offsets;
    }

    private static uint[] Dest(uint register, uint mask)
    {
        return new[] { 0x00100002u | mask << 4, register };
    }

    private static uint[] Component(uint register, uint component)
    {
        return new[] { 0x0010000Au | component << 4, register };
    }

    private static uint[] Replicate(uint register, uint component)
    {
        return new[] { 0x00100006u | component << 4 | component << 6 | component << 8 | component << 10, register };
    }

    private static uint[] Rgb(uint register)
    {
        return new[] { 0x00100246u, register };
    }

    private static uint[] NegateRgb(uint register)
    {
        return new[] { 0x80100246u, 0x41u, register };
    }

    private static uint[] ColorComponent(uint row, uint component, bool absolute = false)
    {
        uint token = 0x0020800Au | component << 4;
        return absolute ? new[] { token | 0x80000000u, 0x81u, 0u, row } : new[] { token, 0u, row };
    }

    private static uint[] ColorRgb(uint row)
    {
        return new[] { 0x00208246u, 0u, row };
    }

    private static uint[] Immediate(uint integerBits)
    {
        return new[] { 0x00004001u, integerBits };
    }

    private static uint[] FloatLiteral(float value)
    {
        return Immediate(FloatBits(value));
    }

    private static uint[] VectorLiteral(float r, float g, float b)
    {
        return new[] { 0x00004002u, FloatBits(r), FloatBits(g), FloatBits(b), 0u };
    }

    private static uint FloatBits(float value) { return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0); }

    private static void Emit(List<uint> code, uint opcode, params uint[][] operands)
    {
        code.Add(opcode | ((1u + (uint)operands.Sum(values => values.Length)) << 24));
        foreach (var operand in operands)
            code.AddRange(operand);
    }

    private static void WriteVectorParameter(BinaryWriter writer, string propertyName, int byteOffset)
    {
        var name = Encoding.ASCII.GetBytes(propertyName);
        writer.Write(name.Length);
        writer.Write(name);
        while ((writer.BaseStream.Position & 3) != 0)
            writer.Write((byte)0);
        // Unity serializes float, one row, four columns, no array, no matrix transpose, then byte offset.
        writer.Write(0);
        writer.Write(1);
        writer.Write(4);
        writer.Write(0);
        writer.Write(0);
        writer.Write(byteOffset);
    }

    private static int FindName(byte[] data, string name, int start)
    {
        byte[] pattern = Encoding.ASCII.GetBytes(name);
        for (int i = start; i <= data.Length - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }
        return -1;
    }

    private static int Align4(int position)
    {
        return (position + 3) & ~3;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidDataException(message);
    }

    private sealed class Variant
    {
        internal readonly int Index;
        internal readonly int Offset;
        internal readonly byte[] Data;

        internal Variant(int index, int offset, byte[] data)
        {
            Index = index;
            Offset = offset;
            Data = data;
        }
    }
}
