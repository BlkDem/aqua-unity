// A fully synthesized, relaxing soundscape, no audio files (the web version's audio.js):
// water murmur and the filter's trickle and hum, slow pad chords in a major pentatonic,
// soft bells, bubbles; a knock on the glass, sprinkled food and the chime of rare events.
// Everything is generated on the audio thread (OnAudioFilterRead); the main thread only
// sends requests (a knock, a pop) and settings (volumes, night).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.App
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class AquaAudio : MonoBehaviour
    {
        static readonly int[] Scale = { 0, 2, 4, 7, 9, 12, 14, 16, 19, 21 };   // major pentatonic
        static readonly int[][] Chords = { new[] { 0, 4, 7, 11 }, new[] { -3, 0, 4, 7 }, new[] { 5, 9, 12, 16 }, new[] { 2, 5, 9, 12 }, new[] { -5, -1, 2, 7 } };
        static double Mtof(double m) => 440 * Math.Pow(2, (m - 69) / 12);

        [Range(0, 1)] public float master = 0.7f, music = 0.55f, fx = 0.8f;
        /// <summary>0..1 from the day cycle: lower pads, sparser bells at night.</summary>
        [NonSerialized] public float night;

        // ---------------------------------------------------------------- main thread -> audio thread
        readonly object gate = new object();
        readonly List<Action> requests = new List<Action>();
        float lastPop;

        void Send(Action a) { lock (gate) requests.Add(a); }

        /// <summary>A bubble bursting at the surface at x (web coordinates), now and then.</summary>
        public void Pop(float x)
        {
            if (Time.unscaledTime - lastPop < 0.09f) return;
            lastPop = Time.unscaledTime;
            float pan = Mathf.Clamp(x / 6, -1, 1);
            Send(() => Bubble(0.02f + Rand() * 0.02f, pan, Rand() * 0.5f, 0));
        }

        public void Tap() => Send(() =>
        {
            AddNoise(0.35f, 0.08f, 1800, 3);
            Add(new Voice { wave = Wave.Sine, f0 = 220, f1 = 120, glide = 0.12f, peak = 0.25f, attack = 0.0001f, end = 0.15f, fx = true, panL = 0.707f, panR = 0.707f });
        });

        public void Sprinkle() => Send(() =>
        {
            for (int i = 0; i < 10; i++) Bubble(0.03f, Rand() * 2 - 1, 0.1f + Rand() * 0.3f, i * 0.07f + Rand() * 0.06f);
        });

        public void Chime() => Send(() => { Bell(0); Bell(0.15); Bell(0.3); });

        // ---------------------------------------------------------------- the synth (audio thread)
        enum Wave { Sine, Triangle }

        sealed class Voice
        {
            public Wave wave;
            public double phase;
            public float f0, f1, glide, detune;
            /// <summary>Exponential envelope: 0.0001 -> peak over attack, then -> 0.0001 at end (seconds).</summary>
            public float peak, attack, end;
            public float delay;
            public float panL = 0.707f, panR = 0.707f;
            public bool fx;
            /// <summary>Low-pass cutoff (Hz) for pads, 0 = none.</summary>
            public float lowpass;
            public float lp1, lp2;
            // running state
            public double t, gain, gainK, freq, freqK;
            public int seg = -1;
        }

        sealed class NoiseBurst
        {
            public Biquad bp;
            public float gain, k, end, t, delay;
        }

        /// <summary>RBJ biquad (low-pass / band-pass), direct form 1.</summary>
        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;

            public void LowPass(double f, double q, double sr) => Set(f, q, sr, false);
            public void BandPass(double f, double q, double sr) => Set(f, q, sr, true);

            void Set(double f, double q, double sr, bool band)
            {
                double w = 2 * Math.PI * Math.Min(f, sr * 0.45) / sr, cw = Math.Cos(w), alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
                if (band) { b0 = alpha / a0; b1 = 0; b2 = -alpha / a0; }
                else { b0 = (1 - cw) / 2 / a0; b1 = (1 - cw) / a0; b2 = b0; }
                a1 = -2 * cw / a0; a2 = (1 - alpha) / a0;
            }

            public float Run(float x)
            {
                double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return (float)y;
            }
        }

        /// <summary>A small Freeverb: a long, soft room for the music and a little of the water.</summary>
        sealed class Reverb
        {
            static readonly int[] CombSizes = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
            static readonly int[] AllpassSizes = { 556, 441, 341, 225 };
            readonly float[][] combL, combR, apL, apR;
            readonly int[] ciL, ciR, aiL, aiR;
            readonly float[] fsL, fsR;
            const float Feedback = 0.86f, Damp = 0.25f;

            public Reverb(int sr)
            {
                float k = sr / 44100f;
                int n = CombSizes.Length, m = AllpassSizes.Length;
                combL = new float[n][]; combR = new float[n][]; ciL = new int[n]; ciR = new int[n]; fsL = new float[n]; fsR = new float[n];
                for (int i = 0; i < n; i++) { combL[i] = new float[(int)(CombSizes[i] * k)]; combR[i] = new float[(int)((CombSizes[i] + 23) * k)]; }
                apL = new float[m][]; apR = new float[m][]; aiL = new int[m]; aiR = new int[m];
                for (int i = 0; i < m; i++) { apL[i] = new float[(int)(AllpassSizes[i] * k)]; apR[i] = new float[(int)((AllpassSizes[i] + 23) * k)]; }
            }

            static float Comb(float x, float[] buf, ref int i, ref float store)
            {
                float y = buf[i];
                store = y * (1 - Damp) + store * Damp;
                buf[i] = x + store * Feedback;
                if (++i >= buf.Length) i = 0;
                return y;
            }

            static float Allpass(float x, float[] buf, ref int i)
            {
                float b = buf[i];
                buf[i] = x + b * 0.5f;
                if (++i >= buf.Length) i = 0;
                return b - x;
            }

            public void Run(float inL, float inR, out float outL, out float outR)
            {
                float x = (inL + inR) * 0.015f, l = 0, r = 0;
                for (int i = 0; i < combL.Length; i++)
                {
                    l += Comb(x, combL[i], ref ciL[i], ref fsL[i]);
                    r += Comb(x, combR[i], ref ciR[i], ref fsR[i]);
                }
                for (int i = 0; i < apL.Length; i++) { l = Allpass(l, apL[i], ref aiL[i]); r = Allpass(r, apR[i], ref aiR[i]); }
                outL = l; outR = r;
            }
        }

        int sr;
        bool ready;
        readonly System.Random rng = new System.Random();
        readonly List<Voice> voices = new List<Voice>();
        readonly List<NoiseBurst> bursts = new List<NoiseBurst>();
        readonly List<Action> pending = new List<Action>();
        Reverb reverb;
        readonly Biquad waterLp = new Biquad(), trickleBp = new Biquad();
        double clock, nextChord = 0.5, nextBell = 3, nextBubbles;
        int chordIdx;
        float brown, masterGain, nightA, musicA, fxA, masterA;
        double lfoWater, lfoTrickle, hum1, hum2;
        int waterCoefCount;

        float Rand() { lock (rng) return (float)rng.NextDouble(); }

        void Start()
        {
            sr = AudioSettings.outputSampleRate;
            reverb = new Reverb(sr);
            trickleBp.BandPass(2600, 0.8, sr);
            var src = GetComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0;
            src.Play();
            ready = true;
        }

        void Update()
        {
            nightA = night;
            musicA = music;
            fxA = fx;
            masterA = master;
        }

        void Add(Voice v) => voices.Add(v);

        void AddNoise(float gain, float end, double f, double q, float delay = 0)
        {
            var b = new NoiseBurst { bp = new Biquad(), gain = gain, end = end, delay = delay, k = (float)Math.Pow(0.001 / gain, 1.0 / (end * sr)) };
            b.bp.BandPass(f, q, sr);
            bursts.Add(b);
        }

        static (float l, float r) Pan(float p)
        {
            float a = (Mathf.Clamp(p, -1, 1) + 1) * Mathf.PI / 4;
            return (Mathf.Cos(a), Mathf.Sin(a));
        }

        void Bubble(float vol, float pan, float size, float delay)
        {
            float f0 = 380 + (1 - size) * 1400;
            var (l, r) = Pan(pan);
            Add(new Voice
            {
                wave = Wave.Sine, f0 = f0, f1 = f0 * (1.8f + Rand()), glide = 0.06f + size * 0.05f,
                peak = vol, attack = 0.005f, end = 0.09f + size * 0.06f, delay = delay, fx = true, panL = l, panR = r,
            });
        }

        void Bell(double delay)
        {
            int note = 69 + Scale[(int)(Rand() * Scale.Length) % Scale.Length] - (nightA > 0.5f ? 12 : 0);
            foreach (var (mul, vol) in new[] { (1f, 0.03f), (2.01f, 0.008f), (3.0f, 0.004f) })
            {
                var (l, r) = Pan(Rand() * 1.4f - 0.7f);
                float f = (float)Mtof(note) * mul;
                Add(new Voice { wave = Wave.Sine, f0 = f, f1 = f, peak = vol, attack = 0.01f, end = 3.5f / mul, delay = (float)delay, panL = l, panR = r });
            }
        }

        void Pad(int[] chord, double delay)
        {
            int root = 57 - (nightA > 0.5f ? 5 : 0);
            const float dur = 9;
            for (int i = 0; i < chord.Length; i++)
                foreach (int det in new[] { -6, 5 })
                {
                    float f = (float)(Mtof(root + chord[i] + (i == 0 ? -12 : 0)) * Math.Pow(2, det / 1200.0));
                    Add(new Voice
                    {
                        wave = i == 0 ? Wave.Sine : Wave.Triangle, f0 = f, f1 = f, peak = 0.022f, attack = 3, end = dur + 2, delay = (float)delay,
                        lowpass = 700 + (1 - nightA) * 600,
                    });
                }
        }

        /// <summary>The slow parts of the music and the background bubbles, scheduled ahead in the audio clock.</summary>
        void Tick()
        {
            if (clock > nextChord - 0.2)
            {
                chordIdx = (chordIdx + (Rand() < 0.7f ? 1 : 2)) % Chords.Length;
                Pad(Chords[chordIdx], nextChord - clock);
                nextChord += 8 + (nightA > 0.5f ? 2 : 0);
            }
            if (clock > nextBell)
            {
                Bell(0.05);
                if (Rand() < 0.35f) Bell(0.35 + Rand() * 0.3);
                nextBell = clock + 2 + Rand() * (nightA > 0.5f ? 7 : 5);
            }
            if (clock > nextBubbles)
            {
                int n = Rand() < 0.15f ? 3 + (int)(Rand() * 4) : 1;
                float at = 0;
                for (int i = 0; i < n; i++)
                {
                    Bubble(0.012f + Rand() * 0.02f, -0.6f + Rand() * 0.4f, Rand(), at);
                    at += 0.04f + Rand() * 0.06f;
                }
                nextBubbles = clock + 0.12 + Rand() * 0.6;
            }
        }

        void OnAudioFilterRead(float[] data, int channels) => Render(data, channels);

        /// <summary>Fill an interleaved buffer with the next stretch of sound (the audio thread does this; tests too, with the component off).</summary>
        public void Render(float[] data, int channels)
        {
            lock (renderGate) RenderLocked(data, channels);
        }

        readonly object renderGate = new object();

        void RenderLocked(float[] data, int channels)
        {
            if (!ready) return;
            lock (gate) { pending.AddRange(requests); requests.Clear(); }
            foreach (var a in pending) a();
            pending.Clear();
            Tick();

            double dt = 1.0 / sr;
            int frames = data.Length / channels;
            float musicG = musicA, fxG = fxA, masterTarget = masterA;
            // the water's low-pass wanders slowly (an LFO on the cutoff)
            lfoWater += frames * dt * 0.07 * 2 * Math.PI;
            if (waterCoefCount++ % 2 == 0) waterLp.LowPass(420 + Math.Sin(lfoWater) * 160, 0.4, sr);

            for (int n = 0; n < frames; n++)
            {
                float mL = 0, mR = 0, fL = 0, fR = 0;
                // water murmur: brown noise through the wandering low-pass
                float w = Rand2() * 2 - 1;
                brown = (brown + 0.02f * w) / 1.02f;
                float water = waterLp.Run(brown * 3.5f) * 0.32f;
                fL += water; fR += water;
                // the filter: a trickle and a soft motor hum, a little to the right
                lfoTrickle += dt * 0.31 * 2 * Math.PI;
                float trickle = trickleBp.Run(Rand2() * 2 - 1) * (0.018f + 0.008f * (float)Math.Sin(lfoTrickle));
                hum1 += dt * 55 * 2 * Math.PI; hum2 += dt * 110 * 2 * Math.PI;
                float filt = trickle + (float)Math.Sin(hum1) * 0.012f + (float)Math.Sin(hum2) * 0.004f;
                fL += filt * 0.45f; fR += filt * 0.89f;

                for (int i = voices.Count - 1; i >= 0; i--)
                {
                    var v = voices[i];
                    if (v.delay > 0) { v.delay -= (float)dt; continue; }
                    if (v.seg < 0) Begin(v, 0);
                    if (v.seg == 0 && v.t >= v.attack) Begin(v, 1);
                    if (v.t >= v.end) { voices.RemoveAt(i); continue; }
                    float s = v.wave == Wave.Sine ? (float)Math.Sin(v.phase * 2 * Math.PI) : 4 * Mathf.Abs((float)(v.phase - Math.Floor(v.phase)) - 0.5f) - 1;
                    v.phase += v.freq * dt;
                    if (v.phase > 1) v.phase -= Math.Floor(v.phase);
                    if (v.glide > 0 && v.t < v.glide) v.freq *= v.freqK;
                    s *= (float)v.gain;
                    v.gain *= v.gainK;
                    if (v.lowpass > 0)
                    {
                        float a = 1 - Mathf.Exp(-2 * Mathf.PI * v.lowpass / sr);
                        v.lp1 += (s - v.lp1) * a; v.lp2 += (v.lp1 - v.lp2) * a;
                        s = v.lp2;
                    }
                    v.t += dt;
                    if (v.fx) { fL += s * v.panL; fR += s * v.panR; }
                    else { mL += s * v.panL; mR += s * v.panR; }
                }
                for (int i = bursts.Count - 1; i >= 0; i--)
                {
                    var b = bursts[i];
                    if (b.delay > 0) { b.delay -= (float)dt; continue; }
                    float s = b.bp.Run(Rand2() * 2 - 1) * b.gain;
                    b.gain *= b.k;
                    b.t += (float)dt;
                    fL += s; fR += s;
                    if (b.t > b.end) bursts.RemoveAt(i);
                }

                mL *= musicG; mR *= musicG; fL *= fxG; fR *= fxG;
                reverb.Run(mL * 0.55f + fL * 0.25f, mR * 0.55f + fR * 0.25f, out float rL, out float rR);
                masterGain += (masterTarget - masterGain) * (float)(dt / 4);   // fades in over a few seconds at start
                float L = (mL + fL + rL) * masterGain, R = (mR + fR + rR) * masterGain;
                // a gentle limiter in place of the web version's compressor
                L = (float)Math.Tanh(L * 1.2f); R = (float)Math.Tanh(R * 1.2f);
                int o = n * channels;
                data[o] = L;
                if (channels > 1) data[o + 1] = R;
                for (int c = 2; c < channels; c++) data[o + c] = 0;
                clock += dt;
            }
        }

        // a fast white-noise source for the audio thread
        uint noiseState = 22222;
        float Rand2()
        {
            noiseState ^= noiseState << 13; noiseState ^= noiseState >> 17; noiseState ^= noiseState << 5;
            return (noiseState & 0xFFFFFF) / (float)0x1000000;
        }

        void Begin(Voice v, int seg)
        {
            v.seg = seg;
            if (seg == 0)
            {
                v.freq = v.f0;
                v.freqK = v.glide > 0 ? Math.Pow(v.f1 / v.f0, 1.0 / (v.glide * sr)) : 1;
                v.gain = 0.0001;
                v.gainK = Math.Pow(v.peak / 0.0001, 1.0 / Math.Max(1, v.attack * sr));
            }
            else
            {
                v.gain = v.peak;
                v.gainK = Math.Pow(0.0001 / v.peak, 1.0 / Math.Max(1, (v.end - v.attack) * sr));
            }
        }
    }
}
