Shader "Hidden/RelicExample/Outline Silhouette Closing"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        float4 _MaskDirection;
        float _MaskRadius, _InputNormals;
        float ReadMask(float2 uv)
        {
            return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_PointClamp,saturate(uv),0).r;
        }
        float4 Dilate(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float mask=ReadMask(i.texcoord);
            if(mask>0.5)return float4(mask,0,0,1); // Never overwrite an existing object's identity.
            [unroll]for(int s=1;s<=8;s++)if(s<=_MaskRadius)
            {
                mask=max(ReadMask(i.texcoord+_MaskDirection.xy*s),ReadMask(i.texcoord-_MaskDirection.xy*s));
                if(mask>0.5)break;
            }
            return float4(mask,0,0,1);
        }
        float4 Erode(Varyings i):SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
            float mask=ReadMask(i.texcoord);
            float keep=1;
            [unroll]for(int s=1;s<=8;s++)if(s<=_MaskRadius)
            {
                keep*=step(0.5,ReadMask(i.texcoord+_MaskDirection.xy*s))*step(0.5,ReadMask(i.texcoord-_MaskDirection.xy*s));
            }
            return float4(mask*keep,0,0,1);
        }
        ENDHLSL
        Pass
        {
            Name "Dilate"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Dilate
            ENDHLSL
        }
        Pass
        {
            Name "Erode"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Erode
            ENDHLSL
        }
    }
    Fallback Off
}
