// Draw a surface layer from the native liquid UVs while keeping the source material active.
struct SkinVertex {
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct SkinFragment {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 worldNormal : TEXCOORD1;
};

#ifdef HAIR_VERTEX
cbuffer UnityPerDraw : register(b0) {
    column_major float4x4 unity_ObjectToWorld : packoffset(c0);
    column_major float4x4 unity_WorldToObject : packoffset(c4);
};
cbuffer UnityPerFrame : register(b1) {
    column_major float4x4 unity_MatrixVP : packoffset(c17);
};

SkinFragment main(SkinVertex input) {
    SkinFragment output;
    float4 world = mul(unity_ObjectToWorld, float4(input.position.xyz, 1.0));
    output.position = mul(unity_MatrixVP, world);
    output.uv = input.uv;
    output.worldNormal = mul(input.normal, (float3x3)unity_WorldToObject);
    return output;
}
#else
cbuffer SkinProjectionParams : register(b0) {
    // These two slots retain the overlay packer's common property names.
    float4 _HairProjectionX : packoffset(c0);  // Front upper/lower, back upper/lower.
    float4 _HairProjectionY : packoffset(c1);  // Face tier, Belly tier.
    float4 _HairJuiceColor : packoffset(c3);
    float4 _HairJuiceParams : packoffset(c4);
    float4 _HairSourceTex_ST : packoffset(c5);
    float4 _HairAlphaTex_ST : packoffset(c6);
    float4 _HairAlphaParams : packoffset(c7);
    float4 _HairPatternTex_ST : packoffset(c8);
    float4 _SkinMask_ST : packoffset(c9);
};
Texture2D _HairSourceTex : register(t0);
Texture2D _HairAlphaTex : register(t1);
Texture2D _HairPatternTex : register(t2);
Texture2D _SkinMask : register(t3);
SamplerState sampler_HairSourceTex : register(s0);
SamplerState sampler_HairAlphaTex : register(s1);
SamplerState sampler_HairPatternTex : register(s2);
SamplerState sampler_SkinMask : register(s3);

float NativeCoverage(float2 pattern, float3 mask) {
    // RGB encode front upper/lower and back upper, yellow encodes back lower, cyan the face.
    float4 region = float4(mask.r - max(mask.g, mask.b), mask.g - max(mask.r, mask.b),
        mask.b - max(mask.r, mask.g), (min(mask.r, mask.g) - .1) / .9);
    float4 tier = _HairProjectionX;
    float4 body = min(region, max(pattern.r * saturate(tier), pattern.g * saturate(tier - 1.0)));
    float faceTier = _HairProjectionY.x;
    float face = min((min(mask.g, mask.b) - .1) / .9,
        max(pattern.r * saturate(faceTier), pattern.g * saturate(faceTier - 1.0)));
    return saturate(max(face, max(max(body.x, body.y), max(body.z, body.w))));
}

// The stock Belly/side regions use the original torso UV, before texture tiling.
// Keep the boundaries and fades aligned with AbdomenJuiceMask.cs.
float FeatheredWindow(float2 uv, float2 lower, float2 upper, float4 feather) {
    float2 enter = (uv - lower) / feather.xy;
    float2 exit = (upper - uv) / feather.zw;
    return smoothstep(0.0, 1.0, min(min(enter.x, enter.y), min(exit.x, exit.y)));
}

float BellyWindow(float2 uv, bool secondTier) {
    float edge = secondTier ? .019 : .009;
    float gate = FeatheredWindow(uv, float2(.101 - edge, .700), float2(.212 + edge, .813),
        float4(edge, .014, edge, .014));
    float front = min(uv.x, .3132 - uv.x);
    if (secondTier) {
        float pinch = smoothstep(0.0, 1.0, min((uv.y - .815) / .018, (.878 - uv.y) / .017));
        float underarm = FeatheredWindow(float2(front, uv.y), float2(.090, .790),
            float2(lerp(.124, .1045, pinch), .899), float4(.006, .020, .006, .024));
        gate = max(gate, .85 * underarm);
    }

    float bend = saturate((uv.y - .785) / .067);
    float2 frontEdges = lerp(float2(.050, .111), float2(.039, .104), bend);
    float frontFlank = FeatheredWindow(float2(front, uv.y), float2(frontEdges.x, .696),
        float2(frontEdges.y, .904), float4(.009, .016, .009, .016));
    float rear = max(uv.x, .8948 - uv.x);
    float2 rearEdges = lerp(float2(.491, .552), float2(.486, .548), bend);
    float rearFlank = FeatheredWindow(float2(rear, uv.y), float2(rearEdges.x, .696),
        float2(rearEdges.y, .904), float4(.022, .016, .009, .016));
    return max(gate, max(frontFlank, rearFlank) * (secondTier ? .96 : .64));
}

float4 main(SkinFragment input) : SV_Target {
    float sourceAlpha = 1.0;
    if (_HairJuiceParams.z > .5)
        sourceAlpha = _HairSourceTex.Sample(sampler_HairSourceTex, input.uv * _HairSourceTex_ST.xy + _HairSourceTex_ST.zw).a;
    if (any(_HairAlphaParams > 0.0)) {
        float4 alpha = _HairAlphaTex.Sample(sampler_HairAlphaTex, input.uv * _HairAlphaTex_ST.xy + _HairAlphaTex_ST.zw);
        sourceAlpha *= saturate(dot(alpha, _HairAlphaParams));
    }
    clip(sourceAlpha - max(.003, _HairJuiceParams.w));

    float2 patternUv = input.uv * _HairPatternTex_ST.xy + _HairPatternTex_ST.zw;
    float2 pattern = _HairPatternTex.Sample(sampler_HairPatternTex, patternUv).rg;
    float3 mask = _SkinMask.Sample(sampler_SkinMask, input.uv * _SkinMask_ST.xy + _SkinMask_ST.zw).rgb;
    float coverage = NativeCoverage(pattern, mask);
    float bellyTier = _HairProjectionY.y;
    if (bellyTier > 0.0) {
        float2 shifted = _HairPatternTex.Sample(sampler_HairPatternTex, patternUv - float2(.084, .096)).rg;
        float2 merged = max(pattern, .91 * shifted);
        bool secondTier = bellyTier > 1.5;
        float belly = secondTier ? max(merged.r, merged.g) : merged.r;
        coverage = max(coverage, belly * (1.38 - .38 * belly) * BellyWindow(input.uv, secondTier));
    }

    float bias = 24.0 * saturate(_HairJuiceParams.y) * coverage;
    float opacity = coverage * (1.0 + bias) / (1.0 + bias * coverage);
    opacity *= saturate(abs(_HairJuiceColor.w)) * saturate(sourceAlpha);
    clip(opacity - .001);

    float3 normal = normalize(input.worldNormal + float3(0.0, .0001, 0.0));
    float3 color = saturate(_HairJuiceColor.rgb * (.78 + .22 * saturate(abs(normal.y))));
    if (_HairJuiceColor.w > 0.0)
        color = lerp(color, 1.0, .3 * smoothstep(.52, .84, coverage));
    return float4(color, opacity);
}
#endif
