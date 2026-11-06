// Passes of Aquarium/Fish: the body bends around a spine (a travelling wave, head to tail), the
// caudal fin pivots at the peduncle with a lag towards its tip, soft fins ripple and the
// pectorals paddle around their roots. Ported from the web version's shaderPatch.js.
// The solid fins of a sculpted model (marked by the web version's tools/fin_map.py) keep moving
// on their own: the dorsal and anal fins carry a wave and fold down at speed, the tail fans and
// its rim flutters, the pelvic pair beats and spreads to brake, every rim flickers a little.
// _MEMBRANE draws those fins as skin between rays (the fin mesh of FinMembrane.cs), see MembraneSurface.
// AQ_DEPTH is what goes into the depth texture: the bodies, and the solid part of the fins.
// The face (FishFace.cs): the jaws turn around the corner of the mouth, the gill covers lift, a slit
// opens behind them. Per fish _Face = (mouth open 0..1, gill covers open 0..1).
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
    half _FinClear;
    half _FinDense;
    half _FinBlur;
    half _FinGlow;
    float4 _Jaw;    // the corner of the mouth (z, y), how far the jaw turns, how much the gill slit shows
    float4 _Gill;   // the gill cover as an ellipse: centre (z, y), radii (z, y)
CBUFFER_END

UNITY_INSTANCING_BUFFER_START(FishProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Wag)
    UNITY_DEFINE_INSTANCED_PROP(float, _Pect)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FinA)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FinB)
    UNITY_DEFINE_INSTANCED_PROP(float4, _Face)
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
    float4 fin : TEXCOORD3;    // x: fin type + 0.98 u (0 body, 1 tail, 2 dorsal, 3 anal, 4 pelvic, 5 pectoral in the body, 6 pectoral of its own), y: ray number, z: skin
    float3 face : TEXCOORD4;   // jaw weight, gill weight, 1 inside the mouth
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
    if (type > 5.5)
    {
        // a pectoral of its own paddles around its root (FishDeform): here only the soft rim gives
        p.x += flick * 0.6;
    }
    else if (type < 1.5)
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

// the jaws and the gill covers, around the still head
void FaceMotion(float3 face, float2 open, inout float3 p, inout float3 n)
{
    float a = face.x * open.x * _Jaw.z;
    if (abs(a) > 1e-4)
    {
        float c = cos(a), s = sin(a);
        float2 q = p.zy - _Jaw.xy;
        p.zy = float2(c * q.x + s * q.y, -s * q.x + c * q.y) + _Jaw.xy;
        n.zy = float2(c * n.z + s * n.y, -s * n.z + c * n.y);
    }
    float g = face.y * open.y;
    if (g > 1e-4)
    {
        // hanging from its front edge: out and a little back, and it faces more forward
        float lift = g * _Gill.z * 0.3;
        p.x += (p.x >= 0.0 ? lift : -lift);
        p.z -= lift * 0.35;
        n = normalize(n + float3(0.0, 0.0, g * 0.5));
    }
}

// the moving fish in object space
void FishDeform(FishAttributes i, out float3 p, out float3 n)
{
    float4 W = UNITY_ACCESS_INSTANCED_PROP(FishProps, _Wag);
    float4 FA = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FinA), FB = UNITY_ACCESS_INSTANCED_PROP(FishProps, _FinB);
    p = i.positionOS.xyz;
    n = i.normalOS;
#if !defined(_FINS) && !defined(_MEMBRANE)
    FaceMotion(i.face, UNITY_ACCESS_INSTANCED_PROP(FishProps, _Face).xy, p, n);
#endif
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

#if defined(_MEMBRANE)
// root -> rim 0..1, ray number, skin 0..1
float3 FinCoords(float4 fin)
{
    float type = floor(fin.x + 1e-3);
    return float3(saturate((fin.x - type) / 0.98), fin.y, fin.z);
}

// Skin between rays, by fin = (root -> rim, ray number, skin). The rays fork towards the rim and melt
// into an average when smaller than a pixel. Pigment holds the opacity (pale skin is nearly clear), the
// skin sags between the rays, a sheet seen edge-on is denser, and the rim thins out, frayed. Where
// skin is 0 the fin meets the body and looks like it: opaque, no rays.
void MembraneSurface(float3 fin, float3 viewWS, float3 positionWS, inout float3 normalWS, inout half3 albedo, inout half alpha, out float rayLine, out float skin)
{
    float u = fin.x, x = fin.y;
    skin = smoothstep(0.0, 1.0, fin.z);
    float pixel = fwidth(x);
    float c = frac(x) - 0.5;
    float fork = smoothstep(0.45, 0.85, u) * 0.23;
    float halfWidth = lerp(0.13, 0.05, u);
    float line01 = 1.0 - smoothstep(halfWidth - pixel, halfWidth + pixel, abs(abs(c) - fork));
    float show = saturate(1.8 - pixel * 3.0);
    rayLine = lerp(2.0 * halfWidth, line01, show) * skin;

    float3 n = normalize(normalWS);
    float3 dx = ddx(positionWS), dy = ddy(positionWS);
    float3 across = cross(dy, n) * ddx(x) + cross(n, dx) * ddy(x);
    float lenAcross = length(across);
    if (lenAcross > 1e-8)
    {
        across *= sign(dot(n, cross(dx, dy))) / lenAcross;
        n = normalize(n + across * sin(6.2832 * x) * 0.3 * show * skin);
    }
    // thin skin has no wrong side: on a folded lobe the normals pulled to the fin's plane may point away
    if (dot(n, viewWS) < 0.0) n = lerp(n, -n, skin);
    normalWS = n;

    half peak = max(albedo.r, max(albedo.g, albedo.b));
    half pigment = saturate((peak - min(albedo.r, min(albedo.g, albedo.b))) * 1.3 + (1.0 - peak) * 0.9);
    float a = lerp(_FinClear, _FinDense, pigment);
    a = lerp(a, saturate(a + 0.22), rayLine);
    a = 1.0 - pow(saturate(1.0 - a), 1.0 / max(abs(dot(n, viewWS)), 0.3));
    a *= 1.0 - 0.8 * smoothstep(0.84, 1.0, u + (1.0 - rayLine) * 0.07);
    alpha = saturate(lerp(1.0, a, skin) * _Opacity);
    albedo *= lerp(1.0, lerp(1.05, 0.78, rayLine), skin);
}
#endif

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
#if defined(_MEMBRANE)
    float3 fin : TEXCOORD6;    // see FinCoords
#elif !defined(_FINS)
    float3 face : TEXCOORD6;   // jaw weight, gill weight, 1 inside the mouth
    float2 rest : TEXCOORD7;   // (z, y) in the still model
#endif
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
#if defined(_MEMBRANE)
    o.fin = FinCoords(i.fin);
#elif !defined(_FINS)
    o.face = i.face;
    o.rest = i.positionOS.zy;
#endif
    OUTPUT_SH4(o.positionWS, o.normalWS, GetWorldSpaceNormalizeViewDir(o.positionWS), o.vertexSH, o.probeOcclusion);
    return o;
}

#if !defined(_FINS) && !defined(_MEMBRANE)
// The inside of the mouth: dark flesh by the lips, darker deep in. The slit behind a lifted gill
// cover: only along its rear edge, widest in the middle, as wide as the cover is open. By the cover
// it is the skin in the cover's shadow, further out and with the cover well open the red of the gills.
void FaceSurface(float3 face, float2 rest, float gillOpen, inout SurfaceData s, inout float caustics)
{
    if (face.z > 0.5)
    {
        float across = saturate(face.x * 0.8 + 0.2);
        s.albedo = lerp(half3(0.07, 0.008, 0.01), half3(0.004, 0.0005, 0.001), saturate(5.0 * across * (1.0 - across)));
        s.smoothness = 0.05;
        s.metallic = 0;
        s.emission = 0;
        caustics = 0;
        return;
    }
    if (gillOpen <= 0.001 || face.y <= 0.001) return;
    float2 q = (rest - _Gill.xy) / _Gill.zw;
    float r = length(q);
    float rear = smoothstep(0.3, 0.85, -q.x);
    if (rear <= 0.0) return;
    float wide = (0.02 + 0.12 * gillOpen) * rear;
    float out01 = (r - 1.0) / wide;
    float slit = smoothstep(-0.6, 0.0, out01) * (1.0 - smoothstep(0.35, 1.0, out01))
        * saturate(face.y * 10.0) * saturate(gillOpen * 6.0) * _Jaw.w;
    if (slit <= 0.0) return;
    half3 inside = lerp(s.albedo * 0.16, half3(0.2, 0.02, 0.03), saturate(out01 * 1.5) * saturate(gillOpen * 1.6 - 0.2));
    s.albedo = lerp(s.albedo, inside, slit);
    s.smoothness *= 1.0 - slit;
    s.emission *= 1.0 - slit;
    caustics *= 1.0 - slit;
}
#endif

half4 FishFragment(Varyings i, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
#if defined(_MEMBRANE)
    // the sculpt's colours without its fine relief (it reads as scales on a fin), sharp at the root to meet the body
    half4 albedoAlpha = SAMPLE_TEXTURE2D_BIAS(_BaseMap, sampler_BaseMap, i.uv, _FinBlur * i.fin.z) * _BaseColor * i.color;
#else
    half4 albedoAlpha = SampleAlbedoAlpha(i.uv, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)) * _BaseColor * i.color;
#endif
    SurfaceData s = (SurfaceData)0;
    s.albedo = albedoAlpha.rgb;
    s.alpha = saturate(albedoAlpha.a * _Opacity);
    s.metallic = _Metallic;
    s.smoothness = 1.0 - _Roughness;
    s.occlusion = 1;
    s.emission = _EmissionColor.rgb;
    s.normalTS = SampleNormal(i.uv, TEXTURE2D_ARGS(_BumpMap, sampler_BumpMap), _BumpScale);
    float caustics = _CausticStrength;
#if !defined(_FINS) && !defined(_MEMBRANE)
    FaceSurface(i.face, i.rest, UNITY_ACCESS_INSTANCED_PROP(FishProps, _Face).y, s, caustics);
#endif

    float3 normalWS = i.normalWS * IS_FRONT_VFACE(facing, 1.0, -1.0);
    InputData d = (InputData)0;
    d.positionWS = i.positionWS;
    d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
#if defined(_MEMBRANE)
    float rayLine, skin;
    MembraneSurface(i.fin, d.viewDirectionWS, i.positionWS, normalWS, s.albedo, s.alpha, rayLine, skin);
#endif
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
    color.rgb += AqCaustics(i.positionWS, d.normalWS, s.albedo, caustics);
#if defined(_MEMBRANE)
    // thin skin: the light on its far side comes through, tinted by the skin (less through a ray)
    Light sun = GetMainLight(d.shadowCoord, d.positionWS, d.shadowMask);
    float through = saturate(0.35 - dot(d.normalWS, sun.direction) * 0.65) + 0.8 * pow(saturate(dot(d.viewDirectionWS, -sun.direction)), 4.0);
    color.rgb += s.albedo * sun.color * sun.shadowAttenuation * through * _FinGlow * (1.0 - 0.6 * rayLine) * skin;
#endif
    if (_Glow > 0)
    {
        float fr = pow(1.0 - abs(dot(d.normalWS, d.viewDirectionWS)), 2.0);
        color.rgb += s.albedo * _Glow * (0.6 + fr * 2.0);
    }
    color.rgb = MixFog(color.rgb, d.fogCoord);
#if defined(_FINS) || defined(_MEMBRANE)
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

// ------------------------------------------------------------------ depth
#ifdef AQ_DEPTH
// the fin as a whole counts, all but its thinning rim: below this it goes with what is behind it
static const float FIN_SOLID = 0.1;

struct DepthVaryings
{
    float2 uv : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS : TEXCOORD2;
    half4 color : TEXCOORD3;
    float3 fin : TEXCOORD4;
    float4 positionCS : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

DepthVaryings FishDepthVertex(FishAttributes i)
{
    DepthVaryings o = (DepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_TRANSFER_INSTANCE_ID(i, o);
    float3 p, n;
    FishDeform(i, p, n);
    o.positionWS = TransformObjectToWorld(p);
    o.positionCS = TransformWorldToHClip(o.positionWS);
    o.normalWS = TransformObjectToWorldNormal(n);
    o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
    o.color = i.color * half4(UNITY_ACCESS_INSTANCED_PROP(FishProps, _Tint).rgb, 1);
#if defined(_MEMBRANE)
    o.fin = FinCoords(i.fin);
#endif
    return o;
}

half4 FishDepthFragment(DepthVaryings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
#if defined(_FINS) || defined(_MEMBRANE)
    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor * i.color;
    half alpha = saturate(tex.a * _Opacity);
    #if defined(_MEMBRANE)
    float rayLine, skin;
    float3 surfaceNormal = i.normalWS;
    half3 albedo = tex.rgb;
    MembraneSurface(i.fin, GetWorldSpaceNormalizeViewDir(i.positionWS), i.positionWS, surfaceNormal, albedo, alpha, rayLine, skin);
    #endif
    clip(alpha - FIN_SOLID);
#endif
#if defined(AQ_DEPTH_NORMALS)
    return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
#else
    return i.positionCS.z;
#endif
}
#endif

#endif
