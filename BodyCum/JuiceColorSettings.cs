using UnityEngine;

namespace BodyCum
{
    internal struct JuiceColorSettings
    {
        internal Color32 Color;
        internal int Opacity;
        internal int Density;
        internal int Brightness;

        internal Vector4 ToShaderColor(bool linear)
        {
            var source = (Color)Color;
            var brightness = Brightness / 100f;
            var color = new Color(Mathf.Clamp01(source.r * brightness), Mathf.Clamp01(source.g * brightness),
                Mathf.Clamp01(source.b * brightness));
            if (linear)
                color = color.linear;
            var opacity = Opacity / 100f;
            return new Vector4(color.r, color.g, color.b, -opacity);
        }
    }
}
