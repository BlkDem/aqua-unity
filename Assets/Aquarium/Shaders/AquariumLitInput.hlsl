#ifndef AQUARIUM_LIT_INPUT_INCLUDED
#define AQUARIUM_LIT_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "AquariumCommon.hlsl"

// glTF metallic-roughness: base colour x texture, roughness = factor x G, metallic = factor x B
CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Cutoff;
    half _Roughness;
    half _Metallic;
    half _BumpScale;
    half4 _EmissionColor;
    half _CausticStrength;
    half _Sway;
    half _SwayFromUV3;
    half _UseFog;
    half _VertexColor;
CBUFFER_END

TEXTURE2D(_OrmMap);        SAMPLER(sampler_OrmMap);

// height above the root for sway: object-space y, or (merged meshes) a value stored per vertex
float AqSwayHeight(float3 positionOS, float2 uv3)
{
    return _SwayFromUV3 > 0.5 ? uv3.x : positionOS.y;
}

float3 AqObjectToWorldSwayed(float3 positionOS, float2 uv3)
{
    float3 positionWS = TransformObjectToWorld(positionOS);
    return AqSway(positionWS, AqSwayHeight(positionOS, uv3), _Sway);
}

#endif
