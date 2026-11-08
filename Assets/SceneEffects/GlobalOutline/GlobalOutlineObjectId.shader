Shader "Hidden/RelicExample/Outline Object ID"
{
    Properties
    {
        _GlobalOutlineObjectId("Object ID",Float)=1
        _SourceAlphaMap("Source alpha",2D)="white"{}
        _SourceAlphaClip("Alpha clipping",Float)=0
        _SourceCutoff("Source cutoff",Float)=0.5
        _SourceAlphaRed("Use red channel alpha",Float)=0
        _OutlineDepthTolerance("Depth tolerance",Float)=0.025
        _OutlineCanopyMask("Leaf canopy",Float)=0
        _OutlinePlantWind("Match SG Plant wind",Float)=0
        _OutlineWind("Strength density speed UV1",Vector)=(0,0,0,0)
        _OutlineWindFalloff("UV1 falloff",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline"}
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex ObjectVertex
            #pragma fragment ObjectFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Hashes.hlsl"
            TEXTURE2D(_SourceAlphaMap);SAMPLER(sampler_SourceAlphaMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _SourceAlphaMap_ST;
                float _GlobalOutlineObjectId,_SourceAlphaClip,_SourceCutoff,_SourceAlphaRed,_OutlineDepthTolerance,_OutlineCanopyMask;
                float4 _OutlineWind;
                float _OutlinePlantWind,_OutlineWindFalloff;
            CBUFFER_END
            struct Attributes{float3 positionOS:POSITION;float2 uv:TEXCOORD0;float2 uv1:TEXCOORD1;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;UNITY_VERTEX_OUTPUT_STEREO};
            float2 WindDirection(float2 p)
            {
                float x;Hash_Tchou_2_1_float(p,x);
                return normalize(float2(x-floor(x+0.5),abs(x)-0.5));
            }
            float WindNoise(float2 p)
            {
                float2 ip=floor(p),fp=frac(p);
                float d00=dot(WindDirection(ip),fp);
                float d01=dot(WindDirection(ip+float2(0,1)),fp-float2(0,1));
                float d10=dot(WindDirection(ip+float2(1,0)),fp-float2(1,0));
                float d11=dot(WindDirection(ip+float2(1,1)),fp-float2(1,1));
                fp=fp*fp*fp*(fp*(fp*6-15)+10);
                return lerp(lerp(d00,d01,fp.y),lerp(d10,d11,fp.y),fp.x);
            }
            Varyings ObjectVertex(Attributes i)
            {
                UNITY_SETUP_INSTANCE_ID(i);Varyings o;UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS=TransformObjectToWorld(i.positionOS);
                if(_OutlinePlantWind>0.5)
                {
                    // Matches this project's SG_Plant VertexDescription, including time and UV1 weighting.
                    float sway=WindNoise((positionWS.xy+_TimeParameters.x*float2(0.6,0.4)*_OutlineWind.z)*_OutlineWind.y)*_OutlineWind.x;
                    if(_OutlinePlantWind>1.5)sway*=(_OutlineWind.w>0.5)?i.uv1.y:i.uv.y;
                    else sway*=(_OutlineWind.w>0.5)?saturate(i.uv1.y-_OutlineWindFalloff):1;
                    positionWS+=float3(sway,0,sway);
                }
                o.positionCS=TransformWorldToHClip(positionWS);o.uv=i.uv*_SourceAlphaMap_ST.xy+_SourceAlphaMap_ST.zw;return o;
            }
            float EyeDepth(float raw)
            {
                #if UNITY_REVERSED_Z
                    float ortho=lerp(_ProjectionParams.z,_ProjectionParams.y,raw);
                #else
                    float ortho=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                #endif
                return lerp(LinearEyeDepth(raw,_ZBufferParams),ortho,unity_OrthoParams.w);
            }
            float4 ObjectFragment(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                clip(_GlobalOutlineObjectId-0.5);
                if(_SourceAlphaClip>0.5)
                {
                    float4 sample=SAMPLE_TEXTURE2D(_SourceAlphaMap,sampler_SourceAlphaMap,i.uv);
                    clip(lerp(sample.a,sample.r,_SourceAlphaRed)-_SourceCutoff);
                }
                float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
                float surface=EyeDepth(i.positionCS.z),scene=EyeDepth(SampleSceneDepth(uv));
                clip(_OutlineDepthTolerance-abs(surface-scene));
                return _GlobalOutlineObjectId+step(0.5,_OutlineCanopyMask)*0.25;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
