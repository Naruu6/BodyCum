using System;
using System.Collections.Generic;
using System.IO;

internal static class AbdomenJuiceMask
{
    internal const uint TemporaryCount = 5;

    // SM4/5 opcodes. The checked pixel variants all carry mesh UV0 in v1.xy.
    private const uint Add = 0;
    private const uint Else = 18;
    private const uint EndIf = 21;
    private const uint IfNonzero = 31 | 0x40000;
    private const uint Lt = 49;
    private const uint Mad = 50;
    private const uint Min = 51;
    private const uint Max = 52;
    private const uint Mov = 54;
    private const uint Movc = 55;
    private const uint Mul = 56;
    private const uint Sample = 69;
    private const uint Saturate = 0x2000;

    internal struct NativeSample
    {
        internal int Start;
        internal int End;
        internal uint Register;
        internal uint Red;
        internal uint Green;
    }

    internal static NativeSample FindNativeSample(uint[] program, uint texture, uint sampler, int before)
    {
        var found = new NativeSample();
        for (var cursor = 2; cursor < before;)
        {
            var opcode = program[cursor];
            var length = (int)((opcode >> 24) & 127);
            if (length == 0 || cursor + length > before)
                throw new InvalidDataException("Invalid instruction before the native Juices coverage.");

            var end = cursor + length;
            // Resource and sampler are the last two operands, with one static register index each.
            // This also works for the KKUTS sample_indexable dimension extensions preceding them.
            if ((opcode & 0x7ff) == Sample && length >= 9 &&
                (program[end - 4] & 0x7ffff000) == 0x00107000 && program[end - 3] == texture &&
                (program[end - 2] & 0x7ffff000) == 0x00106000 && program[end - 1] == sampler)
            {
                if (found.End != 0 || (program[end - 8] & 0x7ffff000) != 0x00100000)
                    throw new InvalidDataException("Unexpected native Juices texture sample.");
                var destinationMask = (program[end - 8] >> 4) & 15;
                var textureSwizzle = program[end - 4] >> 4;
                found.Start = cursor;
                found.End = end;
                found.Register = program[end - 7];
                found.Red = Channel(destinationMask, textureSwizzle, 0);
                found.Green = Channel(destinationMask, textureSwizzle, 1);
            }
            cursor = end;
        }
        if (found.End == 0)
            throw new InvalidDataException("Missing native Juices texture sample.");
        return found;
    }

    private static uint Channel(uint destinationMask, uint textureSwizzle, uint color)
    {
        for (uint channel = 0; channel < 4; channel++)
            if ((destinationMask & (1u << (int)channel)) != 0 && ((textureSwizzle >> (2 * (int)channel)) & 3) == color)
                return channel;
        throw new InvalidDataException("The native Juices sample did not preserve both pattern channels.");
    }

    internal static void SaveNativeSample(List<uint> code, NativeSample sample, uint destination)
    {
        var swizzle = 0x00100006u | sample.Red << 4 | sample.Green << 6;
        Emit(code, Mov, Destination(destination, 3), new[] { swizzle, sample.Register });
    }

    // Locate the two ST transformations from _LiquidTiling. Re-sample the pattern
    // and normal together, then crossfade their *values*. Fading their coordinates
    // instead would magnify the drops in the boundary ring.
    internal static int EmitBodyPattern(List<uint> code, uint[] program, NativeSample sample,
        uint densityRow, uint saved)
    {
        var starts = new Queue<int>();
        for (int i = 2; i < sample.Start; i += (int)((program[i] >> 24) & 127))
        {
            starts.Enqueue(i);
            if (starts.Count > 3) starts.Dequeue();
        }
        var mad = starts.ToArray();
        if (mad.Length != 3 || (program[mad[0]] & 0x7ff) != Mad ||
            (program[mad[1]] & 0x7ff) != Mad || (program[mad[2]] & 0x7ff) != Mad ||
            (program[mad[0] + 3] & 0x7ffff000) != 0x00101000 || program[mad[0] + 4] != 1 ||
            program[mad[1] + 4] != program[mad[0] + 2] || program[mad[2] + 4] != program[mad[0] + 2])
            throw new InvalidDataException("Cannot identify the shared liquid UV and its two ST transforms.");

        int normalStart = sample.End;
        int normalEnd = normalStart + (int)((program[normalStart] >> 24) & 127);
        uint texture = program[sample.End - 3];
        uint expectedNormal = texture == 3 ? 2u : texture + 1u;
        if ((program[normalStart] & 0x7ff) != Sample || program[normalEnd - 3] != expectedNormal ||
            program[normalEnd - 7] == sample.Register)
            throw new InvalidDataException("Unexpected native liquid normal sampling operands.");

        // Save the legacy samples for the optional Belly layer before altering the breast samples.
        code.AddRange(new ArraySegment<uint>(program, 0, sample.Start));
        SaveShiftedSample(code, program, sample, densityRow, saved);
        code.AddRange(new ArraySegment<uint>(program, sample.Start, sample.End - sample.Start));
        SaveNativeSample(code, sample, saved);
        code.AddRange(new ArraySegment<uint>(program, normalStart, normalEnd - normalStart));

        var gainRow = densityRow + 1;
        var delta = saved + 1;
        var uv = saved + 2;
        var oldPattern = saved + 3;
        var oldNormal = saved + 4;
        Emit(code, Add, Destination(uv, 4), CBufferComponent(gainRow, 0), CBufferComponent(gainRow, 1));
        Emit(code, IfNonzero, Register(uv, 2));
        Emit(code, Mov, Destination(oldPattern, 15), new[] { 0x00100e46u, sample.Register });
        Emit(code, Mov, Destination(oldNormal, 15), new[] { 0x00100e46u, program[normalEnd - 7] });

        // Keep this in sync with BustPatternWarp.hlsl. Full-strength through .7
        // normalized radius, then smoothstep to zero at the boundary.
        var inputX = new[] { 0x0010100au, 1u };
        var inputY = new[] { 0x0010101au, 1u };
        var inputXY = new[] { 0x00101046u, 1u };
        var xy = new[] { 0x00100046u, delta };
        var uvXY = new[] { 0x00100046u, uv };
        Emit(code, Lt, Destination(delta, 4), inputX, Literal(.1566f));
        Emit(code, Movc, Destination(delta, 1), Register(delta, 2), Literal(-.1107f), Literal(-.2025f));
        Emit(code, Add, Destination(delta, 1), inputX, Register(delta, 0));
        Emit(code, Add, Destination(delta, 2), inputY, Literal(-.8453f));
        Emit(code, Mul, Destination(uv, 3), xy, Vector(1f/.0455f, 1f/.053f, 0f, 0f));
        Emit(code, 15, Destination(uv, 4), uvXY, uvXY); // dp2
        var outerGain = new[] { 0x00208046u, 0u, gainRow };
        var innerGain = new[] { 0x002080e6u, 0u, gainRow }; // cb0[a].zwxx
        Emit(code, Max, Destination(uv, 8), Register(uv, 2), Literal(.24f * .24f));
        Emit(code, 68, Destination(uv, 8), Register(uv, 3)); // rsq, finite even at the centre
        Emit(code, Mad | Saturate, Destination(delta, 8), Register(uv, 3), Literal(-.24f), Literal(1f));
        Emit(code, Mul, Destination(delta, 8), Register(delta, 3), Register(delta, 3));
        Emit(code, Mul, Destination(delta, 8), Register(delta, 3), Literal(1f / (.70f * .70f)));
        Emit(code, Add, Destination(uv, 3), outerGain, new[] { 0x802080e6u, 0x41u, 0u, gainRow });
        Emit(code, Mad, Destination(uv, 3), Register(delta, 3), uvXY, innerGain);
        Emit(code, Mul, Destination(delta, 3), xy, uvXY);
        Emit(code, Mad | Saturate, Destination(uv, 4), Register(uv, 2), Literal(-1f/.51f), Literal(1f/.51f));
        Emit(code, Mad, Destination(uv, 8), Register(uv, 2), Literal(-2f), Literal(3f));
        Emit(code, Mul, Destination(uv, 4), Register(uv, 2), Register(uv, 2));
        Emit(code, Mul, Destination(uv, 4), Register(uv, 2), Register(uv, 3));
        Emit(code, Add, Destination(uv, 3), xy, inputXY);

        code.AddRange(new ArraySegment<uint>(program, mad[0], 3));
        code.Add(program[mad[0] + 3] - 0x1000u); // Preserve the source swizzle, read our temporary.
        code.Add(uv);
        code.AddRange(new ArraySegment<uint>(program, mad[0] + 5, normalEnd - mad[0] - 5));
        BlendBustSample(code, program, sample.End, oldPattern, uv, delta);
        BlendBustSample(code, program, normalEnd, oldNormal, uv, delta);
        Emit(code, EndIf);
        return normalEnd;
    }

    private static void BlendBustSample(List<uint> code, uint[] program, int sampleEnd, uint original, uint uv, uint work)
    {
        var mask = (program[sampleEnd - 8] >> 4) & 15;
        var target = program[sampleEnd - 7];
        Emit(code, Add, Destination(work, mask), new[] { 0x00100e46u, target },
            new[] { 0x80100e46u, 0x41u, original });
        Emit(code, Mad, Destination(target, mask), new[] { 0x00100e46u, work },
            Register(uv, 2), new[] { 0x00100e46u, original });
    }
    internal static void SaveShiftedSample(List<uint> code, uint[] program, NativeSample sample,
        uint densityRow, uint destination)
    {
        var coordinates = destination + 1;
        var operands = sample.End - 8;
        Emit(code, IfNonzero, CBufferComponent(densityRow, 1));
        // Shift the original UVs without rescaling the drops or changing sampler bindings.
        Emit(code, Add, Destination(coordinates, 3), new[] { program[operands + 2], program[operands + 3] },
            Vector(-.084f, -.096f, 0f, 0f));

        // Copy the original sample opcode and any resource-dimension extensions. Store R/G in .zw.
        for (var i = sample.Start; i < operands; i++)
            code.Add(program[i]);
        code.AddRange(Destination(destination, 12));
        code.Add(0x00100046u); // Temporary coordinates .xyxx.
        code.Add(coordinates);
        code.Add((program[operands + 4] & ~0xff0u) | 0x4e0u); // Texture swizzle .zwxy.
        code.Add(program[operands + 5]);
        code.Add(program[operands + 6]);
        code.Add(program[operands + 7]);
        Emit(code, EndIf);
    }

    internal static uint[] ExtendCoverage(List<uint> code, uint densityRow, uint sample)
    {
        var gate = sample + 1;
        var level = CBufferComponent(densityRow, 1);
        var coverage = Register(sample, 2);
        Emit(code, IfNonzero, level);

        // Offset drips fill gaps in the original pattern. Merge each tier independently, then
        // gently lift its faint marks without flattening the solid centers or widening its support.
        Emit(code, Mul, Destination(sample, 12), new[] { 0x00100ee6u, sample }, Literal(.91f)); // .zwzw
        Emit(code, Max, Destination(sample, 3), new[] { 0x00100046u, sample }, new[] { 0x00100ee6u, sample });

        var secondTier = Register(sample, 2);
        Emit(code, Lt, Destination(sample, 4), Literal(1.5f), level);
        Emit(code, Max, Destination(sample, 8), Register(sample, 0), Register(sample, 1));
        Emit(code, Movc, Destination(sample, 8), secondTier, Register(sample, 3), Register(sample, 0));
        Emit(code, Mad, Destination(gate, 4), Register(sample, 3), Literal(-.38f), Literal(1.38f));
        Emit(code, Mul, Destination(sample, 8), Register(sample, 3), Register(gate, 2));

        // Stock front abdomen island, verified against the front waist anchor and native masks.
        const float sideFeather = .009f;
        const float verticalFeather = .014f;
        var bodyUv = new[] { 0x00101446u, 1u }; // v1.xyxy
        Emit(code, Mad, Destination(gate, 15), bodyUv,
            Vector(1f / sideFeather, 1f / verticalFeather, -1f / sideFeather, -1f / verticalFeather),
            Vector(-.092f / sideFeather, -.700f / verticalFeather, .221f / sideFeather, .813f / verticalFeather));

        // x2 reaches a little farther on each side. Keep the full-strength interval and V taper.
        const float secondTierFeather = .019f;
        Emit(code, IfNonzero, secondTier);
        Emit(code, Mad, Destination(gate, 5), bodyUv,
            Vector(1f / secondTierFeather, 0f, -1f / secondTierFeather, 0f),
            Vector(-.082f / secondTierFeather, 0f, .231f / secondTierFeather, 0f));
        Emit(code, EndIf);
        Emit(code, Min, Destination(gate, 3), new[] { 0x00100046u, gate }, new[] { 0x00100ee6u, gate });
        Emit(code, Min | Saturate, Destination(gate, 4), Register(gate, 0), Register(gate, 1));
        Emit(code, Mul, Destination(gate, 8), Register(gate, 2), Register(gate, 2));
        Emit(code, Mad, Destination(gate, 4), Register(gate, 2), Literal(-2f), Literal(3f));
        Emit(code, Mul, Destination(gate, 4), Register(gate, 2), Register(gate, 3));

        Emit(code, IfNonzero, secondTier);
        ExtendUnderarmCoverage(code, gate, sample + 2);
        Emit(code, EndIf);

        // The posed torso's side crosses the UV seam. Treat the two islands separately:
        // low folded U on the front island meets high folded U on the back island.
        ExtendFlankCoverage(code, gate, sample + 2, secondTier);

        // Retain the literal native coverage when the feather is zero. Only arithmetic is
        // inside this per-pixel branch; the texture samples above retain their derivatives.
        Emit(code, Mov, Destination(sample, 4), Register(0, 3));
        Emit(code, IfNonzero, Register(gate, 2));
        Emit(code, Mul, Destination(sample, 8), Register(sample, 3), Register(gate, 2));
        Emit(code, Max, Destination(sample, 4), Register(0, 3), Register(sample, 3));
        Emit(code, EndIf);
        Emit(code, Else);
        Emit(code, Mov, Destination(sample, 4), Register(0, 3));
        Emit(code, EndIf);
        return coverage;
    }

    private static void ExtendUnderarmCoverage(List<uint> code, uint gate, uint corridor)
    {
        const float sideFeather = .006f;
        const float bottomFeather = .020f;
        const float topFeather = .024f;
        var u = new[] { 0x0010100au, 1u }; // v1.x
        var v = new[] { 0x0010101au, 1u }; // v1.y
        var reflected = Register(corridor, 0);
        var amount = Register(corridor, 1);
        var edge = Register(corridor, 2);
        var pinch = Register(corridor, 3);

        // Fold the two front-side corridors around the torso's UV center.
        Emit(code, Add, Destination(corridor, 1), new[] { u[0] | 0x80000000u, 0x41u, u[1] }, Literal(.3132f));
        Emit(code, Min, Destination(corridor, 1), u, reflected);

        // Narrow the inner boundary near the bust, with smooth transitions at both ends.
        Emit(code, Mad, Destination(corridor, 8), v, Literal(1f / .018f), Literal(-.815f / .018f));
        Emit(code, Mad, Destination(corridor, 4), v, Literal(-1f / .017f), Literal(.878f / .017f));
        Emit(code, Min | Saturate, Destination(corridor, 8), pinch, edge);
        Emit(code, Mul, Destination(corridor, 4), pinch, pinch);
        Emit(code, Mad, Destination(corridor, 8), pinch, Literal(-2f), Literal(3f));
        Emit(code, Mul, Destination(corridor, 8), pinch, edge);
        Emit(code, Mad, Destination(corridor, 8), pinch, Literal((.1045f - .124f) / sideFeather), Literal(.124f / sideFeather));

        Emit(code, Mad, Destination(corridor, 2), reflected, Literal(1f / sideFeather), Literal(-.090f / sideFeather));
        Emit(code, Mad, Destination(corridor, 4), reflected, Literal(-1f / sideFeather), pinch);
        Emit(code, Min, Destination(corridor, 2), amount, edge);
        Emit(code, Mad, Destination(corridor, 4), v, Literal(1f / bottomFeather), Literal(-.790f / bottomFeather));
        Emit(code, Min, Destination(corridor, 2), amount, edge);
        Emit(code, Mad, Destination(corridor, 4), v, Literal(-1f / topFeather), Literal(.899f / topFeather));
        Emit(code, Min | Saturate, Destination(corridor, 2), amount, edge);

        // Keep the base Belly gate verbatim outside the corridor, including on the back.
        Emit(code, IfNonzero, amount);
        Emit(code, Mul, Destination(corridor, 4), amount, amount);
        Emit(code, Mad, Destination(corridor, 2), amount, Literal(-2f), Literal(3f));
        Emit(code, Mul, Destination(corridor, 2), amount, edge);
        Emit(code, Mul, Destination(corridor, 2), amount, Literal(.85f));
        Emit(code, Max, Destination(gate, 4), Register(gate, 2), amount);
        Emit(code, EndIf);
    }

    private static void ExtendFlankCoverage(List<uint> code, uint gate, uint corridor, uint[] secondTier)
    {
        var u = new[] { 0x0010100au, 1u }; // v1.x
        var negativeU = new[] { u[0] | 0x80000000u, 0x41u, u[1] };
        var foldedU = Register(corridor, 0);

        // Both tiers reach the UV seam. x1 uses the existing first-tier pattern at a lower weight.
        Emit(code, Add, Destination(corridor, 1), negativeU, Literal(.3132f));
        Emit(code, Min, Destination(corridor, 1), u, foldedU);
        AddFlankBand(code, gate, corridor, secondTier, .050f, .111f, .039f, .104f, .009f);

        // The tested rear midpoint ends below folded U .462. Fade the rear-facing edge more
        // gradually so the drips stay concentrated on the side instead of wrapping onto the back.
        Emit(code, Add, Destination(corridor, 1), negativeU, Literal(.8948f));
        Emit(code, Max, Destination(corridor, 1), u, foldedU);
        AddFlankBand(code, gate, corridor, secondTier, .491f, .552f, .486f, .548f, .022f);
    }

    private static void AddFlankBand(List<uint> code, uint gate, uint corridor, uint[] secondTier,
        float lowerLeft, float lowerRight, float upperLeft, float upperRight, float leftFeather)
    {
        const float rightFeather = .009f;
        const float verticalFeather = .016f;
        const float startBend = .785f;
        const float bendHeight = .067f;
        var v = new[] { 0x0010101au, 1u }; // v1.y
        var u = Register(corridor, 0);
        var band = Register(corridor, 1);
        var edge = Register(corridor, 2);
        var bend = Register(corridor, 3);

        // Move each edge with height and feather both the horizontal and vertical boundaries.
        Emit(code, Mad | Saturate, Destination(corridor, 8), v, Literal(1f / bendHeight), Literal(-startBend / bendHeight));
        Emit(code, Mad, Destination(corridor, 4), bend, Literal((lowerLeft - upperLeft) / leftFeather),
            Literal(-lowerLeft / leftFeather));
        Emit(code, Mad, Destination(corridor, 2), u, Literal(1f / leftFeather), edge);
        Emit(code, Mad, Destination(corridor, 4), bend, Literal((upperRight - lowerRight) / rightFeather),
            Literal(lowerRight / rightFeather));
        Emit(code, Mad, Destination(corridor, 4), u, Literal(-1f / rightFeather), edge);
        Emit(code, Min, Destination(corridor, 2), band, edge);
        Emit(code, Mad, Destination(corridor, 4), v, Literal(1f / verticalFeather), Literal(-.696f / verticalFeather));
        Emit(code, Min, Destination(corridor, 2), band, edge);
        Emit(code, Mad, Destination(corridor, 4), v, Literal(-1f / verticalFeather), Literal(.904f / verticalFeather));
        Emit(code, Min | Saturate, Destination(corridor, 2), band, edge);

        Emit(code, Mul, Destination(corridor, 4), band, band);
        Emit(code, Mad, Destination(corridor, 2), band, Literal(-2f), Literal(3f));
        Emit(code, Mul, Destination(corridor, 2), band, edge);
        Emit(code, Movc, Destination(corridor, 8), secondTier, Literal(.96f), Literal(.64f));
        Emit(code, Mul, Destination(corridor, 2), band, bend);
        Emit(code, Max, Destination(gate, 4), Register(gate, 2), band);
    }

    private static uint[] Destination(uint register, uint mask)
    {
        return new[] { 0x00100002u | mask << 4, register };
    }

    private static uint[] Register(uint register, uint component)
    {
        return new[] { 0x0010000Au | component << 4, register };
    }

    private static uint[] CBufferComponent(uint row, uint component)
    {
        return new[] { 0x0020800Au | component << 4, 0u, row };
    }

    private static uint[] Literal(float value)
    {
        return new[] { 0x00004001u, Bits(value) };
    }

    private static uint[] Vector(float x, float y, float z, float w)
    {
        return new[] { 0x00004002u, Bits(x), Bits(y), Bits(z), Bits(w) };
    }

    private static uint Bits(float value) { return BitConverter.ToUInt32(BitConverter.GetBytes(value), 0); }

    private static void Emit(List<uint> code, uint opcode, params uint[][] operands)
    {
        var size = 1u;
        foreach (var operand in operands)
            size += (uint)operand.Length;
        code.Add(opcode | size << 24);
        foreach (var operand in operands)
            code.AddRange(operand);
    }
}
