Shader "RelicExample/Mask Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("Base texture",2D)="white"{}
        [MainColor] _BaseColor("Texture tint",Color)=(1,1,1,1)
        [Toggle] _ToonEnabled("Enable toon shading",Float)=1
        [Toggle] _UseMidtone("Use middle band",Float)=0
        _ShadowThreshold("Shadow boundary",Range(0,1))=0.52
        _HighlightThreshold("Light boundary",Range(0,1))=0.78
        _ShadowLevel("Dark brightness",Range(0,2))=0.28
        _MidLevel("Middle brightness",Range(0,2))=0.64
        _LightLevel("Light brightness",Range(0,2))=1.15
        _BandSoftness("Boundary softness",Range(0,0.2))=0.004
        _Exposure("Exposure",Range(-2,2))=0
        _Saturation("Saturation",Range(0,1.5))=1
        _ShadowTint("Shadow tint",Color)=(0.78,0.83,1,1)
        _MidTint("Middle tint",Color)=(0.95,0.95,1,1)
        _LightTint("Light tint",Color)=(1,0.98,0.93,1)
        [Toggle] _UseSceneLight("Use scene key light direction",Float)=1
        _LightYaw("Custom light yaw",Range(-180,180))=-35
        _LightPitch("Custom light pitch",Range(0,90))=48
        _SceneColorInfluence("Scene light color influence",Range(0,1))=0.12
        [Toggle] _ReceiveSceneShadows("Receive scene cast shadows",Float)=0
        [Toggle] _AlphaClip("Alpha clipping",Float)=0
        _Cutoff("Alpha cutoff",Range(0,1))=0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull",Float)=2
        [HideInInspector] _MaskToonMarker("Mask category",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST,_BaseColor,_ShadowTint,_MidTint,_LightTint;
            float _ToonEnabled,_UseMidtone,_ShadowThreshold,_HighlightThreshold;
            float _ShadowLevel,_MidLevel,_LightLevel,_BandSoftness,_Exposure,_Saturation;
            float _UseSceneLight,_LightYaw,_LightPitch,_SceneColorInfluence,_ReceiveSceneShadows;
            float _AlphaClip,_Cutoff,_Cull,_MaskToonMarker;
        CBUFFER_END
        struct Attributes
        {
            float3 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;
            float3 normalWS:TEXCOORD1;float2 uv:TEXCOORD2;float fog:TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings MaskVertex(Attributes input)
        {
            Varyings output=(Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            output.positionWS=TransformObjectToWorld(input.positionOS);
            output.positionCS=TransformWorldToHClip(output.positionWS);
            output.normalWS=TransformObjectToWorldNormal(input.normalOS);
            output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
            output.fog=ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 Albedo(Varyings input)
        {
            half4 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv)*_BaseColor;
            if(_AlphaClip>0.5)clip(albedo.a-_Cutoff);
            return albedo;
        }
        float Boundary(float signal,float threshold)
        {
            if(_BandSoftness<0.0001)return step(threshold,signal);
            return smoothstep(threshold-_BandSoftness,threshold+_BandSoftness,signal);
        }
        half4 MaskFragment(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 base=Albedo(input);
            float3 normal=normalize(input.normalWS);
            Light key=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
            float yaw=radians(_LightYaw),pitch=radians(_LightPitch);
            float3 customDirection=float3(sin(yaw)*cos(pitch),sin(pitch),cos(yaw)*cos(pitch));
            float3 direction=normalize(lerp(customDirection,key.direction,step(0.5,_UseSceneLight)));
            float signal=saturate(dot(normal,direction)*0.5+0.5);
            signal*=lerp(1,key.shadowAttenuation,step(0.5,_ReceiveSceneShadows));
            float low=Boundary(signal,_ShadowThreshold);
            float high=Boundary(signal,max(_ShadowThreshold+0.001,_HighlightThreshold));
            float3 tone=lerp(_ShadowTint.rgb*_ShadowLevel,_LightTint.rgb*_LightLevel,low);
            if(_UseMidtone>0.5)
                tone=lerp(lerp(_ShadowTint.rgb*_ShadowLevel,_MidTint.rgb*_MidLevel,low),_LightTint.rgb*_LightLevel,high);
            if(_ToonEnabled<0.5)tone=lerp(_ShadowTint.rgb*_ShadowLevel,_LightTint.rgb*_LightLevel,signal);
            float gray=dot(base.rgb,float3(0.2126,0.7152,0.0722));
            float3 textureColor=max(0,lerp(gray.xxx,base.rgb,_Saturation))*exp2(_Exposure);
            float3 color=textureColor*tone*lerp(float3(1,1,1),key.color,_SceneColorInfluence);
            color=MixFog(color,input.fog);
            return half4(color,base.a);
        }
        half4 DepthFragment(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            Albedo(input);return input.positionCS.z;
        }
        half4 NormalsFragment(Varyings input):SV_Target
        {
            UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            Albedo(input);float3 normal=normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 packed=PackNormalOctQuadEncode(normal)*0.5+0.5;
                return half4(PackFloat2To888(packed),0);
            #else
                return half4(normal,0);
            #endif
        }
        float3 _LightDirection,_LightPosition;
        Varyings MaskShadowVertex(Attributes input)
        {
            Varyings output=MaskVertex(input);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection=normalize(_LightPosition-output.positionWS);
            #else
                float3 lightDirection=_LightDirection;
            #endif
            output.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,lightDirection)));
            return output;
        }
        ENDHLSL
        Pass
        {
            Name "Mask Toon Forward" Tags{"LightMode"="UniversalForwardOnly"}
            ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment MaskFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags{"LightMode"="DepthOnly"}
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags{"LightMode"="DepthNormalsOnly"}
            ZWrite On
            HLSLPROGRAM
            #pragma vertex MaskVertex
            #pragma fragment NormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags{"LightMode"="ShadowCaster"}
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex MaskShadowVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
    }
    CustomEditor "RelicExample.EditorTools.MaskToonShaderGUI"
    Fallback "Hidden/Universal Render Pipeline/FallbackError"
}
