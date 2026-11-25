using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal static class PatchJuiceShader
{
    private const string OriginalHash = "D973679D6F677ED51C13D33569B475F869082A4A1D25611B9BBB9F4B91C75E6F";
    private const string PropertyName = "_SkinEffectsJuiceColor";
    private const string DensityPropertyName = "_SkinEffectsJuiceDensity";
    private const int OriginalGlobalsSize = 36 * 16;
    private const int PatchedGlobalsSize = OriginalGlobalsSize + 32;
    private const int DefaultVariant = 8;
    private const int ScreenShadowVariant = 9;

    private static int Main(string[] arguments)
    {
        if (arguments.Length != 2)
        {
            Console.Error.WriteLine("Usage: PatchJuiceShader.exe <original D3D11 shader blob> <patched blob>");
            return 2;
        }
        try
        {
            var input = File.ReadAllBytes(arguments[0]);
            using (var sha = SHA256.Create())
                Check(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "") == OriginalHash,
                    "Unsupported stock main_skin shader blob.");

            var output = Patch(input);
            File.WriteAllBytes(arguments[1], output);
            Console.WriteLine("Patched two ForwardBase variants. " + input.Length + " -> " + output.Length + " bytes.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static byte[] Patch(byte[] input)
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

        using (var result = new MemoryStream(input.Length + 1024))
        using (var writer = new BinaryWriter(result))
        {
            writer.Write(input, 0, 4 + count * 8);
            foreach (var entry in entries.OrderBy(item => item.Offset))
            {
                byte[] data = entry.Data;
                if (entry.Index == DefaultVariant || entry.Index == ScreenShadowVariant)
                    data = PatchVariant(data, entry.Index == ScreenShadowVariant);
                var offset = (int)result.Position;
                writer.Write(data);
                var end = result.Position;
                result.Position = 4 + entry.Index * 8;
                writer.Write(offset);
                writer.Write(data.Length);
                result.Position = end;
            }
            return result.ToArray();
        }
    }

    private static byte[] PatchVariant(byte[] data, bool screenShadows)
    {
        int encodedLengthOffset = screenShadows ? 64 : 44;
        int shaderOffset = encodedLengthOffset + 10;
        int oldShaderSize = Dxbc.ReadInt(data, shaderOffset + 24);
        Check(Dxbc.ReadInt(data, encodedLengthOffset) == oldShaderSize + 6,
            "Unexpected Unity shader code length.");
        var oldShader = new byte[oldShaderSize];
        Buffer.BlockCopy(data, shaderOffset, oldShader, 0, oldShader.Length);
        var newShader = Dxbc.EditProgram(oldShader, program => InjectInstructions(program, screenShadows));
        Check(Dxbc.Disassemble(newShader).Contains("dcl_constantbuffer CB0[38], immediateIndexed"),
            "Could not validate patched Direct3D instructions.");

        int uniformsStart = Align4(shaderOffset + oldShaderSize);
        int globalsNameOffset = uniformsStart + 28;
        Check(Encoding.ASCII.GetString(data, globalsNameOffset, 8) == "$Globals",
            "Unexpected Unity parameter layout.");
        int sizeOffset = globalsNameOffset + 8;
        int countOffset = sizeOffset + 4;
        int parameterCount = Dxbc.ReadInt(data, countOffset);
        Check(Dxbc.ReadInt(data, sizeOffset) == OriginalGlobalsSize && parameterCount == 45,
            "Unexpected Unity global constant buffer.");
        int parameterEnd = countOffset + 4;
        for (int i = 0; i < parameterCount; ++i)
            parameterEnd = Align4(parameterEnd + 4 + Dxbc.ReadInt(data, parameterEnd)) + 24;
        Check(Encoding.ASCII.GetString(data, parameterEnd + 4, 14) == "UnityPerCamera",
            "Unexpected end of the Unity global constant buffer.");

        using (var result = new MemoryStream(data.Length + 256))
        using (var writer = new BinaryWriter(result))
        {
            writer.Write(data, 0, encodedLengthOffset);
            writer.Write(newShader.Length + 6);
            writer.Write(data, encodedLengthOffset + 4, 6);
            writer.Write(newShader);
            writer.Write(data, shaderOffset + oldShaderSize, sizeOffset - shaderOffset - oldShaderSize);
            writer.Write(PatchedGlobalsSize);
            writer.Write(parameterCount + 2);
            writer.Write(data, countOffset + 4, parameterEnd - countOffset - 4);
            WriteVectorParameter(writer, PropertyName, OriginalGlobalsSize);
            WriteVectorParameter(writer, DensityPropertyName, OriginalGlobalsSize + 16);
            writer.Write(data, parameterEnd, data.Length - parameterEnd);
            return result.ToArray();
        }
    }

    private static uint[] InjectInstructions(uint[] program, bool screenShadows)
    {
        const int stockMask = 0;
        const int normal = 3;
        const int normalDelta = 4;
        const int diffuseLight = 7;
        const int custom = 10;
        const int highlightRange = 11;
        const int density = 12;
        int tempCountOffset = screenShadows ? 157 : 147;
        int normalWeightOffset = screenShadows ? 1405 : 1395;
        int colorOffset = screenShadows ? 2335 : 2300;
        const int stockColorInstructionLength = 24;
        Check(program[2] == 0x04000059 && program[5] == 36 &&
            program[tempCountOffset - 1] == 0x02000068 && program[tempCountOffset] == 10 &&
            program[normalWeightOffset] == 0x09000032 && program[normalWeightOffset + 3] == 0x00100FF6 &&
            program[colorOffset] == 0x0F000032 && program[colorOffset + 7] == 0x3EB33334 &&
            program[colorOffset + 15] == 0x09000032,
            "Unexpected Direct3D instruction layout.");
        program[5] = 38;
        program[tempCountOffset] = 13 + AbdomenJuiceMask.TemporaryCount;

        var result = new List<uint>(program.Length + 80);
        var nativeSample = AbdomenJuiceMask.FindNativeSample(program, 3, screenShadows ? 10u : 9u, normalWeightOffset);
        result.AddRange(program.Take(nativeSample.Start));
        AbdomenJuiceMask.SaveShiftedSample(result, program, nativeSample, 37, 13);
        result.AddRange(program.Skip(nativeSample.Start).Take(nativeSample.End - nativeSample.Start));
        AbdomenJuiceMask.SaveNativeSample(result, nativeSample, 13);
        result.AddRange(program.Skip(nativeSample.End).Take(normalWeightOffset - nativeSample.End));
        var coverage = AbdomenJuiceMask.ExtendCoverage(result, 37, 13);

        // Preserve native slopes at the edge. Choose native coverage exactly when density is zero.
        Add(result, 54 | 0x2000, Dest(density, 1), new[] { 0x0020800Au, 0u, 37u });
        Add(result, 56, Dest(density, 2), Component(density, 0), Literal(24));
        Add(result, 56, Dest(density, 2), Component(density, 1), coverage);
        Add(result, 50, Dest(density, 4), Component(density, 1), coverage, Literal(1));
        Add(result, 0, Dest(density, 2), Component(density, 1), Literal(1));
        Add(result, 56, Dest(density, 2), Component(density, 1), coverage);
        Add(result, 14, Dest(density, 2), Component(density, 1), Component(density, 2));
        Add(result, 55, Dest(density, 2), Component(density, 0), Component(density, 1), coverage);

        Add(result, 51, Dest(custom, 1), ColorComponent(3, absolute: true), Literal(1));
        Add(result, 56, Dest(density, 2), Component(density, 1), Component(custom, 0));
        Add(result, 56, Dest(stockMask, 8), Component(stockMask, 3), Component(custom, 0));
        Add(result, 49, Dest(custom, 8), Literal(0), ColorComponent(3));

        // The signed opacity selects whether Unity's original liquid normal is blended into skin.
        Add(result, 55, Dest(custom, 1), Component(custom, 3), Component(stockMask, 3), Literal(0));
        Add(result, 50, Dest(normal, 7), Swizzle(custom, 0, 0, 0, 0), Rgb(normalDelta), Rgb(normal));

        result.AddRange(program.Skip(normalWeightOffset + 9).Take(colorOffset - normalWeightOffset - 9));

        // Use the same native directional term and specular lobe. Bound highlights by RGB headroom.
        Add(result, 56 | 0x2000, Dest(custom, 7), ColorRgb(), Rgb(diffuseLight));
        Add(result, 55, Dest(normal, 1), Component(custom, 3), Component(normal, 0), Literal(0));
        Add(result, 0, Dest(highlightRange, 7), NegateRgb(custom), LiteralRgbOne());
        Add(result, 50, Dest(normal, 7), Swizzle(normal, 0, 0, 0, 0), Rgb(highlightRange), Rgb(custom));
        Add(result, 54, Dest(stockMask, 8), Component(density, 1));

        result.AddRange(program.Skip(colorOffset + stockColorInstructionLength));
        return result.ToArray();
    }

    // DXBC uses four-component operands, with a mask for destinations and selections for sources.
    private static uint[] Dest(int register, uint mask)
    {
        return new[] { 0x00100002u | mask << 4, (uint)register };
    }

    private static uint[] Component(int register, uint component)
    {
        return new[] { 0x0010000Au | component << 4, (uint)register };
    }

    private static uint[] Swizzle(int register, uint x, uint y, uint z, uint w)
    {
        return new[] { 0x00100006u | x << 4 | y << 6 | z << 8 | w << 10, (uint)register };
    }

    private static uint[] Rgb(int register)
    {
        return Swizzle(register, 0, 1, 2, 0);
    }

    private static uint[] NegateRgb(int register)
    {
        return new[] { 0x80100246u, 0x41u, (uint)register };
    }

    private static uint[] ColorComponent(uint channel, bool absolute = false)
    {
        var operand = 0x0020800Au | channel << 4;
        return absolute ? new[] { operand | 0x80000000, 0x81u, 0u, 36u } :
            new[] { operand, 0u, 36u };
    }

    private static uint[] ColorRgb()
    {
        return new[] { 0x00208246u, 0u, 36u };
    }

    private static uint[] Literal(float value)
    {
        return new[] { 0x00004001u, BitConverter.ToUInt32(BitConverter.GetBytes(value), 0) };
    }

    private static uint[] LiteralRgbOne()
    {
        const uint one = 0x3F800000;
        return new[] { 0x00004002u, one, one, one, 0u };
    }

    private static void Add(List<uint> code, uint opcode, params uint[][] operands)
    {
        code.Add(opcode | ((1u + (uint)operands.Sum(values => values.Length)) << 24));
        foreach (var operand in operands)
            code.AddRange(operand);
    }

    private static void WriteVectorParameter(BinaryWriter writer, string property, int offset)
    {
        byte[] name = Encoding.ASCII.GetBytes(property);
        writer.Write(name.Length);
        writer.Write(name);
        while ((writer.BaseStream.Position & 3) != 0)
            writer.Write((byte)0);
        writer.Write(0); // Float.
        writer.Write(1); // Row count.
        writer.Write(4); // Column count.
        writer.Write(0); // No array.
        writer.Write(0); // No matrix transpose.
        writer.Write(offset);
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
