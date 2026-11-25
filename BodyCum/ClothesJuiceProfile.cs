using System;
using System.Collections.Generic;
using System.IO;

namespace BodyCum
{
    internal sealed class ClothesJuiceProfile
    {
        internal const int MinimumSize = HairJuiceProfile.MinimumSize;
        internal const int DefaultSize = HairJuiceProfile.DefaultSize;
        internal const int MaximumSize = HairJuiceProfile.MaximumSize;

        internal int Level;
        internal int Horizontal;
        internal int Vertical;
        internal int Size = DefaultSize;
        internal readonly Dictionary<int, string> Slots = new Dictionary<int, string>();

        internal bool HasTransform => Horizontal != 0 || Vertical != 0 || Size != DefaultSize;
        internal bool HasValues => Level != 0 || Slots.Count != 0 || HasTransform;

        internal ClothesJuiceProfile Copy()
        {
            var copy = new ClothesJuiceProfile
            {
                Level = Level, Horizontal = Horizontal, Vertical = Vertical, Size = Size
            };
            foreach (var entry in Slots)
                copy.Slots.Add(entry.Key, entry.Value);
            return copy;
        }

        // Store the transform separately so older builds can still read the original outfit slots.
        internal byte[] SerializeTransform() => new[] { (byte)1, unchecked((byte)(sbyte)Horizontal),
            unchecked((byte)(sbyte)Vertical), (byte)Size };

        internal bool TryReadTransform(byte[] data)
        {
            if (data == null || data.Length != 4 || data[0] != 1)
                return false;
            var x = (int)unchecked((sbyte)data[1]);
            var y = (int)unchecked((sbyte)data[2]);
            var size = (int)data[3];
            if (Math.Abs(x) > 100 || Math.Abs(y) > 100 || size < MinimumSize || size > MaximumSize)
                return false;

            Horizontal = x;
            Vertical = y;
            Size = size;
            return true;
        }

        internal byte[] Serialize()
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)1);
                // The old record still carries the on/off bit. The second tier is a sibling scene value.
                writer.Write(Level != 0);
                writer.Write((byte)Slots.Count);
                foreach (var entry in Slots)
                {
                    writer.Write((byte)entry.Key);
                    writer.Write(entry.Value);
                }
                return stream.ToArray();
            }
        }

        internal static ClothesJuiceProfile Deserialize(byte[] data)
        {
            if (data == null || data.Length < 3)
                return null;

            try
            {
                using (var stream = new MemoryStream(data, false))
                using (var reader = new BinaryReader(stream))
                {
                    if (reader.ReadByte() != 1)
                        return null;
                    var enabled = reader.ReadByte();
                    var count = reader.ReadByte();
                    if (enabled > 1 || count > ClothesJuiceParts.SlotCount || count > (stream.Length - stream.Position) / 3)
                        return null;

                    var profile = new ClothesJuiceProfile { Level = enabled };
                    for (var i = 0; i < count; i++)
                    {
                        var slot = reader.ReadByte();
                        var asset = reader.ReadString();
                        if (slot >= ClothesJuiceParts.SlotCount || asset.Length == 0 || profile.Slots.ContainsKey(slot))
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
