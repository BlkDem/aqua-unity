// The dark room around the aquarium: a big inside-out dome with a soft gradient and a glow
// behind the tank.
Shader "Aquarium/Room"
{
    Properties
    {
        _Top("Top", Color) = (0.106, 0.141, 0.212, 1)
        _Bottom("Bottom", Color) = (0.02, 0.027, 0.047, 1)
        _Glow("Glow", Color) = (0.165, 0.239, 0.361, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Front
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Top; half4 _Bottom; half4 _Glow;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = normalize(i.positionOS.xyz); return o; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float h = d.y * 0.5 + 0.5;
                float3 c = lerp(_Bottom.rgb, _Top.rgb, smoothstep(0.2, 0.9, h));
                c += _Glow.rgb * pow(saturate(dot(d, normalize(float3(0.0, 0.15, -1.0)))), 6.0) * 0.8;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
