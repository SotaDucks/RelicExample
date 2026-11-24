Shader "RelicExample/Interactive Ground Fog"
{
    Properties
    {
        [MainColor] _FogColor ("Fog Color", Color) = (1, 1, 1, 1)
        _DensityMap ("Ground Density", 2D) = "black" {}
        _DensityPatternSize ("Density Pattern Size (m)", Float) = 18
        [HideInInspector] _LiveDensity ("Simulated Density", 2D) = "black" {}
        [HideInInspector] _SimulationWindow ("Simulation Window", Vector) = (0, 0, 0, 0)
        [HideInInspector] _MotionMap ("Fog Motion", 2D) = "black" {}
        [HideInInspector] _HistoryMap ("Fog Wake History", 2D) = "black" {}
        [HideInInspector] _HistoryArea ("Wake History Area", Vector) = (0, 0, 0, 0)
        [HideInInspector] _LiftExtent ("Lift Extent", Float) = 0
        _Detail ("Volume Detail", 3D) = "white" {}
        _FogTint ("Fog Tint", Color) = (0.58, 0.69, 0.79, 1)
        _ShadowTint ("Shadow Tint", Color) = (0.29, 0.39, 0.51, 1)
        _Extinction ("Density Strength", Range(0, 5)) = 2.1
        _ContourStrength ("Contour Strength", Range(0, 1)) = 0.22
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-20" }

        Pass
        {
            Name "Ground Fog Volume"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "GroundFogWorldPattern.hlsl"

            TEXTURE2D(_DensityMap);
            SAMPLER(sampler_DensityMap);
            TEXTURE2D(_LiveDensity);
            SAMPLER(sampler_LiveDensity);
            TEXTURE2D(_MotionMap);
            SAMPLER(sampler_MotionMap);
            TEXTURE2D(_HistoryMap);
            SAMPLER(sampler_HistoryMap);
            TEXTURE3D(_Detail);
            SAMPLER(sampler_Detail);

            CBUFFER_START(UnityPerMaterial)
                float4 _SimulationWindow;
                float4 _HistoryArea;
                half4 _FogColor;
                half4 _FogTint;
                half4 _ShadowTint;
                float _Extinction;
                float _ContourStrength;
                float _DensityPatternSize;
                float _LiftExtent;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                input.positionOS.y += step(0.0, input.positionOS.y) * _LiftExtent;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float2 CubeInterval(float3 origin, float3 direction)
            {
                float3 signs = step(0.0, direction) * 2.0 - 1.0;
                float3 reciprocal = signs / max(abs(direction), 0.00001);
                float3 first = (-0.5 - origin) * reciprocal;
                float3 upper = float3(0.5, 0.5 + _LiftExtent, 0.5);
                float3 second = (upper - origin) * reciprocal;
                float3 entry = min(first, second);
                float3 exit = max(first, second);
                return float2(max(entry.x, max(entry.y, entry.z)), min(exit.x, min(exit.y, exit.z)));
            }

            float2 WorldToWindowUV(float2 worldXZ)
            {
                return (worldXZ - _SimulationWindow.xy) * _SimulationWindow.w + 0.5;
            }

            float WindowBlend(float2 uv)
            {
                float2 edgeDistance = min(uv, 1.0 - uv);
                return step(0.00001, _SimulationWindow.w) * smoothstep(0.0, 0.28, min(edgeDistance.x, edgeDistance.y));
            }

            float4 ReadHistory(float2 worldXZ)
            {
                if (any(_HistoryArea.zw <= 0.0))
                    return 0;

                float2 uv = (worldXZ - _HistoryArea.xy) / _HistoryArea.zw + 0.5;
                float2 edgeDistance = min(uv, 1.0 - uv);
                float fade = smoothstep(0.0, 0.025, min(edgeDistance.x, edgeDistance.y));
                if (fade <= 0.0001)
                    return 0;

                return SAMPLE_TEXTURE2D_LOD(_HistoryMap, sampler_HistoryMap, uv, 0) * fade;
            }

            float ReadDensity(float2 worldXZ, float2 windowUV, float blend, float historyDelta)
            {
                if (blend >= 0.9999)
                    return SAMPLE_TEXTURE2D_LOD(_LiveDensity, sampler_LiveDensity, windowUV, 0).r;

                float background = SampleGroundFogPattern(_DensityMap, sampler_DensityMap,
                    _Detail, sampler_Detail, worldXZ, _DensityPatternSize, _Time.y);
                background = clamp(background + historyDelta, 0.0, 1.5);
                if (blend <= 0.0001)
                    return background;

                return lerp(background, SAMPLE_TEXTURE2D_LOD(_LiveDensity, sampler_LiveDensity, windowUV, 0).r, blend);
            }

            float ReadDensity(float2 worldXZ)
            {
                float2 windowUV = WorldToWindowUV(worldXZ);
                float blend = WindowBlend(windowUV);
                float historyDelta = blend >= 0.9999 ? 0.0 : ReadHistory(worldXZ).x;
                return ReadDensity(worldXZ, windowUV, blend, historyDelta);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ray = -GetWorldSpaceNormalizeViewDir(input.positionWS);
                float3 origin = GetCameraPositionWS();
                if (IsPerspectiveProjection() == false)
                    origin = input.positionWS - ray * dot(input.positionWS - origin, ray);

                float3 localOrigin = TransformWorldToObject(origin);
                float3 localRay = mul((float3x3)GetWorldToObjectMatrix(), ray);
                float2 interval = CubeInterval(localOrigin, localRay);
                interval.x = max(interval.x, 0.0);

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneDepth = SampleSceneDepth(screenUV);
                #if UNITY_REVERSED_Z
                    bool hitOpaque = sceneDepth > 0.000001;
                #else
                    bool hitOpaque = sceneDepth < 0.999999;
                    sceneDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, sceneDepth);
                #endif
                if (hitOpaque)
                {
                    float3 opaquePoint = ComputeWorldSpacePosition(screenUV, sceneDepth, UNITY_MATRIX_I_VP);
                    interval.y = min(interval.y, dot(opaquePoint - origin, ray));
                }

                clip(interval.y - interval.x - 0.0001);

                float rayLength = interval.y - interval.x;
                int sampleCount = (int)clamp(ceil(rayLength / 0.16), 16.0, 40.0);
                float stepLength = rayLength / sampleCount;
                float3x3 objectToWorld = (float3x3)GetObjectToWorldMatrix();
                float extentX = length(mul(objectToWorld, float3(1, 0, 0)));
                float extentZ = length(mul(objectToWorld, float3(0, 0, 1)));
                float fadeStart = 1.0 - saturate(4.2 / max(0.1, min(extentX, extentZ)));
                half3 fogColor = 0;
                float transmittance = 1;

                [loop]
                for (int i = 0; i < sampleCount; i++)
                {
                    float distance = interval.x + (i + 0.5) * stepLength;
                    float3 local = localOrigin + localRay * distance;
                    float3 world = origin + ray * distance;
                    float2 windowUV = WorldToWindowUV(world.xz);
                    float simulationBlend = WindowBlend(windowUV);
                    float4 wakeHistory = simulationBlend >= 0.9999 ? 0.0 : ReadHistory(world.xz);
                    float edge = 1.0 - smoothstep(fadeStart, 1.0, length(local.xz * 2.0));
                    float mass = ReadDensity(world.xz, windowUV, simulationBlend, wakeHistory.x) * edge;
                    if (mass < 0.009)
                        continue;

                    float4 motion = 0;
                    if (simulationBlend > 0.0001)
                        motion = SAMPLE_TEXTURE2D_LOD(_MotionMap, sampler_MotionMap, windowUV, 0);
                    float lift = lerp(wakeHistory.y, motion.x, simulationBlend) * _LiftExtent * edge;
                    float2 horizontalOffset = lerp(wakeHistory.zw, motion.yz * _SimulationWindow.z, simulationBlend);
                    float3 horizontalMotion = float3(horizontalOffset.x, 0, horizontalOffset.y);
                    float3 detailWorld = world - horizontalMotion - mul(objectToWorld, float3(0, lift * 0.65, 0));
                    float3 detailUV = float3(0.92 * detailWorld.x - 0.39 * detailWorld.z, detailWorld.y,
                        0.39 * detailWorld.x + 0.92 * detailWorld.z) * float3(0.32, 0.48, 0.32);
                    detailUV += _Time.y * float3(0.009, 0.001, -0.007);
                    float coarse = SAMPLE_TEXTURE3D_LOD(_Detail, sampler_Detail, detailUV, 0).r;
                    float3 fineUV = float3(0.63 * detailUV.x + 0.78 * detailUV.z, detailUV.y,
                        0.63 * detailUV.z - 0.78 * detailUV.x) * 2.24 + 0.31;
                    float fine = SAMPLE_TEXTURE3D_LOD(_Detail, sampler_Detail, fineUV, 0).r;
                    float billow = saturate(0.75 * coarse + 0.25 * fine);
                    float height = local.y + 0.5;
                    float crest = lerp(0.26, 1.0, saturate(mass * 0.82 + billow * 0.63)) + lift;
                    float profile = 1.0 - smoothstep(crest * 0.32, crest, height);
                    float extinction = mass * profile * lerp(0.37, 1.5, billow) * _Extinction;
                    float opacity = 1.0 - exp(-extinction * stepLength);

                    float contour = 0;
                    float contourBand = 1.0 - smoothstep(0.024, 0.11, abs(mass - 0.42));
                    if (contourBand * _ContourStrength > 0.001)
                    {
                        float forwardX = ReadDensity(world.xz + float2(0.125, 0)) * edge;
                        float forwardZ = ReadDensity(world.xz + float2(0, 0.125)) * edge;
                        float slope = abs(forwardX - mass) + abs(forwardZ - mass);
                        contour = smoothstep(0.012, 0.075, slope) * contourBand;
                    }
                    float lit = saturate(billow * 0.83 + height * 0.50 + contour * _ContourStrength);
                    half3 tint = lerp(_ShadowTint.rgb, _FogTint.rgb, lit) * _FogColor.rgb;
                    fogColor += tint * (opacity * transmittance);
                    transmittance *= 1.0 - opacity;
                    if (transmittance < 0.02)
                        break;
                }

                return half4(fogColor, 1.0 - transmittance);
            }
            ENDHLSL
        }
    }
}
