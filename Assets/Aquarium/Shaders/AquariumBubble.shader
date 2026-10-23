// Air bubbles: hollow-looking spheres, clear in the middle with a bright rim, a reflection of
// the room and a faint thin-film shimmer (the web version uses iridescence for it).
Shader "Aquarium/Bubble"
{
    Properties
    {
        _Opacity("Opacity", Range(0, 1)) = 0.32
        _Rim("Rim", Range(0, 2)) = 0.9
        _Reflect("Reflection", Range(0, 3)) = 1.2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+12" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Opacity, _Rim, _Reflect;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; half fog : TEXCOORD2; };
            V vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS), v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float nv = saturate(dot(n, v));
                float fr = pow(1.0 - nv, 3.0);
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), 0.0h, 1.0h);
                Light l = GetMainLight();
                float spec = pow(saturate(dot(n, normalize(l.direction + v))), 400.0) * 4.0;
                // thin-film shimmer: hue shifts with the angle
                float3 film = 0.5 + 0.5 * cos(6.2831 * (nv * 1.6 + float3(0.0, 0.33, 0.67)));
                half3 c = env * _Reflect * (0.3 + fr) + half3(0.75, 0.95, 1.0) * fr * _Rim + film * fr * 0.25 + l.color * spec;
                half a = saturate(_Opacity * 0.4 + fr * 0.9 + spec);
                c = MixFog(c, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
