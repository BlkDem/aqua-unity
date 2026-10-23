// The painted deep-water background behind the back glass: depth gradient, slow distant light
// rays and silhouettes of far plants and hills (the web version's backdrop, ported).
Shader "Aquarium/Backdrop"
{
    Properties
    {
        _Deep("Deep", Color) = (0.024, 0.196, 0.29, 1)
        _Top("Top", Color) = (0.1, 0.63, 0.72, 1)
        _Boost("Brightness (the web draws it untonemapped)", Float) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AquariumCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep;
                half4 _Top;
                half _Boost;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.uv = i.uv; return o; }

            float hsh(float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }
            float n2(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hsh(i), hsh(i + float2(1, 0)), f.x), lerp(hsh(i + float2(0, 1)), hsh(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _AqTime, night = _AqNight;
                float3 c = lerp(_Deep.rgb, _Top.rgb, pow(uv.y, 1.4));
                float r = 0;
                [unroll] for (int k = 0; k < 3; k++) { float fk = k; r += smoothstep(0.55, 1.0, n2(float2(uv.x * 6.0 + uv.y * 1.5 + fk * 7.0, t * 0.08 + fk))); }
                c += float3(0.4, 0.8, 0.9) * r * 0.12 * uv.y * (1.0 - night * 0.8);
                float hill = 0.18 + 0.08 * sin(uv.x * 7.0) + 0.05 * sin(uv.x * 17.0 + 1.0) + 0.03 * n2(float2(uv.x * 30.0, 1.0));
                float stems = 0;
                [unroll] for (int s = 0; s < 14; s++)
                {
                    float fs = s;
                    float x = frac(fs * 0.137 + 0.05) + 0.02 * sin(t * 0.3 + fs + uv.y * 4.0);
                    float w = 0.004 + 0.004 * frac(fs * 0.71);
                    float hh = 0.3 + 0.35 * frac(fs * 0.433);
                    stems = max(stems, (1.0 - smoothstep(w, w * 2.5, abs(uv.x - x - uv.y * 0.03 * sin(fs)))) * step(uv.y, hh));
                }
                float sil = max(1.0 - smoothstep(hill - 0.01, hill + 0.01, uv.y), stems * 0.7);
                c = lerp(c, _Deep.rgb * 0.45, sil * 0.75);
                c *= lerp(1.0, 0.25, night);
                c += float3(0.0, 0.02, 0.06) * night;
                return half4(c * _Boost, 1);
            }
            ENDHLSL
        }
    }
}
