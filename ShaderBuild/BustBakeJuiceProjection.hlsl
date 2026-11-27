#include "BustPatternWarp.hlsl"

struct BakeFragment { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
#ifdef HAIR_VERTEX
BakeFragment main(float2 uv : TEXCOORD0) {
    BakeFragment output;
    // D3D texture row zero is at the top. Derive clip position from the blit UV so
    // both directions use exactly the same atlas axes as sampling the source texture.
    output.position = float4(2.0 * uv.x - 1.0, 1.0 - 2.0 * uv.y, 0.0, 1.0);
    output.uv = uv;
    return output;
}
#else
cbuffer Params : register(b0) { float4 _BustBakeScale : packoffset(c0); float4 _BustBakeTransform : packoffset(c1); };
Texture2D _BustBakeSource : register(t0);
SamplerState bakeSampler : register(s0);
float4 main(BakeFragment input) : SV_Target {
    float4 original = _BustBakeSource.Sample(bakeSampler, input.uv * _BustBakeTransform.xy + _BustBakeTransform.zw);
    float weight = BustWarpWeight(input.uv);
    if (weight <= 0) return original;
    float2 uv = WarpBustUv(input.uv, _BustBakeScale.xy, _BustBakeScale.zw);
    float4 corrected = _BustBakeSource.Sample(bakeSampler, uv * _BustBakeTransform.xy + _BustBakeTransform.zw);
    return lerp(original, corrected, weight);
}
#endif
