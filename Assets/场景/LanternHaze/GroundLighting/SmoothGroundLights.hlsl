#ifndef RELIC_SMOOTH_GROUND_LIGHTS_INCLUDED
#define RELIC_SMOOTH_GROUND_LIGHTS_INCLUDED

#ifndef SHADERGRAPH_PREVIEW
float GroundLightFalloff(int lightIndex, float3 worldPosition)
{
#if !USE_CLUSTER_LIGHT_LOOP
    lightIndex = GetPerObjectLightIndex(lightIndex);
#endif
#if USE_STRUCTURED_BUFFER_FOR_LIGHT_DATA
    float4 lightPosition = _AdditionalLightsBuffer[lightIndex].position;
    float4 attenuation = _AdditionalLightsBuffer[lightIndex].attenuation;
    float3 spotDirection = _AdditionalLightsBuffer[lightIndex].spotDirection.xyz;
#else
    float4 lightPosition = _AdditionalLightsPosition[lightIndex];
    float4 attenuation = _AdditionalLightsAttenuation[lightIndex];
    float3 spotDirection = _AdditionalLightsSpotDir[lightIndex].xyz;
#endif

    float3 toLight = lightPosition.xyz - worldPosition * lightPosition.w;
    float distanceSquared = max(dot(toLight, toLight), HALF_MIN);
    float rangeFraction = sqrt(distanceSquared * max(attenuation.x, 0.0));
    if (attenuation.z > 0.0)
    {
        float cone = saturate(dot(spotDirection, toLight * rsqrt(distanceSquared)) * attenuation.z + attenuation.w);
        return cone * cone * (1.0 - smoothstep(0.45, 1.0, rangeFraction));
    }
    return 1.0 - smoothstep(0.08, 1.0, rangeFraction);
}
#endif

// Keep the existing Custom Function ports so the copied Shader Graph can reuse its wiring.
void AdditionalLightsSmoothGround_float(float3 WorldPosition, float3 WorldNormal, half4 Shadowmask,
    float PointLightBands, float SpotLightBands, out float3 Diffuse, out float Attenuation)
{
    Diffuse = 0.0;
    Attenuation = 0.0;
#ifndef SHADERGRAPH_PREVIEW
    uint pixelLightCount = GetAdditionalLightsCount();
    uint meshRenderingLayers = GetMeshRenderingLayer();

#if USE_CLUSTER_LIGHT_LOOP
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
#ifdef _LIGHT_LAYERS
        if (!IsMatchingLightLayer(light.layerMask, meshRenderingLayers)) continue;
#endif
        float attenuation = light.distanceAttenuation * light.shadowAttenuation;
        Diffuse += light.color * attenuation;
        Attenuation += attenuation;
    }
#endif

    InputData inputData = (InputData) 0;
    float4 screenPosition = ComputeScreenPos(TransformWorldToHClip(WorldPosition));
    inputData.normalizedScreenSpaceUV = screenPosition.xy / screenPosition.w;
    inputData.positionWS = WorldPosition;

    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, WorldPosition, Shadowmask);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
#endif
        {
            float falloff = GroundLightFalloff(lightIndex, WorldPosition);
            Diffuse += light.color * light.shadowAttenuation * falloff;
            Attenuation += light.distanceAttenuation * light.shadowAttenuation;
        }
    LIGHT_LOOP_END
#endif
}

#endif
