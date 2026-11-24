#ifndef RELIC_GROUND_FOG_WORLD_PATTERN
#define RELIC_GROUND_FOG_WORLD_PATTERN

static const float2 GroundFogAmbientWind = float2(0.12, -0.08);

float SampleGroundFogPattern(
    Texture2D<float4> densityPattern, SamplerState densitySampler,
    Texture3D<float4> volumeDetail, SamplerState detailSampler,
    float2 worldXZ, float patternSize, float time)
{
    float2 uv = (worldXZ - GroundFogAmbientWind * time) / max(patternSize, 1.0);
    float2 detailUV = float2(0.545 * uv.x - 0.839 * uv.y, 0.839 * uv.x + 0.545 * uv.y) * 1.37;
    float variation = volumeDetail.SampleLevel(detailSampler, float3(detailUV.x + 0.13, 0.31, detailUV.y + 0.47), 0).r;

    float2 artwork = float2(0.924 * uv.x - 0.383 * uv.y, 0.383 * uv.x + 0.924 * uv.y);
    artwork += (variation - 0.5) * float2(0.37, -0.27);
    // Fold only the interior of the authored density texture so its faded border never tiles.
    float2 artworkUV = 0.5 + 0.34 * cos(6.2831853 * artwork);
    float broadDensity = densityPattern.SampleLevel(densitySampler, artworkUV, 0).r;
    return lerp(broadDensity, lerp(0.09, 0.84, smoothstep(0.27, 0.74, variation)), 0.55);
}

#endif
