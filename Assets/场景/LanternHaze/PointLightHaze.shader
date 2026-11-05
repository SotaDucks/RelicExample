Shader "RelicExample/Point Light Haze"
{
    Properties
    {
        _FogColor("Fog Color", Color) = (1, 0.73, 0.40, 1)
        _Density("Density", Range(0, 0.6)) = 0.13
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "DisableBatching" = "True" }

        Pass
        {
            Name "LocalHaze"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float _Density;
            CBUFFER_END

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(float3 positionOS : POSITION)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            // Integral of the squared radial falloff along a ray through the sphere.
            float IntegratedFalloff(float distanceFromClosestPoint, float profile)
            {
                float distanceSquared = distanceFromClosestPoint * distanceFromClosestPoint;
                return distanceFromClosestPoint * (profile * profile + distanceSquared * (0.2 * distanceSquared - 2.0 * profile / 3.0));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 rayStart = ComputeWorldSpacePosition(uv, UNITY_NEAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 rayDirection = normalize(input.positionWS - rayStart);
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                // Use a uniformly scaled Unity sphere, whose mesh radius is 0.5.
                float radius = max(0.5 * length(TransformObjectToWorldDir(float3(1, 0, 0), false)), 0.001);
                float3 toCenter = center - rayStart;
                float closestDistance = dot(toCenter, rayDirection);
                float3 radialOffset = toCenter - rayDirection * closestDistance;
                float profile = 1.0 - dot(radialOffset, radialOffset) / (radius * radius);
                if (profile <= 0.0) return 0.0;

                float halfChord = radius * sqrt(profile);
                float entry = max(closestDistance - halfChord, 0.0);
                float exit = closestDistance + halfChord;
                float depth = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    bool hasSurface = depth > 0.00001;
                #else
                    bool hasSurface = depth < 0.99999;
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, depth);
                #endif
                if (hasSurface)
                {
                    float3 surface = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                    exit = min(exit, dot(surface - rayStart, rayDirection));
                }
                if (exit <= entry) return 0.0;

                float start = (entry - closestDistance) / radius;
                float end = (exit - closestDistance) / radius;
                float thickness = radius * max(IntegratedFalloff(end, profile) - IntegratedFalloff(start, profile), 0.0);
                float alpha = _FogColor.a * (1.0 - exp(-_Density * thickness));
                return half4(_FogColor.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
