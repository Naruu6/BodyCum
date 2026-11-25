using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

internal static class Dxbc
{
    private static readonly int[] Md5Shifts =
    {
        7, 12, 17, 22, 5, 9, 14, 20, 4, 11, 16, 23, 6, 10, 15, 21
    };

    private static readonly uint[] Md5Constants = Enumerable.Range(1, 64)
        .Select(index => (uint)(4294967296.0 * Math.Abs(Math.Sin(index)))).ToArray();

    [ComImport, Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IBlob
    {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }

    [DllImport("d3dcompiler_47.dll")]
    private static extern int D3DCompile(byte[] source, UIntPtr length,
        [MarshalAs(UnmanagedType.LPStr)] string sourceName, IntPtr defines, IntPtr include,
        [MarshalAs(UnmanagedType.LPStr)] string entryPoint, [MarshalAs(UnmanagedType.LPStr)] string target,
        uint flags, uint effectFlags, out IBlob code, out IBlob messages);

    [DllImport("d3dcompiler_47.dll")]
    private static extern int D3DDisassemble(byte[] source, UIntPtr length,
        uint flags, IntPtr comment, out IBlob text);

    internal static byte[] Compile(string source, string profile = "ps_5_0", uint flags = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(source);
        IBlob code, messages;
        var result = D3DCompile(bytes, (UIntPtr)bytes.Length, null, IntPtr.Zero, IntPtr.Zero,
            "main", profile, flags, 0, out code, out messages);
        try
        {
            if (result < 0)
                throw new InvalidDataException(messages == null ? "Cannot compile shader" : Encoding.UTF8.GetString(Read(messages)));
            return Read(code);
        }
        finally
        {
            if (code != null)
                Marshal.ReleaseComObject(code);
            if (messages != null)
                Marshal.ReleaseComObject(messages);
        }
    }

    internal static string Disassemble(byte[] bytes)
    {
        IBlob listing;
        Marshal.ThrowExceptionForHR(D3DDisassemble(bytes, (UIntPtr)bytes.Length, 0, IntPtr.Zero, out listing));
        try
        {
            return Encoding.ASCII.GetString(Read(listing)).TrimEnd('\0');
        }
        finally
        {
            Marshal.ReleaseComObject(listing);
        }
    }

    private static byte[] Read(IBlob blob)
    {
        byte[] bytes = new byte[checked((int)blob.GetBufferSize().ToUInt64())];
        Marshal.Copy(blob.GetBufferPointer(), bytes, 0, bytes.Length);
        return bytes;
    }

    // DXBC uses the MD5 compression function with Direct3D-specific final padding.
    internal static byte[] Checksum(byte[] bytecode)
    {
        var bodyLength = bytecode.Length - 20;
        var prefixLength = bodyLength & ~63;
        var residueLength = bodyLength - prefixLength;
        var padded = new byte[prefixLength + (residueLength >= 56 ? 128 : 64)];
        var bitLength = checked((uint)bodyLength * 8);
        var lastBlock = padded.Length - 64;
        Buffer.BlockCopy(bytecode, 20, padded, 0, prefixLength);
        if (residueLength >= 56)
        {
            Buffer.BlockCopy(bytecode, 20 + prefixLength, padded, prefixLength, residueLength);
            padded[prefixLength + residueLength] = 0x80;
        }
        else
        {
            Buffer.BlockCopy(bytecode, 20 + prefixLength, padded, lastBlock + 4, residueLength);
            padded[lastBlock + 4 + residueLength] = 0x80;
        }
        WriteUInt(padded, lastBlock, bitLength);
        WriteUInt(padded, padded.Length - 4, (bitLength >> 2) | 1);

        return Md5Blocks(padded);
    }

    // MD5 block compression as defined by RFC 1321, without applying standard MD5 padding.
    private static byte[] Md5Blocks(byte[] padded)
    {
        uint a0 = 0x67452301;
        uint b0 = 0xefcdab89;
        uint c0 = 0x98badcfe;
        uint d0 = 0x10325476;
        for (var offset = 0; offset < padded.Length; offset += 64)
        {
            uint a = a0;
            uint b = b0;
            uint c = c0;
            uint d = d0;
            for (int i = 0; i < 64; i++)
            {
                uint mix;
                int index;
                if (i < 16)
                {
                    mix = (b & c) | (~b & d);
                    index = i;
                }
                else if (i < 32)
                {
                    mix = (d & b) | (~d & c);
                    index = (5 * i + 1) & 15;
                }
                else if (i < 48)
                {
                    mix = b ^ c ^ d;
                    index = (3 * i + 5) & 15;
                }
                else
                {
                    mix = c ^ (b | ~d);
                    index = (7 * i) & 15;
                }

                uint sum = unchecked(a + mix + Md5Constants[i] + BitConverter.ToUInt32(padded, offset + index * 4));
                int shift = Md5Shifts[(i / 16) * 4 + i % 4];
                a = d;
                d = c;
                c = b;
                b = unchecked(b + ((sum << shift) | (sum >> (32 - shift))));
            }
            a0 = unchecked(a0 + a);
            b0 = unchecked(b0 + b);
            c0 = unchecked(c0 + c);
            d0 = unchecked(d0 + d);
        }

        return new[] { a0, b0, c0, d0 }.SelectMany(BitConverter.GetBytes).ToArray();
    }

    internal static int FindChunk(byte[] bytecode, string name)
    {
        for (int i = 0; i < ReadInt(bytecode, 28); i++)
        {
            int offset = ReadInt(bytecode, 32 + 4 * i);
            if (Encoding.ASCII.GetString(bytecode, offset, 4) == name)
                return offset;
        }
        return -1;
    }

    internal static byte[] EditProgram(byte[] bytecode, Func<uint[],uint[]> edit)
    {
        if (Encoding.ASCII.GetString(bytecode, 0, 4) != "DXBC" ||
                ReadInt(bytecode, 24) != bytecode.Length ||
                !Checksum(bytecode).SequenceEqual(bytecode.Skip(4).Take(16)))
            throw new InvalidDataException("Invalid DXBC data or checksum.");
        var programOffset = FindChunk(bytecode, "SHEX");
        if (programOffset < 0)
            programOffset = FindChunk(bytecode, "SHDR");
        if (programOffset < 0)
            throw new InvalidDataException("DXBC program chunk is missing.");
        var oldLength = ReadInt(bytecode, programOffset + 4);
        var tokens = new uint[oldLength / 4];
        Buffer.BlockCopy(bytecode, programOffset + 8, tokens, 0, oldLength);

        var edited = edit(tokens);
        edited[1] = (uint)edited.Length;
        var newLength = edited.Length * 4;
        var prefixLength = programOffset + 8;
        var oldEnd = prefixLength + oldLength;
        var newEnd = prefixLength + newLength;
        var replacement = new byte[bytecode.Length + newLength - oldLength];
        Buffer.BlockCopy(bytecode, 0, replacement, 0, prefixLength);
        Buffer.BlockCopy(edited, 0, replacement, prefixLength, newLength);
        Buffer.BlockCopy(bytecode, oldEnd, replacement, newEnd, bytecode.Length - oldEnd);
        WriteUInt(replacement, programOffset + 4, (uint)newLength);
        WriteUInt(replacement, 24, (uint)replacement.Length);
        for (int i = 0; i < ReadInt(bytecode, 28); i++)
        {
            int location = 32 + 4 * i;
            int offset = ReadInt(bytecode, location);
            if (offset > programOffset)
                WriteUInt(replacement, location, (uint)(offset + newLength - oldLength));
        }
        Buffer.BlockCopy(Checksum(replacement), 0, replacement, 4, 16);
        return replacement;
    }

    internal static int ReadInt(byte[] bytes, int offset)
    {
        return BitConverter.ToInt32(bytes, offset);
    }

    private static void WriteUInt(byte[] bytes, int offset, uint value)
    {
        Buffer.BlockCopy(BitConverter.GetBytes(value), 0, bytes, offset, 4);
    }
}
