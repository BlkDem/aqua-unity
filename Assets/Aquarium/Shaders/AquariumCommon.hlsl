// Aquarium-wide shader features shared by every material: light caustics on everything under
// water and plants swaying in the current (and pushed aside by passing fish). Ported from the
// web version's shaderPatch.js. Globals are set by AquariumClock / DayCycle.
#ifndef AQUARIUM_COMMON_INCLUDED
#define AQUARIUM_COMMON_INCLUDED

float _AqTime;
float _AqCaustic;
float3 _AqCausticColor;
float2 _AqCurrent;
float _AqNight;
float4 _AqFish[8];   // xyz: position, w: strength of the push (0: none)

// GLSL's mod (HLSL's fmod truncates towards zero, which breaks the pattern for negative input)
float2 AqMod(float2 x, float y) { return x - y * floor(x / y); }

// a classic water caustic: overlapping wavy rings, sharpened into bright filaments
float AqCausticWave(float2 uv, float time)
{
    float2 p = AqMod(uv * 6.28318, 6.28318) - 250.0;
    float2 i = p;
    float c = 1.0;
    const float inten = 0.005;
    [unroll] for (int n = 0; n < 4; n++)
    {
        float t = time * (1.0 - (3.5 / float(n + 1)));
        i = p + float2(cos(t - i.x) + sin(t + i.y), sin(t - i.y) + cos(t + i.x));
        c += 1.0 / length(float2(p.x / (sin(i.x + t) / inten), p.y / (cos(i.y + t) / inten)));
    }
    c /= 4.0;
    c = 1.17 - pow(c, 1.4);
    return pow(abs(c), 8.0);
}

// light added by caustics to a surface of the given albedo (zero outside the water)
float3 AqCaustics(float3 positionWS, float3 normalWS, float3 albedo, float strength)
{
    bool inside = abs(positionWS.x) < 6.0 && abs(positionWS.z) < 2.6 && positionWS.y < 5.7;
    if (!inside || strength <= 0.0) return 0;
    float up = saturate(normalWS.y * 0.75 + 0.35);
    float depthFade = lerp(0.55, 1.0, saturate(positionWS.y / 5.5));
    float g = AqCausticWave(positionWS.xz * 0.23 + positionWS.y * 0.05, _AqTime * 0.55);
    float3 cc = clamp(float3(g * 0.95, g, g * 1.08) * 1.1, 0.0, 3.0);
    return albedo * cc * _AqCausticColor * _AqCaustic * strength * up * depthFade * 1.6;
}

// sway: h is the height of the vertex above the plant's root (object space), strength per material
float3 AqSway(float3 positionWS, float h, float strength)
{
    if (strength <= 0.0) return positionWS;
    h = max(h, 0.0);
    float hs = pow(h, 1.35) * strength;
    float ph = positionWS.x * 0.7 + positionWS.z * 0.45;
    float2 sw = float2(
        sin(_AqTime * 0.9 + ph) + 0.45 * sin(_AqTime * 2.3 + positionWS.y * 1.7 + ph * 2.0),
        0.6 * cos(_AqTime * 0.7 + ph * 1.3) + 0.3 * sin(_AqTime * 1.9 + positionWS.y * 2.1)) * 0.07 + _AqCurrent;
    float2 push = 0;
    [unroll] for (int k = 0; k < 8; k++)
    {
        float3 d = positionWS - _AqFish[k].xyz;
        float rad = _AqFish[k].w * 2.6 + 0.001;
        float f = _AqFish[k].w * smoothstep(rad, 0.0, length(d));
        push += normalize(d.xz + 1e-4) * f;
    }
    positionWS.xz += sw * hs + push * 0.5 * min(h, 1.2);
    positionWS.y -= dot(sw * hs, sw * hs) * 0.6;
    return positionWS;
}

#endif
