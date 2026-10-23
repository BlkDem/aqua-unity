// Fine particles drifting in the water, brighter inside the light shafts; a few big soft motes.
// Each particle is a quad (uv: corner, uv2.x: seed) turned to the camera in the vertex shader.
Shader "Aquarium/Dust"
{
    Properties
    {
        _Color("Colour", Color) = (0.85, 0.96, 1, 1)
        _Ray("Brightness", Float) = 1
        _Size("Size (world units per point)", Float) = 0.0075
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+15" "RenderPipeline" = "UniversalPipeline" }
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
                half _Ray;
                half _Size;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float2 corner : TEXCOORD0; float2 seed : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float2 corner : TEXCOORD0; float alpha : TEXCOORD1; float big : TEXCOORD2; };
            V vert(A i)
            {
                V o;
                float seed = i.seed.x, T = _AqTime;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                float t = T * (0.05 + seed * 0.05);
                p.x += sin(t * 3.1 + seed * 40.0) * 0.25 + sin(T * 0.02 + seed * 10.0) * 0.4;
                p.y += sin(t * 2.3 + seed * 13.0) * 0.18 - fmod(T * 0.01 * seed, 0.5);
                p.z += cos(t * 2.7 + seed * 27.0) * 0.2;
                float big = step(0.985, seed);
                float sz = lerp(1.2 + seed * 2.0, 10.0 + seed * 10.0, big) * _Size;
                float3 viewPos = TransformWorldToView(p);
                viewPos.xy += (i.corner - 0.5) * sz;
                o.positionCS = TransformWViewToHClip(viewPos);
                float ray = 0.5 + 0.5 * sin(p.x * 1.3 + p.y * 0.35 + T * 0.15) * sin(p.x * 0.7 - T * 0.11 + p.z);
                o.alpha = (0.25 + ray * 0.9) * lerp(1.0, 0.18, big) * smoothstep(0.2, 1.5, p.y);
                o.big = big;
                o.corner = i.corner;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float d = length(i.corner - 0.5);
                float a = lerp(smoothstep(0.5, 0.0, d), smoothstep(0.5, 0.35, d) * 0.6 + smoothstep(0.35, 0.0, d) * 0.4, i.big);
                return half4(_Color.rgb * a * i.alpha * _Ray * 0.5, 1);
            }
            ENDHLSL
        }
    }
}
