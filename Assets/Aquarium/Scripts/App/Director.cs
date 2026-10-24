// The camera director (the web version's director.js). Free: the orbit camera is the user's,
// flights take it to a preset view. Follow: it keeps a selected creature in view. Observe and
// cinematic: it stages shots by itself — wide views, dollies, close-ups of a fish, the school,
// the sand, the surface — and turns to whoever a story is about; cinematic cuts harder, adds
// letterbox bars and fades. Shots are computed in the web version's coordinates.
using System;
using System.Linq;
using Aquarium.Fish;
using Aquarium.World;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using FishT = Aquarium.Fish.Fish;

namespace Aquarium.App
{
    public enum CameraMode { Free, Follow, Observe, Cinematic }

    public sealed class Director
    {
        static float Ease(float t) => t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
        static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
        /// <summary>Web coordinates -> Unity.</summary>
        static Vector3 U(Vector3 w) => new Vector3(-w.x, w.y, w.z);

        public static readonly (string name, Vector3 pos, Vector3 look)[] Views =
        {
            ("Фронт", V(0, 3.3f, 14.5f), V(0, 2.6f, 0)),
            ("Сбоку", V(11.5f, 3.6f, 6.5f), V(0, 2.4f, 0)),
            ("Сверху", V(0.5f, 13, 7), V(0, 1.5f, 0)),
            ("У дна", V(-3.5f, 1.0f, 2.0f), V(1.5f, 1.5f, -1.5f)),
            ("Крупно", V(-1.5f, 2.2f, 6.0f), V(-1.5f, 1.6f, 0)),
        };

        sealed class Shot
        {
            public string kind;
            public float dur, t;
            public Func<float, Vector3> pos, look;
            public object subject;
            public bool track;
        }

        readonly Camera cam;
        readonly OrbitCamera orbit;
        readonly Ecosystem eco;
        readonly Volume volume;
        public CameraMode mode = CameraMode.Free;
        public object subject;
        public bool dofOn = true;
        /// <summary>0..1, for the interface: black bars of the cinematic mode, and the fade at a cut.</summary>
        public float letterbox, fade;
        /// <summary>0..1: the camera is under water (inside the tank).</summary>
        public float under;

        int view;
        Shot shot;
        bool snap;
        int fadeDir;
        object pendingFocus;
        Vector3 look;   // web space
        (Vector3 from, Vector3 to, Vector3 lf, Vector3 lt, float t, float dur)? fly;

        public Director(Camera cam, OrbitCamera orbit, Ecosystem eco, Volume volume)
        {
            this.cam = cam;
            this.orbit = orbit;
            this.eco = eco;
            this.volume = volume;
        }

        static Vector3 PosOf(object o) => o switch { FishT f => f.pos, Critter c => c.pos, _ => Vector3.zero };
        static float SizeOf(object o) => o switch { FishT f => f.size, Critter c => c.size, _ => 0.2f };
        static bool Gone(object o) => o is FishT f && (f.hidden || !f.eco.fish.Contains(f));

        Vector3 CamWeb => U(cam.transform.position);
        Vector3 OrbitTargetWeb => U(orbit.target);

        public void SetMode(CameraMode m)
        {
            mode = m;
            orbit.enabled = m == CameraMode.Free || m == CameraMode.Follow;
            if (m == CameraMode.Cinematic || m == CameraMode.Observe) NextShot(true);
            if (m == CameraMode.Free)
            {
                subject = null;
                shot = null;
            }
        }

        public void Follow(object s)
        {
            subject = s;
            mode = CameraMode.Follow;
            orbit.enabled = true;
            var p = PosOf(s);
            var dir = (CamWeb - p).normalized;
            float dist = 1.6f + SizeOf(s) * 3;
            FlyTo(p + dir * dist + V(0, 0.2f, 0), p, 1.6f);
        }

        public void Unfollow()
        {
            if (mode != CameraMode.Follow) return;
            mode = CameraMode.Free;
            subject = null;
        }

        /// <summary>Fly to a view (web coordinates).</summary>
        public void FlyTo(Vector3 pos, Vector3 lookAt, float dur = 2)
        {
            fly = (CamWeb, pos, OrbitTargetWeb, lookAt, 0, dur);
        }

        public string CycleView()
        {
            view = (view + 1) % Views.Length;
            var v = Views[view];
            mode = CameraMode.Free;
            subject = null;
            orbit.enabled = true;
            FlyTo(v.pos, v.look, 2.2f);
            return v.name;
        }

        public void OnStory(Story s)
        {
            if ((mode == CameraMode.Observe || mode == CameraMode.Cinematic) && s.focus != null && Rnd.Chance(s.rare ? 0.95f : 0.45f))
                pendingFocus = s.focus;
        }

        // --------------------------------------------------------------- shot library
        Shot MakeShot(string kind)
        {
            bool cin = mode == CameraMode.Cinematic;
            float dur = cin ? Rnd.R(6, 11) : Rnd.R(11, 20);
            var live = eco.fish.Where(f => !f.hidden).ToList();
            switch (kind)
            {
                case "dolly":
                {
                    float x0 = Rnd.R(-5, 0), x1 = x0 + Rnd.R(3, 6) * (Rnd.Chance(0.5f) ? 1 : -1), y = Rnd.R(1.4f, 3.5f), z = Rnd.R(4.5f, 7.5f);
                    return new Shot { kind = kind, dur = dur, pos = t => V(x0 + (x1 - x0) * t, y, z), look = t => V(x0 + (x1 - x0) * t * 1.1f, y - 0.4f, -0.5f) };
                }
                case "fish":
                {
                    var f = pendingFocus is FishT pf && !pf.hidden ? pf : Rnd.Pick(live.Where(q => q.size > 0.3f).Concat(live).ToList());
                    pendingFocus = null;
                    if (f == null) return MakeShot("wide");
                    float side = Rnd.Chance(0.5f) ? -1 : 1, d = f.size * 3.5f + Rnd.R(0.8f, 1.6f), up = Rnd.R(-0.1f, 0.5f);
                    return new Shot
                    {
                        kind = kind, dur = dur, subject = f, track = true,
                        pos = _ =>
                        {
                            var h = f.heading.sqrMagnitude > 0 ? f.heading : Vector3.right;
                            var s = Vector3.Cross(h, Vector3.up).normalized * side * d;
                            var p = f.pos + s + h * d * 0.3f + V(0, up, 0);
                            if (p.z < 2.3f) { p.x = Mathf.Clamp(p.x, -5.6f, 5.6f); p.y = Mathf.Clamp(p.y, 0.7f, 5.3f); p.z = Mathf.Max(p.z, -2.1f); }
                            return p;
                        },
                        look = _ => f.pos,
                    };
                }
                case "critter":
                {
                    var c = pendingFocus as Critter ?? Rnd.Pick(eco.critters);
                    pendingFocus = null;
                    if (c == null) return MakeShot("wide");
                    float a = Rnd.R(0, Mathf.PI * 2);
                    return new Shot
                    {
                        kind = kind, dur = dur, subject = c, track = true,
                        pos = t =>
                        {
                            var p = c.pos + V(Mathf.Sin(a + t * 0.4f) * 0.8f, 0.35f, Mathf.Cos(a + t * 0.4f) * 0.8f + 0.4f);
                            p.x = Mathf.Clamp(p.x, -5.6f, 5.6f); p.z = Mathf.Clamp(p.z, -2.0f, 4); p.y = Mathf.Max(p.y, c.pos.y + 0.15f);
                            return p;
                        },
                        look = _ => c.pos + V(0, 0.05f, 0),
                    };
                }
                case "school":
                {
                    if (eco.schools.Count == 0) return MakeShot("wide");
                    var s = Rnd.Pick(eco.schools.Values.ToList());
                    var off = V(Rnd.R(-2, 2), Rnd.R(0, 1), Rnd.R(3, 5));
                    return new Shot { kind = kind, dur = dur, track = true, pos = _ => s.target + off, look = _ => s.target };
                }
                case "lowSand":
                {
                    float x0 = Rnd.R(-5, 3), dx = Rnd.R(1.5f, 2.5f);
                    return new Shot { kind = kind, dur = dur, pos = t => V(x0 + dx * t, 0.95f, 1.85f), look = t => V(x0 + dx * t + 1.5f, 1.2f, -1.5f) };
                }
                case "surface":
                {
                    float x = Rnd.R(-4, 4);
                    return new Shot { kind = kind, dur = dur, pos = t => V(x + t * 0.6f, 1.2f + t * 0.3f, 1.9f), look = t => V(x + 1.2f + t, 5.6f, -0.8f) };
                }
                case "decor":
                {
                    var s = Rnd.Pick(eco.spots.Where(q => q.kind != "open" && q.kind != "plants" || Rnd.Chance(0.3f)).ToList());
                    float a = Rnd.R(-0.5f, 0.5f);
                    return new Shot { kind = kind, dur = dur, track = true, pos = t => s.pos + V(Mathf.Sin(a) * (3.6f - t * 1.4f), 0.4f + (1 - t) * 0.5f, Mathf.Cos(a) * (3.6f - t * 1.4f)), look = _ => s.pos };
                }
                case "orbitFish":
                {
                    var f = Rnd.Pick(live.Where(q => q.size > 0.35f).ToList());
                    if (f == null) return MakeShot("wide");
                    float a0 = Rnd.R(0, 6.28f), r = f.size * 3 + 0.9f;
                    return new Shot
                    {
                        kind = kind, dur = dur, subject = f, track = true,
                        pos = t =>
                        {
                            var p = f.pos + V(Mathf.Sin(a0 + t * 1.2f) * r, 0.25f, Mathf.Cos(a0 + t * 1.2f) * r);
                            return V(Mathf.Clamp(p.x, -5.6f, 5.6f), Mathf.Clamp(p.y, 0.8f, 5.3f), Mathf.Clamp(p.z, -2.1f, 6));
                        },
                        look = _ => f.pos,
                    };
                }
                default:   // wide
                {
                    float a0 = Rnd.R(-0.7f, 0.7f), da = Rnd.R(-0.25f, 0.25f), r = Rnd.R(11, 15), y = Rnd.R(2.5f, 5.5f);
                    return new Shot { kind = "wide", dur = dur, pos = t => V(Mathf.Sin(a0 + da * t) * r, y + t * 0.3f, Mathf.Cos(a0 + da * t) * r * 0.9f + 1), look = _ => V(0, 2.6f, 0) };
                }
            }
        }

        static readonly (string k, float cin, float obs)[] ShotWeights =
        {
            ("wide", 1, 2.2f), ("dolly", 1.2f, 1.5f), ("fish", 2.2f, 1.2f), ("critter", 0.7f, 0.4f), ("school", 1.2f, 1),
            ("lowSand", 0.9f, 0.5f), ("surface", 0.6f, 0.4f), ("decor", 1, 0.8f), ("orbitFish", 1.2f, 0.5f),
        };

        void NextShot(bool first = false)
        {
            bool cin = mode == CameraMode.Cinematic;
            string kind = "wide";
            if (pendingFocus != null) kind = pendingFocus is FishT ? "fish" : "critter";
            else if (!first)
            {
                float r = Rnd.R() * ShotWeights.Sum(w => cin ? w.cin : w.obs);
                foreach (var w in ShotWeights)
                {
                    r -= cin ? w.cin : w.obs;
                    if (r <= 0) { kind = w.k; break; }
                }
            }
            shot = MakeShot(kind);
            shot.t = 0;
            bool cut = first || (cin ? Rnd.Chance(0.65f) : Rnd.Chance(0.25f));
            if (cut) { fade = 1; fadeDir = -1; snap = true; }
            else snap = false;
        }

        public void Update(float dt)
        {
            if (subject != null && Gone(subject)) { subject = null; if (mode == CameraMode.Follow) mode = CameraMode.Free; }
            float focusDist = Vector3.Distance(cam.transform.position, orbit.target);
            if (mode == CameraMode.Cinematic || mode == CameraMode.Observe)
            {
                var s = shot;
                if (s != null)
                {
                    s.t += dt;
                    float u = Mathf.Min(1, s.t / s.dur);
                    var pos = s.pos(Ease(u) * 0.6f + u * 0.4f);
                    var lk = s.look(u);
                    var camW = CamWeb;
                    if (snap) { camW = pos; look = lk; snap = false; }
                    else
                    {
                        camW = Vector3.Lerp(camW, pos, 1 - Mathf.Exp(-dt * (s.track ? 2.2f : 1.2f)));
                        look = Vector3.Lerp(look, lk, 1 - Mathf.Exp(-dt * 3));
                    }
                    cam.transform.position = U(camW);
                    cam.transform.LookAt(U(look));
                    focusDist = Vector3.Distance(camW, look);
                    if (s.t > s.dur - 0.35f && fadeDir == 0 && mode == CameraMode.Cinematic) fadeDir = 1;
                    if (s.t > s.dur || (pendingFocus != null && s.t > 3 && s.subject == null)) NextShot();
                }
                letterbox += ((mode == CameraMode.Cinematic ? 1 : 0) - letterbox) * Mathf.Min(1, dt * 2);
            }
            else
            {
                letterbox += (0 - letterbox) * Mathf.Min(1, dt * 3);
                if (fly is var (from, to, lf, lt, t, dur))
                {
                    t += dt / dur;
                    float k = Ease(Mathf.Min(1, t));
                    if (mode == CameraMode.Follow && subject != null) lt = PosOf(subject);
                    orbit.Place(U(Vector3.Lerp(from, to, k)), U(Vector3.Lerp(lf, lt, k)));
                    fly = t >= 1 ? null : (from, to, lf, lt, t, dur);
                }
                else if (mode == CameraMode.Follow && subject != null) orbit.Follow(U(PosOf(subject)));
                look = OrbitTargetWeb;
            }
            if (fadeDir != 0)
            {
                fade = Mathf.Clamp01(fade + fadeDir * dt * 3);
                if (fade <= 0 || fade >= 1) fadeDir = fade >= 1 ? -1 : 0;
            }
            var c = CamWeb;
            bool inside = Mathf.Abs(c.x) < 5.9f && Mathf.Abs(c.z) < 2.42f && c.y < 5.6f && c.y > 0;
            under += ((inside ? 1 : 0) - under) * Mathf.Min(1, dt * 5);
            UpdateDof(dt, focusDist);
        }

        float focus = 10;

        void UpdateDof(float dt, float focusDist)
        {
            if (!volume || !volume.sharedProfile || !volume.sharedProfile.TryGet(out DepthOfField dof)) return;
            bool want = dofOn && mode != CameraMode.Free;
            dof.active = want;
            focus += (focusDist - focus) * Mathf.Min(1, dt * 4);
            dof.focusDistance.value = focus;
            bool close = shot != null && shot.track && mode != CameraMode.Follow;
            // the web version's apertures (0.0012 follow, 0.0014 / 0.0009 close, 0.00035 wide) as f-numbers
            dof.aperture.value = mode == CameraMode.Follow ? 4 : close ? (mode == CameraMode.Cinematic ? 3.2f : 5.6f) : 14;
        }
    }
}
