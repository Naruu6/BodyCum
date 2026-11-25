using System;
using System.IO;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private const string AccessoryPatternResource = "BodyCum.Resources.AccessoryJuiceMask.png";
        private Texture2D _accessoryPattern;
        private bool _attemptedAccessoryPattern;

        private Texture2D GetAccessoryPattern()
        {
            if (_accessoryPattern || _attemptedAccessoryPattern)
                return _accessoryPattern;
            _attemptedAccessoryPattern = true;
            Texture2D texture = null;
            try
            {
                using (var stream = typeof(HairJuiceFeature).Assembly.GetManifestResourceStream(AccessoryPatternResource))
                {
                    if (stream == null)
                        throw new IOException("The embedded accessory mask is missing.");
                    using (var reader = new BinaryReader(stream))
                    {
                        // The R/G values are opacity data, independent of scene color space.
                        texture = new Texture2D(2, 2, TextureFormat.RGB24, true, true)
                        {
                            name = "BodyCum Accessory Juice Mask",
                            hideFlags = HideFlags.HideAndDontSave,
                            wrapMode = TextureWrapMode.Repeat,
                            filterMode = FilterMode.Bilinear,
                            anisoLevel = 0
                        };
                        if (!texture.LoadImage(reader.ReadBytes(checked((int)stream.Length)), true))
                            throw new IOException("Unity could not decode the embedded accessory mask.");
                    }
                }
                _accessoryPattern = texture;
            }
            catch (Exception error)
            {
                if (texture)
                    UnityEngine.Object.Destroy(texture);
                _log.LogWarning("Could not load the accessory mask; using the original body pattern: " + error.Message);
            }
            return _accessoryPattern;
        }

        internal void Dispose()
        {
            // Retire draw buffers/materials before the texture they may reference.
            Disable();
            if (_accessoryPattern)
                UnityEngine.Object.Destroy(_accessoryPattern);
            _accessoryPattern = null;
        }
    }
}
