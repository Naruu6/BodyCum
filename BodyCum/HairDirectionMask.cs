using System;
using System.IO;
using UnityEngine;

namespace BodyCum
{
    internal sealed partial class HairJuiceFeature
    {
        private static readonly int HairDirectionMask = Shader.PropertyToID("_HairDirectionMask");
        private Texture2D _directionMask;
        private bool _triedDirectionMask;

        private Texture2D GetDirectionMask()
        {
            if (_triedDirectionMask) return _directionMask;
            _triedDirectionMask = true;
            Texture2D texture = null;
            try
            {
                using (var stream = typeof(HairJuiceFeature).Assembly.GetManifestResourceStream("BodyCum.Resources.HairDirectionRegions.png"))
                {
                    if (stream == null) throw new IOException("Missing hair direction region map.");
                    using (var reader = new BinaryReader(stream))
                    {
                        texture = new Texture2D(2, 2, TextureFormat.RGB24, false, true);
                        if (!texture.LoadImage(reader.ReadBytes(checked((int)stream.Length)), true))
                            throw new IOException("Could not decode hair direction region map.");
                    }
                }
                texture.name = "BodyCum Hair Direction Regions";
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Bilinear;
                texture.hideFlags = HideFlags.HideAndDontSave;
                _directionMask = texture;
            }
            catch (Exception error)
            {
                if (texture) UnityEngine.Object.Destroy(texture);
                _log.LogWarning("Using the live hair pattern without direction correction: " + error.Message);
            }
            return _directionMask;
        }

        private void ReleaseDirectionMask()
        {
            if (_directionMask) UnityEngine.Object.Destroy(_directionMask);
            _directionMask = null;
            _triedDirectionMask = false;
        }
    }
}
