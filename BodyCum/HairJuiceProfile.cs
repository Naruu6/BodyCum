using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BodyCum
{
    internal sealed class HairJuiceProfile
    {
        internal const int MinimumSize = 20;
        internal const int DefaultSize = 100;
        internal const int MaximumSize = 250;

        internal int Level;
        internal int Horizontal;
        internal int Vertical;
        internal int Size = DefaultSize;

        internal readonly Dictionary<int, string> Slots = new Dictionary<int, string>();
        internal HashSet<Transform> RestoredAccessoryRoots;

        internal bool HasValues => Level != 0 || Horizontal != 0 || Vertical != 0 || Size != DefaultSize || Slots.Count != 0;

        internal HairJuiceProfile Copy()
        {
            var copy = new HairJuiceProfile { Level = Level, Horizontal = Horizontal, Vertical = Vertical, Size = Size };
            foreach (var entry in Slots)
                copy.Slots.Add(entry.Key, entry.Value);
            return copy;
        }

        internal byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)1);
                writer.Write((byte)Level);
                writer.Write((sbyte)Horizontal);
                writer.Write((sbyte)Vertical);
                writer.Write((byte)Size);
                writer.Write(Slots.Count);
                foreach (var entry in Slots)
                {
                    writer.Write(entry.Key);
                    writer.Write(entry.Value);
                }
                return stream.ToArray();
            }
        }

        internal static HairJuiceProfile Deserialize(byte[] data)
        {
            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadByte() != 1)
                        return null;
                    var profile = new HairJuiceProfile
                    {
                        Level = reader.ReadByte(), Horizontal = reader.ReadSByte(), Vertical = reader.ReadSByte(), Size = reader.ReadByte()
                    };
                    var count = reader.ReadInt32();
                    if (profile.Level > 2 || Math.Abs(profile.Horizontal) > 100 || Math.Abs(profile.Vertical) > 100 ||
                        profile.Size < MinimumSize || profile.Size > MaximumSize || count < 0 || count > (stream.Length - stream.Position) / 6)
                        return null;
                    for (var i = 0; i < count; i++)
                    {
                        var slot = reader.ReadInt32();
                        var asset = reader.ReadString();
                        if (slot < 0 || asset.Length == 0 || profile.Slots.ContainsKey(slot))
                            return null;
                        profile.Slots.Add(slot, asset);
                    }
                    return stream.Position == stream.Length ? profile : null;
                }
            }
            catch (IOException) { return null; }
            catch (FormatException) { return null; }
        }
    }
}
