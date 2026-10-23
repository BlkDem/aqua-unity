// Morning, day, evening and night (the web version's daycycle.js): one set of numbers per time
// of day, blended smoothly, driving the lights, the ambient, fog, caustics, the backdrop and
// room colours, the LED bar, glowing decor and the exposure.
//
// Intensities are the web version's. three.js divides diffuse light by pi and URP does not, so
// lights and the ambient are divided by pi here; emissive values carry over as they are.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium.World
{
    [Serializable]
    public sealed class DayPreset
    {
        public Color sun, sky, ground, fog, causticColor, rayColor, led, bgTop, bgDeep, roomTop, roomBottom, roomGlow;
        public float sunI, hemiI, fogDensity, caustic, rays, ledI, moon, exposure, dust, night, fill;

        public static DayPreset Lerp(DayPreset a, DayPreset b, float t, DayPreset o = null)
        {
            o ??= new DayPreset();
            foreach (var f in typeof(DayPreset).GetFields())
            {
                if (f.FieldType == typeof(Color)) f.SetValue(o, Color.Lerp((Color)f.GetValue(a), (Color)f.GetValue(b), t));
                else f.SetValue(o, Mathf.Lerp((float)f.GetValue(a), (float)f.GetValue(b), t));
            }
            return o;
        }

        public DayPreset Clone() => Lerp(this, this, 0);
    }

    [Serializable]
    public sealed class GlowMaterial
    {
        public Material material;
        [ColorUsage(false, true)] public Color baseEmission = Color.white;
        public float day = 0.15f, night = 3f;
        public bool flicker = true;
    }

    [ExecuteAlways]
    public sealed class DayCycle : MonoBehaviour
    {
        public enum Mode { Auto, Day, Evening, Night }

        static Color H(string hex) => WebSpace.Hex(hex);

        public static DayPreset Day() => new DayPreset
        {
            sun = H("#fff1db"), sunI = 2.3f, sky = H("#9fdcf0"), ground = H("#3a2e1c"), hemiI = 0.45f,
            fog = H("#0f5f78"), fogDensity = 0.028f, caustic = 0.85f, causticColor = H("#fff6d8"), rays = 1.0f, rayColor = H("#c8f7ff"),
            led = H("#ffffff"), ledI = 3.2f, moon = 0, bgTop = H("#22b2c6"), bgDeep = H("#06415a"),
            roomTop = H("#1e2a3e"), roomBottom = H("#06080d"), roomGlow = H("#34507a"), exposure = 0.82f, dust = 1.0f, night = 0, fill = 0.35f,
        };

        public static DayPreset Evening() => new DayPreset
        {
            sun = H("#ffb27a"), sunI = 1.6f, sky = H("#e0a888"), ground = H("#3a2030"), hemiI = 0.35f,
            fog = H("#3b5a78"), fogDensity = 0.034f, caustic = 0.65f, causticColor = H("#ffcf9a"), rays = 0.8f, rayColor = H("#ffd7a8"),
            led = H("#ffc08a"), ledI = 2.0f, moon = 0.2f, bgTop = H("#d07a5a"), bgDeep = H("#2a2348"),
            roomTop = H("#3a2a36"), roomBottom = H("#0a070b"), roomGlow = H("#a0584a"), exposure = 0.8f, dust = 0.85f, night = 0.3f, fill = 0.25f,
        };

        public static DayPreset Night() => new DayPreset
        {
            sun = H("#7b95ff"), sunI = 0.35f, sky = H("#2c4c9a"), ground = H("#05070f"), hemiI = 0.28f,
            fog = H("#071d3a"), fogDensity = 0.042f, caustic = 0.18f, causticColor = H("#9db8ff"), rays = 0.22f, rayColor = H("#9fb6ff"),
            led = H("#2a46ff"), ledI = 0.6f, moon = 2.4f, bgTop = H("#0e2d5c"), bgDeep = H("#020713"),
            roomTop = H("#090d1a"), roomBottom = H("#020306"), roomGlow = H("#1a2a55"), exposure = 0.85f, dust = 0.5f, night = 1, fill = 0.08f,
        };

        public static DayPreset Morning() => new DayPreset
        {
            sun = H("#ffd9c0"), sunI = 1.9f, sky = H("#e8c8dc"), ground = H("#3a3040"), hemiI = 0.4f,
            fog = H("#3a7a96"), fogDensity = 0.032f, caustic = 0.8f, causticColor = H("#fff0e0"), rays = 0.9f, rayColor = H("#ffe4f0"),
            led = H("#ffe2d0"), ledI = 2.6f, moon = 0.1f, bgTop = H("#7fb8d0"), bgDeep = H("#1c3550"),
            roomTop = H("#2a2a3e"), roomBottom = H("#07070c"), roomGlow = H("#7a6a8a"), exposure = 0.82f, dust = 0.9f, night = 0.15f, fill = 0.3f,
        };

        // auto cycle: phase -> preset
        static readonly (float phase, Func<DayPreset> preset)[] Keys =
        {
            (0.0f, Morning), (0.08f, Day), (0.45f, Day), (0.55f, Evening), (0.63f, Night), (0.92f, Night), (1.0f, Morning),
        };

        public Mode mode = Mode.Auto;
        [Range(0, 1)] public float phase = 0.1f;
        [Tooltip("Length of a whole day in the auto cycle, minutes")] public float cycleMinutes = 8;
        [Tooltip("Brightness set by the user, multiplies the exposure")] public float exposureMul = 1;

        [Header("Scene")]
        public Light sun;
        public Light fill;
        public Light moon;
        public Light lantern;
        public Light crystal;
        public Volume volume;
        public Material led, moonLeds, backdrop, room, rays, dust;
        public List<GlowMaterial> glow = new List<GlowMaterial>();

        public DayPreset Current { get; private set; }
        public float NightAmount => Current?.night ?? 0;

        DayPreset target = new DayPreset();
        readonly DayPreset[] cache = new DayPreset[4];

        static readonly int CausticId = Shader.PropertyToID("_AqCaustic");
        static readonly int CausticColorId = Shader.PropertyToID("_AqCausticColor");
        static readonly int NightId = Shader.PropertyToID("_AqNight");

        public string Label => mode switch
        {
            Mode.Day => "День",
            Mode.Evening => "Вечер",
            Mode.Night => "Ночь",
            _ => phase < 0.08f ? "Утро" : phase < 0.5f ? "День" : phase < 0.6f ? "Вечер" : phase < 0.95f ? "Ночь" : "Утро",
        };

        DayPreset Goal()
        {
            if (mode == Mode.Auto)
            {
                int i = 0;
                while (i < Keys.Length - 2 && phase > Keys[i + 1].phase) i++;
                var (p0, a) = Keys[i];
                var (p1, b) = Keys[i + 1];
                float t = Mathf.Clamp01((phase - p0) / Mathf.Max(1e-5f, p1 - p0));
                return DayPreset.Lerp(a(), b(), t * t * (3 - 2 * t), target);
            }
            return mode == Mode.Day ? Day() : mode == Mode.Evening ? Evening() : Night();
        }

        /// <summary>Jump straight to the current goal (no blending), e.g. after changing the mode in the editor.</summary>
        public void Snap()
        {
            Current = Goal().Clone();
            Apply(Current, 0);
        }

        void OnEnable() => Snap();
        void OnValidate() { if (isActiveAndEnabled) Snap(); }

        void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0;
            if (mode == Mode.Auto && Application.isPlaying) phase = (phase + dt / (cycleMinutes * 60)) % 1;
            else if (mode != Mode.Auto) phase = mode == Mode.Day ? 0.25f : mode == Mode.Evening ? 0.55f : 0.75f;
            var goal = Goal();
            Current = Current == null || !Application.isPlaying ? goal.Clone() : DayPreset.Lerp(Current, goal, 1 - Mathf.Exp(-dt * 0.8f), Current);
            Apply(Current, Application.isPlaying ? Time.time : 0);
        }

        void Apply(DayPreset c, float t)
        {
            const float invPi = 1 / Mathf.PI;
            if (sun) { sun.color = c.sun; sun.intensity = c.sunI * invPi * SunScale; }
            if (fill) { fill.color = H("#9fdfff"); fill.intensity = c.fill * invPi; }
            // three.js lets the moon spot fade with distance^1.2, URP with distance^2: match at the sand (~10.5 m away)
            if (moon) moon.intensity = c.moon * 40 * invPi * MoonScale;

            // hemisphere light plus the diffuse part of the studio environment (constant all day, as
            // in the web version) -> trilight ambient: sky above, ground below, their mix around
            float hk = c.hemiI * invPi * AmbientScale;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = (c.sky.linear * hk + EnvUp * EnvIntensity).gamma;
            RenderSettings.ambientGroundColor = (c.ground.linear * hk + EnvDown * EnvIntensity).gamma;
            RenderSettings.ambientEquatorColor = (Color.Lerp(c.sky, c.ground, 0.5f).linear * hk + EnvSide * EnvIntensity).gamma;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            // the web version mixes fog in after tone mapping; here it goes through the exposure, so it is dimmed to match
            RenderSettings.fogColor = (c.fog.linear * FogScale).gamma;
            RenderSettings.fogDensity = c.fogDensity;

            Shader.SetGlobalFloat(CausticId, c.caustic);
            Shader.SetGlobalColor(CausticColorId, c.causticColor.linear);
            Shader.SetGlobalFloat(NightId, c.night);

            if (led) led.SetColor("_EmissionColor", c.led.linear * c.ledI);
            if (moonLeds) moonLeds.SetColor("_EmissionColor", new Color(0.2f + c.night * 0.3f, 0.45f + c.night * 0.4f, 1) * (0.3f + c.night * 2.5f));
            if (backdrop) { backdrop.SetColor("_Top", c.bgTop); backdrop.SetColor("_Deep", c.bgDeep); }
            if (room) { room.SetColor("_Top", c.roomTop); room.SetColor("_Bottom", c.roomBottom); room.SetColor("_Glow", c.roomGlow); }
            if (rays) { rays.SetFloat("_Intensity", c.rays); rays.SetColor("_Color", c.rayColor); }
            if (dust) dust.SetFloat("_Ray", c.dust);

            foreach (var g in glow)
            {
                if (!g.material) continue;
                float k = Mathf.Lerp(g.day, g.night, c.night) * (g.flicker ? 0.92f + 0.08f * Mathf.Sin(t * 3 + g.day * 10) : 1);
                g.material.SetColor("_EmissionColor", g.baseEmission * k);
            }
            if (lantern) lantern.intensity = c.night * 2.2f * (0.85f + 0.15f * Mathf.Sin(t * 7.3f) * Mathf.Sin(t * 3.1f)) * invPi;
            if (crystal) crystal.intensity = (0.3f + c.night * 1.6f) * invPi;

            if (volume && volume.sharedProfile && volume.sharedProfile.TryGet(out ColorAdjustments ca))
            {
                // three.js's ACES fit scales by exposure / 0.6 before the curve
                ca.postExposure.overrideState = true;
                ca.postExposure.value = Mathf.Log(c.exposure * exposureMul / 0.6f, 2) + ExposureBias;
            }
        }

        // the environment dome (Aquarium/EnvSky) convolved for diffuse light, facing up / sideways / down
        static readonly Color EnvUp = new Color(0.6938f, 0.822f, 0.8936f), EnvSide = new Color(0.2203f, 0.2816f, 0.3272f), EnvDown = new Color(0.0338f, 0.0426f, 0.0598f);
        /// <summary>The web version's envMapIntensity.</summary>
        public static float EnvIntensity = 0.5f;

        /// <summary>Calibration against the web version's look (tuned by comparing screenshots).</summary>
        public static float SunScale = 1, AmbientScale = 1, ExposureBias = 0.8f, MoonScale = 6.5f, FogScale = 0.55f;

        static Color Scaled(Color srgb, float k)
        {
            var lin = srgb.linear * k;
            return lin.gamma;
        }
    }
}
