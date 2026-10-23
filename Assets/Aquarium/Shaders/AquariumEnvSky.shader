// The soft studio environment the web version lights its scene with (a PMREM of this dome):
// dark below, cool blue-grey above, a broad overhead softbox and a faint blue glow behind.
// Used only to bake the reflection cubemap (the camera clears to a solid colour).
Shader "Aquarium/EnvSky"
{
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.dir = i.positionOS.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float y = d.y;
                float3 c = lerp(float3(0.03, 0.035, 0.05), float3(0.45, 0.6, 0.7), smoothstep(-0.3, 0.9, y));
                c += float3(1.0, 0.97, 0.9) * pow(max(y, 0.0), 6.0) * 1.2;
                c += float3(0.2, 0.45, 0.6) * pow(max(dot(d, normalize(float3(0.0, 0.2, -1.0))), 0.0), 4.0) * 0.4;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
