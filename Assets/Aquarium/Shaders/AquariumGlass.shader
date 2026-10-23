// Tank glass: nearly clear, with a Fresnel reflection of the room and a sharp sun glint.
Shader "Aquarium/Glass"
{
    Properties
    {
        _Tint("Tint (alpha: base opacity)", Color) = (0.91, 0.98, 1, 0.1)
        _Reflect("Reflection", Range(0, 2)) = 0.6
        _Glint("Glint", Range(0, 8)) = 2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Reflect;
                half _Glint;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            V vert(A i)
            {
                UNITY_SETUP_INSTANCE_ID(i);
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float nv = saturate(dot(n, v));
                float fres = 0.04 + 0.96 * pow(1.0 - nv, 5.0);
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), 0.02h, 1.0h);
                Light l = GetMainLight();
                float3 h = normalize(l.direction + v);
                float glint = pow(saturate(dot(n, h)), 900.0) * _Glint;
                half3 c = _Tint.rgb * _Tint.a + env * fres * _Reflect + l.color * glint;
                half a = saturate(_Tint.a + fres * _Reflect * 0.6 + glint);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
