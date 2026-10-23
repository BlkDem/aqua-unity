// Passes of Aquarium/Lit, modelled on URP's LitForwardPass / ShadowCasterPass / DepthOnlyPass,
// with sway applied to the vertices in every pass (so shadows and depth move with the leaves)
// and caustics added to the lit colour.
#ifndef AQUARIUM_LIT_PASSES_INCLUDED
#define AQUARIUM_LIT_PASSES_INCLUDED

#include "AquariumLitInput.hlsl"

// ------------------------------------------------------------------ forward
#ifdef AQ_FORWARD
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 texcoord : TEXCOORD0;
    float2 uv3 : TEXCOORD3;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
#if defined(_NORMALMAP)
    half4 tangentWS : TEXCOORD3;
#endif
    half4 color : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings AqLitVertex(Attributes input)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    float3 positionWS = AqObjectToWorldSwayed(input.positionOS.xyz, input.uv3);
    VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
    o.positionWS = positionWS;
    o.positionCS = TransformWorldToHClip(positionWS);
    o.normalWS = n.normalWS;
#if defined(_NORMALMAP)
    o.tangentWS = half4(n.tangentWS, input.tangentOS.w * GetOddNegativeScale());
#endif
    o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    o.color = _VertexColor > 0.5 ? input.color : half4(1, 1, 1, 1);
    o.fogFactor = _UseFog > 0.5 ? ComputeFogFactor(o.positionCS.z) : 0;
    OUTPUT_SH4(positionWS, o.normalWS, GetWorldSpaceNormalizeViewDir(positionWS), o.vertexSH, o.probeOcclusion);
    return o;
}

half4 AqLitFragment(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    half4 albedoAlpha = SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)) * _BaseColor * input.color;
#if defined(_ALPHATEST_ON)
    clip(albedoAlpha.a - _Cutoff);
#endif
    half4 orm = SAMPLE_TEXTURE2D(_OrmMap, sampler_OrmMap, input.uv);

    SurfaceData s = (SurfaceData)0;
    s.albedo = albedoAlpha.rgb;
    s.alpha = 1;
    s.metallic = saturate(_Metallic * orm.b);
    s.smoothness = 1.0 - saturate(_Roughness * orm.g);
    s.specular = 0;
    s.occlusion = 1;
    s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
    s.normalTS = SampleNormal(input.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);

    // two-sided leaves: the back face uses the flipped normal
    float3 normalWS = input.normalWS * IS_FRONT_VFACE(facing, 1.0, -1.0);

    InputData d = (InputData)0;
    d.positionWS = input.positionWS;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
#if defined(_NORMALMAP)
    float sgn = input.tangentWS.w;
    float3 bitangent = sgn * cross(normalWS, input.tangentWS.xyz);
    half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, normalWS);
    d.tangentToWorld = tangentToWorld;
    d.normalWS = TransformTangentToWorld(s.normalTS, tangentToWorld);
#else
    d.normalWS = normalWS;
#endif
    d.normalWS = NormalizeNormalPerPixel(d.normalWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
#endif
    d.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1.0), input.fogFactor);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    d.bakedGI = SAMPLE_GI(input.vertexSH, GetAbsolutePositionWS(d.positionWS), d.normalWS, d.viewDirectionWS, input.positionCS.xy, input.probeOcclusion, d.shadowMask);
#else
    d.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);
#endif

    half4 color = UniversalFragmentPBR(d, s);
    color.rgb += AqCaustics(input.positionWS, d.normalWS, s.albedo, _CausticStrength);
    if (_UseFog > 0.5) color.rgb = MixFog(color.rgb, d.fogCoord);
    color.a = 1;
    return color;
}
#endif

// ------------------------------------------------------------------ shadows and depth
#if defined(AQ_SHADOW) || defined(AQ_DEPTH)
#if defined(AQ_SHADOW)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
float3 _LightDirection;
float3 _LightPosition;
#endif

struct DepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 texcoord : TEXCOORD0;
    float2 uv3 : TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct DepthVaryings
{
    float2 uv : TEXCOORD0;
    float3 normalWS : TEXCOORD1;
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

DepthVaryings AqDepthVertex(DepthAttributes input)
{
    DepthVaryings o = (DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 positionWS = AqObjectToWorldSwayed(input.positionOS.xyz, input.uv3);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    o.normalWS = normalWS;
#if defined(AQ_SHADOW)
    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
        float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirectionWS = _LightDirection;
    #endif
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS)));
#else
    o.positionCS = TransformWorldToHClip(positionWS);
#endif
    return o;
}

half4 AqDepthFragment(DepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
#if defined(_ALPHATEST_ON)
    clip(SampleAlbedoAlpha(input.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a * _BaseColor.a - _Cutoff);
#endif
#if defined(AQ_DEPTH_NORMALS)
    return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
#else
    return input.positionCS.z;
#endif
}
#endif

#endif
