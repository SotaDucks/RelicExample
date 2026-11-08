Shader "RelicExample/Global Pixel Outline"
{
    Properties
    {
        _OutlineEnabled("Enabled", Float) = 1
        _OutlineWidth("Outer contour width in render pixels", Range(0,6)) = 2
        _OutlineColor("Ink color", Color) = (0.005,0.006,0.008,1)
        _OutlineOpacity("Contour darkness", Range(0,1)) = 1
        _RimHighlight("Subtle contour highlight", Range(0,0.3)) = 0.12
        [HideInInspector] _OutlineDebug("Debug object identities",Float)=0
        _SuppressCanopyHoles("Ignore interior leaf gaps",Float)=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off
        Pass
        {
            Name "Outer Silhouette Only"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment OutlineFragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D_X(_GlobalOutlineMask);
            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float _OutlineEnabled, _OutlineWidth, _OutlineOpacity, _RimHighlight,_OutlineDebug,_SuppressCanopyHoles;
            CBUFFER_END
            float ObjectAt(float2 uv){return floor(SAMPLE_TEXTURE2D_X_LOD(_GlobalOutlineMask,sampler_PointClamp,saturate(uv),0).r+0.001);}
            float InteriorCanopyGap(float2 uv,float id)
            {
                if(_SuppressCanopyHoles<0.5)return 0;
                // Look for the SAME object's coverage on all four sides of a transparent gap.
                // An open notch on the outside has an escape direction and retains its outline.
                float4 enclosed=0;
                const float steps[12]={1,2,3,4,6,8,11,15,20,26,33,42};
                [loop]for(int k=0;k<12;k++)
                {
                    float2 d=_BlitTexture_TexelSize.xy*steps[k];
                    enclosed=max(enclosed,1-step(0.5,abs(float4(ObjectAt(uv+float2(d.x,0)),ObjectAt(uv-float2(d.x,0)),ObjectAt(uv+float2(0,d.y)),ObjectAt(uv-float2(0,d.y)))-id)));
                    if(dot(enclosed,float4(1,1,1,1))>3.5)return 1;
                }
                return 0;
            }
            float EyeDepth(float raw)
            {
                #if UNITY_REVERSED_Z
                    float orthographic=lerp(_ProjectionParams.z,_ProjectionParams.y,raw);
                #else
                    float orthographic=lerp(_ProjectionParams.y,_ProjectionParams.z,raw);
                #endif
                return lerp(LinearEyeDepth(raw,_ZBufferParams),orthographic,unity_OrthoParams.w);
            }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=input.texcoord;
                half4 source=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_PointClamp,uv);
                if (_OutlineEnabled<0.5 || _OutlineWidth<=0.001) return source;
                float rawObjectId=SAMPLE_TEXTURE2D_X(_GlobalOutlineMask,sampler_PointClamp,uv).r;
                float objectId=floor(rawObjectId+0.001),canopy=step(0.125,frac(rawObjectId));
                float selected=step(0.5,objectId);
                if(_OutlineDebug>0.5)return float4(frac(objectId*float3(0.6180339,0.381966,0.7548776))*selected,1);
                // Point-sampled pixel art needs at least one full render pixel for any enabled stroke.
                float pixelWidth=max(1,ceil(_OutlineWidth));
                float2 pixel=_BlitTexture_TexelSize.xy*pixelWidth;
                float2 offsets[4]={float2(pixel.x,0),float2(-pixel.x,0),float2(0,pixel.y),float2(0,-pixel.y)};
                float neighborMax=0,nearestSelectedDepth=_ProjectionParams.z,nearestObjectId=0,nearestCanopy=0;
                float centerDepth=EyeDepth(SampleSceneDepth(uv));
                float worldPixel=lerp(centerDepth*2/abs(UNITY_MATRIX_P._m11),unity_OrthoParams.y,unity_OrthoParams.w)*_BlitTexture_TexelSize.y;
                float innerStroke=0,highlightBand=0;
                [unroll]for(int n=0;n<4;n++)
                {
                    float2 neighborUV=saturate(uv+offsets[n]);
                    float rawNeighbor=SAMPLE_TEXTURE2D_X(_GlobalOutlineMask,sampler_PointClamp,neighborUV).r;
                    float neighborId=floor(rawNeighbor+0.001);
                    float mask=step(0.5,neighborId);neighborMax=max(neighborMax,mask);
                    float neighborDepth=EyeDepth(SampleSceneDepth(neighborUV));
                    if(mask>0.5&&neighborDepth<=nearestSelectedDepth){nearestSelectedDepth=neighborDepth;nearestObjectId=neighborId;nearestCanopy=step(0.125,frac(rawNeighbor));}
                    float different=step(0.5,abs(neighborId-objectId));
                    // Each visible object keeps its own boundary even when another object touches it.
                    float front=step(centerDepth,neighborDepth+max(0.02,worldPixel));
                    float innerGap=0;
                    if(canopy>0.5&&different>0.5)innerGap=InteriorCanopyGap(neighborUV,objectId);
                    innerStroke=max(innerStroke,selected*different*max(1-mask,front)*(1-innerGap));
                    float2 fartherUV=saturate(uv+offsets[n]*((pixelWidth+1)/pixelWidth));
                    float fartherId=ObjectAt(fartherUV);
                    float highlightGap=0;
                    if(canopy>0.5&&abs(fartherId-objectId)>0.5)highlightGap=InteriorCanopyGap(fartherUV,objectId);
                    highlightBand=max(highlightBand,selected*step(0.5,abs(fartherId-objectId))*(1-highlightGap));
                }
                // No color, depth or normal change inside the silhouette creates a line.
                float outer=(1-selected)*step(0.5,neighborMax);
                if(outer>0.5&&nearestCanopy>0.5)outer*=1-InteriorCanopyGap(uv,nearestObjectId);
                // Do not paint an outline over a character or other foreground occluder.
                outer*=step(nearestSelectedDepth-max(0.02,worldPixel),centerDepth);
                source.rgb*=1+highlightBand*(1-innerStroke)*_RimHighlight;
                source.rgb=lerp(source.rgb,_OutlineColor.rgb,max(innerStroke,outer)*_OutlineOpacity);
                return source;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
