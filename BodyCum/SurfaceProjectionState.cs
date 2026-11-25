using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;

namespace BodyCum
{
    // These are placement coordinates, not replacement skin or garment textures.
    // Keep them when the scene is reloaded so a different animation frame cannot repaint the surface.
    internal sealed class SurfaceProjectionReference
    {
        internal int VertexCount;
        internal int TopologyHash;
        internal string MeshName;
        internal Matrix4x4 MeshToAnchor;
        internal Vector3 Minimum;
        internal Vector3 Maximum;
        internal List<Vector3> Coordinates;
        // Runtime-only settling period. Saved placements are restored as committed coordinates.
        internal int CaptureUntil = -1;

        internal SurfaceProjectionReference Copy() => new SurfaceProjectionReference
        {
            VertexCount = VertexCount,
            TopologyHash = TopologyHash,
            MeshName = MeshName,
            MeshToAnchor = MeshToAnchor,
            Minimum = Minimum,
            Maximum = Maximum,
            Coordinates = Coordinates == null ? null : new List<Vector3>(Coordinates),
            CaptureUntil = CaptureUntil
        };
    }

    internal sealed class SurfaceProjectionState
    {
        private const uint Magic = 0x31534342; // BCS1.
        // Version 1 could capture the loading pose before the native surface finished updating.
        // Ignore that optional record while retaining all original appearance/slot/transform scene values.
        private const byte FormatVersion = 2;
        private const int MaximumReferences = 2048;
        private const int MaximumVertices = 1000000;
        private const int MaximumTotalCoordinates = 4000000;
        private const int MaximumStringBytes = 16384;
        private const int MaximumPayloadBytes = 64 * 1024 * 1024;
        private const int MaximumCompressedBytes = MaximumPayloadBytes + 65536;
        private const float MaximumMagnitude = 1000000f;
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

        internal readonly Dictionary<string, SurfaceProjectionReference> References =
            new Dictionary<string, SurfaceProjectionReference>(StringComparer.Ordinal);

        internal SurfaceProjectionState Copy()
        {
            var copy = new SurfaceProjectionState();
            foreach (var entry in References)
                if (ValidEntry(entry.Key, entry.Value))
                    copy.References.Add(entry.Key, entry.Value.Copy());
            return copy;
        }

        internal byte[] Serialize()
        {
            // An invalid optional placement must never prevent the rest of a Studio scene from saving.
            var entries = new List<KeyValuePair<string, SurfaceProjectionReference>>();
            var coordinates = 0;
            long size = sizeof(int);
            foreach (var entry in References)
            {
                if (!ValidEntry(entry.Key, entry.Value))
                    continue;
                var count = entry.Value.Coordinates?.Count ?? 0;
                var recordSize = 4L + Utf8.GetByteCount(entry.Key) + 4L + Utf8.GetByteCount(entry.Value.MeshName ?? string.Empty) +
                                 8L + 16L * sizeof(float) + 6L * sizeof(float) + 4L + count * 12L;
                if (entries.Count == MaximumReferences || coordinates > MaximumTotalCoordinates - count ||
                    size > MaximumPayloadBytes - recordSize)
                    continue;
                entries.Add(entry);
                coordinates += count;
                size += recordSize;
            }
            if (entries.Count == 0)
                return null;
            entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));

            byte[] payload;
            using (var raw = new MemoryStream((int)size))
            {
                using (var writer = new BinaryWriter(raw))
                {
                    writer.Write(entries.Count);
                    foreach (var entry in entries)
                    {
                        var reference = entry.Value;
                        WriteString(writer, entry.Key);
                        WriteString(writer, reference.MeshName ?? string.Empty);
                        writer.Write(reference.VertexCount);
                        writer.Write(reference.TopologyHash);
                        for (var row = 0; row < 4; row++)
                            for (var column = 0; column < 4; column++)
                                writer.Write(reference.MeshToAnchor[row, column]);
                        WriteVector(writer, reference.Minimum);
                        WriteVector(writer, reference.Maximum);
                        writer.Write(reference.Coordinates?.Count ?? -1);
                        if (reference.Coordinates != null)
                            foreach (var coordinate in reference.Coordinates)
                                WriteVector(writer, coordinate);
                    }
                }
                payload = raw.ToArray();
            }

            using (var output = new MemoryStream())
            {
                // The uncompressed length bounds allocation before DeflateStream sees any data.
                using (var writer = new BinaryWriter(output))
                {
                    writer.Write(Magic);
                    writer.Write(FormatVersion);
                    writer.Write(payload.Length);
                    writer.Write(Checksum(payload));
                    writer.Flush();
                    using (var compressed = new DeflateStream(output, CompressionMode.Compress))
                        compressed.Write(payload, 0, payload.Length);
                }
                return output.ToArray();
            }
        }

        internal static SurfaceProjectionState Deserialize(byte[] data)
        {
            if (data == null || data.Length < 14 || data.Length > MaximumCompressedBytes)
                return null;
            try
            {
                byte[] payload;
                int length;
                uint checksum;
                using (var input = new MemoryStream(data, false))
                using (var reader = new BinaryReader(input))
                {
                    if (reader.ReadUInt32() != Magic || reader.ReadByte() != FormatVersion)
                        return null;
                    length = reader.ReadInt32();
                    checksum = reader.ReadUInt32();
                    if (length < sizeof(int) || length > MaximumPayloadBytes)
                        return null;
                    using (var compressed = new DeflateStream(input, CompressionMode.Decompress))
                    using (var raw = new MemoryStream(Math.Min(length, 65536)))
                    {
                        var buffer = new byte[16384];
                        int read;
                        while ((read = compressed.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (raw.Length > length - read)
                                return null;
                            raw.Write(buffer, 0, read);
                        }
                        if (raw.Length != length)
                            return null;
                        payload = raw.ToArray();
                    }
                }
                if (Checksum(payload) != checksum)
                    return null;

                var state = new SurfaceProjectionState();
                using (var raw = new MemoryStream(payload, false))
                using (var reader = new BinaryReader(raw))
                {
                    var count = reader.ReadInt32();
                    if (count < 1 || count > MaximumReferences)
                        return null;
                    var total = 0;
                    for (var index = 0; index < count; index++)
                    {
                        var key = ReadString(reader);
                        var reference = new SurfaceProjectionReference
                        {
                            MeshName = ReadString(reader),
                            VertexCount = reader.ReadInt32(),
                            TopologyHash = reader.ReadInt32()
                        };
                        if (reference.VertexCount < 1 || reference.VertexCount > MaximumVertices)
                            return null;
                        for (var row = 0; row < 4; row++)
                            for (var column = 0; column < 4; column++)
                                reference.MeshToAnchor[row, column] = reader.ReadSingle();
                        reference.Minimum = ReadVector(reader);
                        reference.Maximum = ReadVector(reader);
                        var coordinates = reader.ReadInt32();
                        if (coordinates != -1)
                        {
                            if (coordinates != reference.VertexCount || total > MaximumTotalCoordinates - coordinates ||
                                raw.Length - raw.Position < coordinates * 12L)
                                return null;
                            total += coordinates;
                            reference.Coordinates = new List<Vector3>(coordinates);
                            for (var vertex = 0; vertex < coordinates; vertex++)
                                reference.Coordinates.Add(ReadVector(reader));
                        }
                        if (!ValidEntry(key, reference) || state.References.ContainsKey(key))
                            return null;
                        state.References.Add(key, reference);
                    }
                    if (raw.Position != raw.Length)
                        return null;
                }
                return state;
            }
            catch (IOException) { return null; }
            catch (ArgumentException) { return null; }
            catch (OverflowException) { return null; }
            // The game's older Mono profile does not expose this newer exception type.
            catch (Exception error) when (error.GetType().FullName == "System.IO.InvalidDataException") { return null; }
        }

        private static bool ValidEntry(string key, SurfaceProjectionReference reference)
        {
            if (string.IsNullOrEmpty(key) || !ValidString(key) || reference == null || !ValidString(reference.MeshName ?? string.Empty) ||
                reference.VertexCount < 1 || reference.VertexCount > MaximumVertices ||
                !ValidVector(reference.Minimum) || !ValidVector(reference.Maximum) ||
                reference.Minimum.x > reference.Maximum.x || reference.Minimum.y > reference.Maximum.y ||
                reference.Minimum.z > reference.Maximum.z)
                return false;
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                    if (!ValidFloat(reference.MeshToAnchor[row, column]))
                        return false;
            if (reference.Coordinates != null)
            {
                if (reference.Coordinates.Count != reference.VertexCount)
                    return false;
                foreach (var coordinate in reference.Coordinates)
                    if (!ValidVector(coordinate))
                        return false;
            }
            return true;
        }

        private static bool ValidString(string value)
        {
            if (value.Length > MaximumStringBytes)
                return false;
            try { return Utf8.GetByteCount(value) <= MaximumStringBytes; }
            catch (EncoderFallbackException) { return false; }
        }

        private static bool ValidFloat(float value) => !float.IsNaN(value) && !float.IsInfinity(value) &&
                                                        Math.Abs(value) <= MaximumMagnitude;

        private static bool ValidVector(Vector3 value) => ValidFloat(value.x) && ValidFloat(value.y) && ValidFloat(value.z);

        private static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = Utf8.GetBytes(value);
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static string ReadString(BinaryReader reader)
        {
            var length = reader.ReadInt32();
            if (length < 0 || length > MaximumStringBytes || reader.BaseStream.Length - reader.BaseStream.Position < length)
                throw new IOException("Invalid surface placement key.");
            return Utf8.GetString(reader.ReadBytes(length));
        }

        private static void WriteVector(BinaryWriter writer, Vector3 value)
        {
            writer.Write(value.x);
            writer.Write(value.y);
            writer.Write(value.z);
        }

        private static Vector3 ReadVector(BinaryReader reader) => new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

        private static uint Checksum(byte[] bytes)
        {
            unchecked
            {
                var hash = 2166136261u;
                foreach (var value in bytes)
                    hash = (hash ^ value) * 16777619u;
                return hash;
            }
        }
    }
}
