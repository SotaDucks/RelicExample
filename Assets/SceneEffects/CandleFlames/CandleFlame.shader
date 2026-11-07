Shader "SceneEffects/Small Candle Flame"
{
    Properties
    {
        _Intensity("Flame brightness", Range(0.2, 5)) = 2.2
        _Flicker("Flicker", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "CandleFlame"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Flicker;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float phase : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.uv = input.uv;
                o.color = input.color;
                float3 anchor = TransformObjectToWorld(float3(0,0,0));
                o.phase = frac(sin(dot(anchor.xz, float2(12.9898,78.233))) * 43758.5453) * 6.28318;
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float y = saturate(i.uv.y);
                float t = _Time.y;
                float wobble = (sin(t*5.3+i.phase) + 0.38*sin(t*9.1+i.phase*1.7))*_Flicker;
                float x = (i.uv.x*2-1) - wobble*0.105*y*y;
                float width = 0.62*pow(max(0.0,sin(PI*pow(y,0.76))),0.85)*(1-0.42*y);
                width *= 1 + 0.06*_Flicker*sin(t*11.2+y*13+i.phase);
                float q = abs(x)/(width+0.002);
                float edge = 1-smoothstep(0.60,1.05,q);
                float base = smoothstep(0.0,0.075,y);
                float tip = 1-smoothstep(0.88,1.0,y);
                float alpha = edge*base*tip;
                float core = (1-smoothstep(0.15,0.67,q))*(1-smoothstep(0.32,0.78,y));
                half3 orange = half3(1.0,0.24,0.018);
                half3 yellow = half3(1.0,0.66,0.12);
                half3 ivory = half3(1.0,0.94,0.64);
                half3 col = lerp(orange,yellow,saturate(1-q)*0.9);
                col = lerp(col,ivory,core*0.92);
                float halo = exp(-q*q*1.3)*0.08*base*tip;
                alpha = saturate(alpha*0.70+halo) * i.color.a;
                return half4(col*_Intensity*(1+0.045*wobble),alpha);
            }
            ENDHLSL
        }
    }
}
