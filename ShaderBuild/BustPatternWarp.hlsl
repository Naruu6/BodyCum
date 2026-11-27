// Raw body UV, before liquid tiling. Shadows and skin textures retain the original UV.
// Constants mirror AbdomenJuiceMask.EmitBodyPattern for the native skin fragments.
float2 BustOffsetFromCentre(float2 uv)
{
    float2 centre = float2(uv.x < .1566 ? .1107 : .2025, .8453);
    return uv - centre;
}

float BustWarpWeight(float2 uv)
{
    float2 ellipse = BustOffsetFromCentre(uv) * float2(1.0 / .0455, 1.0 / .053);
    float t = saturate((1.0 - dot(ellipse, ellipse)) / .51);
    return t * t * (3.0 - 2.0 * t);
}

float2 WarpBustUv(float2 uv, float2 outer, float2 inner)
{
    float2 offset = BustOffsetFromCentre(uv);
    float2 ellipse = offset * float2(1.0 / .0455, 1.0 / .053);
    // Join measured inner and outer gains with zero slope at the inner edge.
    float ramp = saturate(1.0 - .24 * rsqrt(max(dot(ellipse, ellipse), .24 * .24)));
    float2 gain = lerp(inner, outer, ramp * ramp / (.70 * .70));
    return uv + offset * gain;
}
