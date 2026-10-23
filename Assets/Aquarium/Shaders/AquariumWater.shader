// Water surface: gentle waves (vertex), fine ripples (normals), Fresnel and sun glints. Seen
// from above and from below. The waves are the web version's, computed on the GPU here.
Shader "Aquarium/Water"
{
    Properties
    {
        _Color("Colour (alpha: opacity)", Color) = (0.16, 0.66, 0.75, 0.24)
        [HDR] _Emission("Emission", Color) = (0.02, 0.15, 0.18, 1)
        _Reflect("Reflection", Range(0, 2)) = 0.3
        _RippleScale("Ripple scale", Float) = 4
        _RippleStrength("Ripple strength", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AquariumCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Emission;
                half _Reflect;
                half _RippleScale;
                half _RippleStrength;
            CBUFFER_END

            // filter outlet in world space (web (5.3, -1.4) with x mirrored)
            static const float2 Outlet = float2(-5.3, -1.4);

            float Height(float2 p, float t)
            {
                float y = sin(p.x * 1.3 + t * 1.1) * 0.025 + sin(p.y * 2.1 - t * 1.4 + p.x * 0.4) * 0.02 + sin((p.x + p.y) * 3.3 + t * 2.2) * 0.008;
                float2 d = p - Outlet;
                float dd = dot(d, d);
                y += sin(t * 7.0 + sqrt(dd) * 9.0) * 0.03 * exp(-dd * 1.3);
                return y;
            }

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float fog : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                float3 p = TransformObjectToWorld(i.positionOS.xyz);
                p.y += Height(p.xz, _AqTime);
                o.positionWS = p;
                o.positionCS = TransformWorldToHClip(p);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(V i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float t = _AqTime;
                float2 p = i.positionWS.xz;
                const float e = 0.02;
                float hx = Height(p + float2(e, 0), t) - Height(p - float2(e, 0), t);
                float hz = Height(p + float2(0, e), t) - Height(p - float2(0, e), t);
                // fine ripples drifting with the current
                float2 q = p * _RippleScale + float2(t * 0.15, sin(t * 0.1) * 0.3);
                float2 r = float2(sin(q.x * 3.1 + cos(q.y * 2.3)) + sin(q.y * 1.7 + q.x * 0.9), cos(q.y * 2.9 + sin(q.x * 1.9)) + cos(q.x * 2.1 - q.y * 1.3));
                float3 n = normalize(float3(-hx / (2 * e) - r.x * _RippleStrength * 0.15, 1, -hz / (2 * e) - r.y * _RippleStrength * 0.15));
                float below = IS_FRONT_VFACE(facing, 0.0, 1.0);
                n *= 1.0 - 2.0 * below;
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float fres = 0.02 + 0.98 * pow(1.0 - saturate(dot(n, v)), 5.0);
                Light l = GetMainLight();
                float3 h = normalize(l.direction + v);
                float spec = pow(saturate(dot(n, h)), 600.0) * 3.0;
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), 0.05h, 1.0h);
                // from below the surface is mostly a window: keep its reflection and glow subdued
                float k = lerp(1.0, 0.4, below);
                half3 c = _Color.rgb * (l.color * 0.3 + SampleSH(n)) * _Color.a + _Emission.rgb * k + env * fres * _Reflect * k + l.color * spec;
                half a = saturate(_Color.a + (fres * _Reflect + spec) * k);
                c = MixFog(c, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
