// Passes of Aquarium/Fish: the body bends around a spine (a travelling wave, head to tail), the
// caudal fin pivots at the peduncle with a lag towards its tip, soft fins ripple and the
// pectorals paddle around their roots. Ported from the web version's shaderPatch.js.
// The solid fins of a sculpted model (marked by the web version's tools/fin_map.py) keep moving
// on their own: the dorsal and anal fins carry a wave and fold down at speed, the tail fans and
// its rim flutters, the pelvic pair beats and spreads to brake, every rim flickers a little.
// Per fish (instanced): _Wag = (phase, amplitude, turn bend, body length), _Pect, _Tint,
// _FinA = (fin phase, dorsal/anal erect 0..1, fin activity 0..1, tail spread -1..1),
// _FinB = (left and right pectoral extra angle, pelvic spread 0..1, pelvic beat phase).
#ifndef AQUARIUM_FISH_PASSES_INCLUDED
#define AQUARIUM_FISH_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/SurfaceInput.hlsl"
#include "AquariumCommon.hlsl"

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor;
    half _Roughness;
    half _Metallic;
    half _BumpScale;
    half4 _EmissionColor;
    half _CausticStrength;
    half _Glow;
    half _Opacity;
CBUFFER_END

UNITY_INSTANCING_BUFFER_START(FishProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Wag)
    UNITY_DEFINE_INSTANCED_PROP(float, _Pect)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FinA)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FinB)
UNITY_INSTANCING_BUFFER_END(FishProps)

static const float FISH_K = 5.4;   // wave number * L: a bit over one body length per wave

// lateral spine offset at z and its slope along the swimming axis
float2 FishSpine(float4 W, float z, float lag)
{
    float L = W.w, k = FISH_K / L;
    float s = saturate((L * 0.5 - z) / L);
    // carangiform envelope: slight head recoil, still point at ~1/5 L, largest at the tail
    float env = 0.12 - 0.6 * s + 1.48 * s * s;
    float denv = (-0.6 + 2.96 * s) * (-1.0 / L);
    float a = W.x + k * z - lag;
    float zb = min(z, 0.0);
    float x = W.y * env * sin(a) + W.z * zb * zb;
    float dxdz = W.y * (denv * sin(a) + env * k * cos(a)) + 2.0 * W.z * zb;
    return float2(x, -dxdz);
}

float FishAngle(float4 W, float lag) { return clamp(atan(FishSpine(W, -0.47 * W.w, lag).y), -0.7, 0.7); }

// finU: 0 at a fin's root .. 1 at its tip (-1 for the body), finTail: 1 on the caudal fin
float3 FishBend(float4 W, float3 p, inout float3 n, float finU, float finTail)
{
    float L = W.w, zP = -0.47 * L;
    float2 sp = FishSpine(W, max(p.z, zP), 0.0);
    float phi = clamp(atan(sp.y), -0.6, 0.6);
    float3 q;
    if (p.z < zP)
    {
        float d = zP - p.z;
        float u = finTail > 0.5 ? finU : saturate(d / (0.3 * L));
        float phiM = FishAngle(W, 0.8 * u) * (1.0 - 0.15 * u), phiE = FishAngle(W, 1.6 * u) * (1.0 - 0.3 * u);
        q = float3(sp.x + d * sin(phiM) + p.x * cos(phiE), p.y, zP - d * cos(phiM) + p.x * sin(phiE));
        phi = phiE;
    }
    else
    {
        q = float3(sp.x + p.x * cos(phi), p.y, p.z + p.x * sin(phi));
    }
    if (finU > 0.0)
    {
        float fl = sin(2.0 * W.x - finU * 5.0 + p.y * 4.0 / L) * 0.6 + sin(_AqTime * 3.1 + W.x * 0.37 - finU * 3.0 + p.z * 9.0 / L) * 0.4;
        q.x += fl * pow(finU, 1.4) * (0.012 * L + 0.3 * abs(W.y)) * cos(phi) * sign(W.y + 1e-6);
    }
    float c = cos(phi), si = sin(phi);
    n = float3(c * n.x - si * n.z, n.y, si * n.x + c * n.z);
    return q;
}

struct FishAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float4 tangentOS : TANGENT;
    float2 uv : TEXCOORD0;
    float4 pect : TEXCOORD2;   // x: paddle direction (0 = not a pectoral), yzw: fin root
    float4 fin : TEXCOORD3;    // x: fin type + 0.98 u (0 body, 1 tail, 2 dorsal, 3 anal, 4 pelvic, 5 pectoral)
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// a solid fin moving on its own, before the body wave carries it along
void FinMotion(float code, float L, float4 A, float4 B, inout float3 p)
{
    float type = floor(code + 1e-3);
    if (type < 0.5) return;
    float u = saturate((code - type) / 0.98);
    float act = A.z;
    // every soft fin flickers at the rim: a small fast wave running out from the root
    float flick = sin(A.x * 2.3 - u * 4.0 + p.z * 11.0 / L + p.y * 7.0 / L) * pow(u, 1.4) * L * (0.012 + 0.03 * act);
    float side = p.x >= 0.0 ? 1.0 : -1.0;
    if (type < 1.5)
    {
        // tail: the rays fan out and close up, the rim flutters
        // seen from the side: the fan keeps opening and closing a little, a wave runs along its rim
        p.y *= 1.0 + (A.w * 0.2 + 0.12 * act * sin(A.x * 0.9)) * u;
        p.z += u * L * 0.025 * act * sin(A.x * 0.7 + p.y * 6.0 / L);
        p.x += flick * 1.5 + sin(A.x * 1.7 - abs(p.y) * 9.0 / L) * u * u * L * 0.035 * act;
    }
    else if (type < 3.5)
    {
        // dorsal (2) and anal (3): a wave runs front to back along the fin; folded down and back at speed
        float s = type < 2.5 ? 1.0 : -1.0, fold = 1.0 - A.y, h = abs(p.y);
        p.y -= s * fold * 0.45 * u * h;
        p.z -= fold * 0.3 * u * h;
        p.x += sin(A.x - p.z * 12.0 / L + s * 0.9) * pow(u, 1.2) * L * (0.03 + 0.07 * act) + flick;
        // seen from the side: the rays rise and fall one after another, the fin rocks to and fro
        p.y += s * pow(u, 1.2) * L * (0.02 + 0.04 * act) * sin(A.x * 0.8 - p.z * 10.0 / L);
        p.z += u * L * 0.03 * act * sin(A.x * 0.6 + s);
    }
    else if (type < 4.5)
    {
        // pelvic pair: beating out and in by turns, spread wide to brake and hover
        p.x += side * u * L * (0.07 * B.z + 0.06 * act * sin(B.w + side * 1.3)) + flick * 0.6;
        p.z += u * L * 0.07 * act * cos(B.w + side * 1.3);
        p.y += u * L * 0.03 * act * sin(B.w + side * 1.3);
    }
    else
    {
        // pectoral left in the body: sculls out and back with its side's angle
        float a = side > 0.0 ? B.x : B.y;
        p.x += side * u * L * 0.22 * a + flick * 0.6;
        p.z -= u * L * 0.08 * abs(a);
        p.y += u * L * 0.05 * a;
    }
}

// the moving fish in object space
void FishDeform(FishAttributes i, out float3 p, out float3 n)
{
    float4 W = UNITY_ACCESS_INSTANCED_PROP(FishProps, _Wag);
    float4 FA = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FinA), FB = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FinB);
    p = i.positionOS.xyz;
    n = i.normalOS;
    // pectorals paddle: in the fin mesh, or in the body of a sculpted model with solid fins
    if (i.pect.x != 0.0)
    {
        float3 q = p - i.pect.yzw;
        // each side has its own stroke on top of the shared one (turning, steadying the roll)
        float a = i.pect.x * (UNITY_ACCESS_INSTANCED_PROP(FishProps, _Pect) + (i.pect.x > 0.0 ? FB.x : FB.y));
        float ca = cos(a), sa = sin(a), cx = cos(0.3), sx = sin(0.3);
        q.xz = float2(ca * q.x + sa * q.z, -sa * q.x + ca * q.z);
        n.xz = float2(ca * n.x + sa * n.z, -sa * n.x + ca * n.z);
        q.yz = float2(cx * q.y - sx * q.z, sx * q.y + cx * q.z);
        n.yz = float2(cx * n.y - sx * n.z, sx * n.y + cx * n.z);
        p = q + i.pect.yzw;
    }
#if defined(_FINS)
    // fin atlas: left half of U = caudal fin, right half = other fins; U runs root -> tip
    float finTail = step(i.uv.x, 0.5);
    float finU = finTail > 0.5 ? i.uv.x * 2.0 : i.uv.x * 2.0 - 1.0;
#else
    float finU = -1.0, finTail = 0.0;
    FinMotion(i.fin.x, W.w, FA, FB, p);
#endif
    p = FishBend(W, p, n, finU, finTail);
}

// ------------------------------------------------------------------ forward
#ifdef AQ_FORWARD
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct Varyings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    half4 tangentWS : TEXCOORD3;
    half4 color : TEXCOORD4;
    half fogFactor : TEXCOORD5;
    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 8);
#ifdef USE_APV_PROBE_OCCLUSION
    float4 probeOcclusion : TEXCOORD10;
#endif
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

Varyings FishVertex(FishAttributes i)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_TRANSFER_INSTANCE_ID(i, o);
    float3 p, n;
    FishDeform(i, p, n);
    o.positionWS = TransformObjectToWorld(p);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    VertexNormalInputs ni = GetVertexNormalInputs(n, i.tangentOS);
    o.normalWS = ni.normalWS;
    o.tangentWS = half4(ni.tangentWS, i.tangentOS.w * GetOddNegativeScale());
    o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
    o.color = i.color * half4(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Tint).rgb, 1);
    o.fogFactor = ComputeFogFactor(o.positionCS.z);
    OUTPUT_SH4(o.positionWS, o.normalWS, GetWorldSpaceNormalizeViewDir(o.positionWS), o.vertexSH, o.probeOcclusion);
    return o;
}

half4 FishFragment(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    half4 albedoAlpha = SampleAlbedoAlpha(i.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)) * _BaseColor * i.color;
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedoAlpha.rgb;
    s.alpha = saturate(albedoAlpha.a * _Opacity);
    s.metallic = _Metallic;
    s.smoothness = 1.0 - _Roughness;
    s.occlusion = 1;
    s.emission = _EmissionColor.rgb;
    s.normalTS = SampleNormal(i.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);

    float3 normalWS = i.normalWS * IS_FRONT_VFACE(facing, 1.0, -1.0);
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
#if defined(_NORMALMAP)
    float3 bitangent = i.tangentWS.w * cross(normalWS, i.tangentWS.xyz);
    d.normalWS = TransformTangentToWorld(s.normalTS, half3x3(i.tangentWS.xyz, bitangent, normalWS));
#else
    d.normalWS = normalWS;
#endif
    d.normalWS = NormalizeNormalPerPixel(d.normalWS);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
#endif
    d.fogCoord = InitializeInputDataFog(float4(i.positionWS, 1.0), i.fogFactor);
    d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
#if !defined(LIGHTMAP_ON) && (defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2))
    d.bakedGI = SAMPLE_GI(i.vertexSH, GetAbsolutePositionWS(d.positionWS), d.normalWS, d.viewDirectionWS, i.positionCS.xy, i.probeOcclusion, d.shadowMask);
#else
    d.bakedGI = SAMPLE_GI(i.staticLightmapUV, i.vertexSH, d.normalWS);
    d.shadowMask = SAMPLE_SHADOWMASK(i.staticLightmapUV);
#endif
    half4 color = UniversalFragmentPBR(d, s);
    color.rgb += AqCaustics(i.positionWS, d.normalWS, s.albedo, _CausticStrength);
    if (_Glow > 0)
    {
        float fr = pow(1.0 - abs(dot(d.normalWS, d.viewDirectionWS)), 2.0);
        color.rgb += s.albedo * _Glow * (0.6 + fr * 2.0);
    }
    color.rgb = MixFog(color.rgb, d.fogCoord);
#if defined(_FINS)
    color.a = s.alpha;
#else
    color.a = 1;
#endif
    return color;
}
#endif

// ------------------------------------------------------------------ shadows
#ifdef AQ_SHADOW
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
float3 _LightDirection;

struct ShadowVaryings { float4 positionCS : SV_POSITION; };

ShadowVaryings FishShadowVertex(FishAttributes i)
{
    UNITY_SETUP_INSTANCE_ID(i);
    float3 p, n;
    FishDeform(i, p, n);
    ShadowVaryings o;
    float3 positionWS = TransformObjectToWorld(p);
    o.positionCS = ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(positionWS, TransformObjectToWorldNormal(n), _LightDirection)));
    return o;
}
half4 FishShadowFragment(ShadowVaryings i) : SV_Target { return 0; }
#endif

#endif
