// Frozen surface coordinates are carried independently of the animated vertex position.
struct HairVertex {
    float4 position : POSITION;
    float3 normal : NORMAL;
    float4 tangent : TANGENT;
    float2 uv : TEXCOORD0;
    float4 uv1 : TEXCOORD1;
    float2 uv2 : TEXCOORD2;
    float2 uv3 : TEXCOORD3;
    float4 color : COLOR0;
};

struct HairFragment {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldPosition : TEXCOORD1;
    float3 worldNormal : TEXCOORD2;
    float4 surfacePosition : TEXCOORD3;
};

#ifdef HAIR_VERTEX
// Offsets match the Unity 5.6 built-in buffers taken from the local shader bundle.
cbuffer UnityPerDraw : register(b0) {
    column_major float4x4 unity_ObjectToWorld : packoffset(c0);
    column_major float4x4 unity_WorldToObject : packoffset(c4);
};
cbuffer UnityPerFrame : register(b1) {
    column_major float4x4 unity_MatrixVP : packoffset(c17);
};

HairFragment main(HairVertex input) {
    HairFragment output;
    float4 world = mul(unity_ObjectToWorld, float4(input.position.xyz, 1.0));
    output.position = mul(unity_MatrixVP, world);
    output.worldPosition = world.xyz;
    output.worldNormal = mul(input.normal, (float3x3)unity_WorldToObject);
    output.uv = input.uv;
    output.surfacePosition = input.uv1;
    return output;
}
#else
cbuffer HairProjectionParams : register(b0) {
    float4 _HairProjectionX : packoffset(c0);
    float4 _HairProjectionY : packoffset(c1);
    float4 _HairProjectionZ : packoffset(c2);
    float4 _HairJuiceColor : packoffset(c3);
    float4 _HairJuiceParams : packoffset(c4);
    float4 _HairSourceTex_ST : packoffset(c5);
    float4 _HairAlphaTex_ST : packoffset(c6);
    float4 _HairAlphaParams : packoffset(c7);
    float4 _HairPatternTex_ST : packoffset(c8);
    float4 _HairProjectionDimensions : packoffset(c9);
    float4 _HairProjectionOffsets : packoffset(c10);
};
Texture2D _HairSourceTex : register(t0);
Texture2D _HairAlphaTex : register(t1);
Texture2D _HairPatternTex : register(t2);
Texture2D _HairDirectionMask : register(t3);
SamplerState sampler_HairSourceTex : register(s0);
SamplerState sampler_HairAlphaTex : register(s1);
SamplerState sampler_HairPatternTex : register(s2);
SamplerState sampler_HairDirectionMask : register(s3);

// PNG row zero is the top of the authored 2048 atlas. Probe broad,
// complementary R/G features to distinguish this atlas from other live masks.
float2 atlasProbe(float2 topLeftTexel) {
    float2 uv = (topLeftTexel + .5) / 2048.0;
    return _HairPatternTex.SampleLevel(sampler_HairPatternTex, float2(uv.x, 1.0 - uv.y), 0).rg;
}

bool hasQuarterTurnedIslands() {
    float2 upperR = atlasProbe(float2(1554, 419));
    float2 upperG = atlasProbe(float2(1491, 334));
    float2 lowerR = atlasProbe(float2(1824, 689));
    float2 lowerG = atlasProbe(float2(1579, 689));
    float2 upright = atlasProbe(float2(49, 403));
    return min(min(upperR.r, lowerR.r), upright.r) > .35 && min(upperG.g, lowerG.g) > .55
        && max(max(upperR.g, lowerR.g), upright.g) < .09 && max(upperG.r, lowerG.r) < .09;
}

float4 main(HairFragment input) : SV_Target {
    // z encodes source alpha, frozen coordinates, the accessory tag, and direction correction.
    float4 surface = float4(fmod(_HairJuiceParams.z, 4.0) >= 2.0 ? input.surfacePosition.xyz : input.worldPosition, 1.0);
    float3 projected = float3(dot(_HairProjectionX, surface), dot(_HairProjectionY, surface), dot(_HairProjectionZ, surface));
    float3 distanceFromCenter = abs(projected);
    float boundary = 1.0 - smoothstep(.44, .5, max(distanceFromCenter.x, max(distanceFromCenter.y, distanceFromCenter.z)));
    clip(boundary - .0005);

    float sourceAlpha = 1.0;
    if (fmod(_HairJuiceParams.z, 2.0) > .5)
        sourceAlpha = _HairSourceTex.Sample(sampler_HairSourceTex, input.uv * _HairSourceTex_ST.xy + _HairSourceTex_ST.zw).a;
    if (_HairAlphaParams.w > .5) {
        // Match main_opaque in both the color and depth passes. Disabled
        // channels cannot cut the garment; surviving pixels are opaque fabric.
        float2 mask = _HairAlphaTex.Sample(sampler_HairAlphaTex, input.uv * _HairAlphaTex_ST.xy + _HairAlphaTex_ST.zw).rg;
        float2 enabledMask = max(mask, 1.0 - _HairAlphaParams.xy);
        clip(min(sourceAlpha, min(enabledMask.x, enabledMask.y)) - .5);
        sourceAlpha = 1.0;
    } else {
        if (any(_HairAlphaParams > 0.0)) {
            float4 sampleAlpha = _HairAlphaTex.Sample(sampler_HairAlphaTex, input.uv * _HairAlphaTex_ST.xy + _HairAlphaTex_ST.zw);
            sourceAlpha *= saturate(dot(sampleAlpha, _HairAlphaParams));
        }
        clip(sourceAlpha - max(.003, _HairJuiceParams.w));
    }

    // Keep the original head-space XY flow on every surface. The texture's vertical axis must
    // not turn into head-space depth on selected triangles. Correct sampler bindings provide Repeat.
    // x2 adds the green channel from the live body's original Juices texture as on skin.
    float2 uv = (projected.xy + .5) * _HairPatternTex_ST.xy + _HairPatternTex_ST.zw;
    // Only private accessory geometry carries a per-strand phase in UV1.w.
    // Apply it after tiling, before both the native sample and its direction lookup.
    if (fmod(_HairJuiceParams.z, 8.0) >= 4.0 && _HairProjectionOffsets.w > .5)
        uv.x += input.surfacePosition.w;
    float2 mask = _HairPatternTex.Sample(sampler_HairPatternTex, uv).rg;
    float2 dx = ddx(uv), dy = ddy(uv);
    [branch] if (_HairJuiceParams.z >= 8.0) {
        float2 atlasUV = frac(uv);
        float region = _HairDirectionMask.SampleLevel(sampler_HairDirectionMask, atlasUV, 0).r;
        [branch] if (region > 0.0) {
            [branch] if (hasQuarterTurnedIslands()) {
                // The rotated islands are copies of the top-left tile. Sample
                // its upright counterpart in the same phase and at the same LOD.
                float2 uprightUV = frac(atlasUV * 4.0) * .25 + float2(0.0, .75);
                float2 upright = _HairPatternTex.SampleGrad(sampler_HairPatternTex, uprightUV, dx, dy).rg;
                mask = lerp(mask, upright, region);
            }
        }
    }
    float pattern = _HairJuiceParams.x >= 1.5 ? max(mask.r, mask.g) : mask.r;
    float bias = 24.0 * saturate(_HairJuiceParams.y) * pattern;
    float coverage = pattern * (1.0 + bias) / (1.0 + bias * pattern);
    float visibleSurface = boundary * saturate(abs(_HairJuiceColor.w)) * saturate(sourceAlpha);
    // Preserve the old footprint and the surface alpha. Only strengthen the interior pigment.
    clip(coverage * visibleSurface - .001);
    float remainder = 1.0 - saturate(coverage);
    float solidCoverage = 1.0 - remainder * remainder * remainder;
    float opacity = lerp(coverage, solidCoverage, smoothstep(.20, .76, pattern)) * visibleSurface;

    float3 normal = normalize(input.worldNormal + float3(0.0, .0001, 0.0));
    float shapeLight = .96 + .04 * saturate(abs(normal.y));
    float3 color = saturate(_HairJuiceColor.rgb * shapeLight);
    if (_HairJuiceColor.w > 0.0)
        color = lerp(color, 1.0, .3 * smoothstep(.52, .84, pattern));
    return float4(color, opacity);
}
#endif
