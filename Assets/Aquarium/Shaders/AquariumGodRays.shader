// Shafts of light under the surface: streaky, flickering, fading towards the bottom.
Shader "Aquarium/GodRays"
{
    Properties
    {
        _Color("Colour", Color) = (0.75, 0.96, 1, 1)
        _Intensity("Intensity", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AquariumCommon.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            V vert(A i) { V o; o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.uv = i.uv; return o; }
            float hsh(float n) { return frac(sin(n) * 43758.5453); }
            float n1(float x) { float i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f); return lerp(hsh(i), hsh(i + 1.0), f); }
            half4 frag(V i) : SV_Target
            {
                float t = _AqTime, x = i.uv.x, v = i.uv.y;
                float streak = n1(x * 9.0 + t * 0.25 + i.positionWS.z) * n1(x * 23.0 - t * 0.4 + 7.0);
                streak = pow(streak, 1.6) * 2.0;
                float edge = smoothstep(0.0, 0.25, x) * smoothstep(1.0, 0.75, x);
                float fade = smoothstep(0.0, 0.55, v) * (0.5 + 0.5 * v);
                float flick = 0.75 + 0.25 * sin(t * 0.7 + i.positionWS.x * 2.0);
                float a = streak * edge * fade * flick * _Intensity * 0.11 * 0.35;   // 0.35: the exposure here brightens additive light more than in the web version
                return half4(_Color.rgb * a, 1);
            }
            ENDHLSL
        }
    }
}
