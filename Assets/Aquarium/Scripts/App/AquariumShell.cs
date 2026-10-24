// Everything around the living aquarium (the web version's main.js): the camera director,
// the interface, the sound, and input — clicks on fish and critters (the card), on the secret
// props, knocks on the glass, the cursor fish come to look at; keys F (feed), O (observe),
// C (cinematic), H (hide the interface), Esc; a gamepad for a TV. In full screen the cursor and
// the chrome hide when the pointer rests.
using System;
using System.Linq;
using Aquarium.Fish;
using Aquarium.World;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using FishT = Aquarium.Fish.Fish;
using Terrain = Aquarium.World.Terrain;

namespace Aquarium.App
{
    public sealed class AquariumShell : MonoBehaviour
    {
        public AquariumApp app;
        public DayCycle day;
        public Camera cam;
        public OrbitCamera orbit;
        public Volume volume;
        public AquaAudio audio;
        public UIDocument document;

        public Director director;
        public Quality quality;
        public AquariumUI ui;
        public object selected;
        public CameraMode mode = CameraMode.Free;
        bool hiddenUi, started;

        const float IdleSeconds = 3;
        float lastInput, lastMove, ringAlpha, fpsT, cpuMs, gpuMs;
        int fpsN, timed;
        readonly FrameTiming[] timing = new FrameTiming[1];
        /// <summary>The last second's frame rate and frame times (the benchmark reads them).</summary>
        public float LastFps { get; private set; }
        public float LastCpuMs { get; private set; }
        public float LastGpuMs { get; private set; }
        Vector2 lastPointer, downPos;
        bool down, wasFull;

        Ecosystem Eco => app.Eco;
        static Vector3 W(Vector3 u) => new Vector3(-u.x, u.y, u.z);

        void Start()
        {
            // a measured run works on a world of its own, not the user's
            string bench = Benchmark.Arg();
            if (bench != null) AquariumApp.storeOverride = new Aquarium.Life.MemoryWorldStore();
            app.Boot();
            director = new Director(cam, orbit, Eco, volume);
            quality = new Quality(volume);
            quality.lowered = name =>
            {
                ui.Hint($"Качество снижено до «{name}», чтобы аквариум не тормозил");
                ui.QualityChanged(quality.Level);
            };
            ui = new AquariumUI(this, document.rootVisualElement);
            orbit.inputBlocked = PointerOverUi;
            ApplyPrefs();
            ui.FillSettings();
            Eco.OnStory += s =>
            {
                ui.Story(s);
                director.OnStory(s);
                if (s.rare) audio.Chime();
            };
            Eco.bubbles.onPop = b => { if (Rnd.Chance(0.25f)) audio.Pop(b.pos.x); };
            ui.Started(app.Away);
            wasFull = Screen.fullScreen;
            ui.SetFullScreen(wasFull);
            started = true;
            if (bench != null) gameObject.AddComponent<Benchmark>().output = bench;
        }

        // ---------------------------------------------------------------- settings
        void ApplyPrefs()
        {
            foreach (var k in new[] { "cycle", "stories", "dof", "bloom", "exposure", "fps", "master", "music", "fx", "autores" })
                if (Prefs.Has(k)) ApplyPref(k, Prefs.Get(k, 0f));
            // on the first start the preset is chosen by the graphics card
            quality.Apply(Prefs.Has("quality") ? (int)Prefs.Get("quality", 2f) : Quality.Suggest());
            ui.ShowFps(Prefs.Get("fps", 1f) > 0.5f);
            if (Enum.TryParse(Prefs.Get("dayMode", "Auto"), out DayCycle.Mode m)) { day.mode = m; ui.SetDayLabel(DayTitle(m)); }
        }

        public void SetPref(string k, float v)
        {
            Prefs.Set(k, v);
            ApplyPref(k, v);
        }

        void ApplyPref(string k, float v)
        {
            switch (k)
            {
                case "cycle": day.cycleMinutes = v; break;
                case "stories": ui.showStories = v > 0.5f; break;
                case "quality": quality.Apply((int)v); break;
                case "autores": quality.SetAuto(v > 0.5f); break;
                case "dof": director.dofOn = v > 0.5f; break;
                case "bloom":
                    // the web version's bloom strength (0.38 by default) as URP bloom intensity (0.5)
                    if (volume && volume.sharedProfile && volume.sharedProfile.TryGet(out Bloom bloom)) bloom.intensity.value = v / 0.38f * 0.5f;
                    break;
                case "exposure": day.exposureMul = v; break;
                case "fps": ui.ShowFps(v > 0.5f); break;
                case "master": audio.master = v; break;
                case "music": audio.music = v; break;
                case "fx": audio.fx = v; break;
            }
        }

        static string DayTitle(DayCycle.Mode m) => m switch { DayCycle.Mode.Day => "День", DayCycle.Mode.Evening => "Вечер", DayCycle.Mode.Night => "Ночь", _ => "Авто" };

        public void CycleDayMode()
        {
            var order = new[] { DayCycle.Mode.Auto, DayCycle.Mode.Day, DayCycle.Mode.Evening, DayCycle.Mode.Night };
            var m = order[(Array.IndexOf(order, day.mode) + 1) % order.Length];
            day.mode = m;
            Prefs.Set("dayMode", m.ToString());
            ui.SetDayLabel(DayTitle(m));
            ui.Hint(m == DayCycle.Mode.Auto ? "Смена дня и ночи: автоматически" : $"Освещение: {DayTitle(m)}");
        }

        public void ToggleFullScreen()
        {
            if (Screen.fullScreen) Screen.fullScreenMode = FullScreenMode.Windowed;
            else
            {
                var r = Screen.currentResolution;
                Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
            }
        }

        // ---------------------------------------------------------------- the app's actions
        public void Feed()
        {
            Eco.Feed();
            audio.Sprinkle();
        }

        public void SetMode(CameraMode m)
        {
            if (m == mode && m != CameraMode.Free) m = CameraMode.Free;
            mode = m;
            bool immersive = m == CameraMode.Observe || m == CameraMode.Cinematic;
            ui.SetImmersive(immersive || hiddenUi);
            if (immersive) director.SetMode(m);
            else
            {
                director.SetMode(selected != null ? CameraMode.Follow : CameraMode.Free);
                if (selected != null) director.Follow(selected);
                else director.FlyTo(Director.Views[0].pos, Director.Views[0].look, 2.5f);
            }
        }

        public void Select(object o)
        {
            selected = o;
            if (o == null)
            {
                ui.HideCard();
                director.Unfollow();
                return;
            }
            if (mode != CameraMode.Free) SetMode(CameraMode.Free);
            ui.ShowCard(o);
            director.Follow(o);
            ui.FollowChanged();
            audio.Chime();
        }

        public string SaveBackup()
        {
            Eco.Save();
            return Backup.Save(app.World.ToJson());
        }

        /// <summary>Replace the aquarium with a saved copy and start again with it.</summary>
        public bool LoadBackup(string path)
        {
            string json;
            try { json = System.IO.File.ReadAllText(path); }
            catch (System.IO.IOException) { return false; }
            if (!app.World.Import(json)) return false;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return true;
        }

        // ---------------------------------------------------------------- picking (web coordinates)
        Ray WebRay(Vector2 screen)
        {
            var r = cam.ScreenPointToRay(screen);
            return new Ray(W(r.origin), W(r.direction));
        }

        static float Hit(Ray r, Vector3 c, float rad)
        {
            var oc = r.origin - c;
            float b = Vector3.Dot(oc, r.direction), cc = oc.sqrMagnitude - rad * rad, h = b * b - cc;
            if (h < 0) return float.MaxValue;
            float t = -b - Mathf.Sqrt(h);
            return t > 0 ? t : float.MaxValue;
        }

        object PickCreature(Ray r, out float dist)
        {
            object best = null;
            dist = float.MaxValue;
            foreach (var f in Eco.fish)
            {
                if (f.hidden) continue;
                float t = Hit(r, f.pos, Mathf.Max(0.07f, f.size * 0.5f));
                if (t < dist) { dist = t; best = f; }
            }
            foreach (var c in Eco.critters)
            {
                float t = Hit(r, c.pos + new Vector3(0, 0.05f, 0), c.size * 0.6f);
                if (t < dist) { dist = t; best = c; }
            }
            return best;
        }

        string PickProp(Ray r, float before)
        {
            var L = app.layout;
            string best = null;
            foreach (var (kind, at, rad) in new[] { ("chest", L.chestPosition, 0.45f), ("bottle", L.bottlePosition, 0.35f), ("crystal", L.secretCave, 0.3f) })
            {
                float t = Hit(r, W(at), rad);
                if (t < before) { before = t; best = kind; }
            }
            return best;
        }

        /// <summary>Where the ray meets a glass pane of the tank (front or a side), or null.</summary>
        static Vector3? GlassPoint(Ray r)
        {
            float hw = Terrain.W / 2, hd = Terrain.D / 2;
            Vector3? best = null;
            float bt = float.MaxValue;
            void Plane(Vector3 n, float d)
            {
                float den = Vector3.Dot(n, r.direction);
                if (Mathf.Abs(den) < 1e-5f) return;
                float t = (d - Vector3.Dot(n, r.origin)) / den;
                if (t <= 0 || t >= bt) return;
                var p = r.origin + r.direction * t;
                if (Mathf.Abs(p.x) > hw + 0.01f || Mathf.Abs(p.z) > hd + 0.01f || p.y < 0.3f || p.y > Terrain.WaterY) return;
                bt = t; best = p;
            }
            if (r.origin.z > hd) Plane(Vector3.forward, hd);
            if (r.origin.x > hw) Plane(Vector3.right, hw);
            if (r.origin.x < -hw) Plane(Vector3.left, hw);
            return best;
        }

        bool PointerOverUi()
        {
            var panel = document.rootVisualElement.panel;
            if (panel == null || Pointer.current == null) return false;
            var pos = RuntimePanelUtils.ScreenToPanel(panel, ScreenToUi(Pointer.current.position.ReadValue()));
            var e = panel.Pick(pos);
            return e != null && e != document.rootVisualElement;
        }

        static Vector2 ScreenToUi(Vector2 s) => new Vector2(s.x, Screen.height - s.y);

        Vector2 ToPanel(Vector2 screen) => RuntimePanelUtils.ScreenToPanel(document.rootVisualElement.panel, ScreenToUi(screen));

        void Click(Vector2 screen)
        {
            var r = WebRay(screen);
            var c = PickCreature(r, out float dist);
            if (c != null) { Select(c); return; }
            var prop = PickProp(r, dist);
            if (prop != null) { Eco.ClickProp(prop); audio.Chime(); return; }
            if (GlassPoint(r) is Vector3 g)
            {
                var p = new Vector3(Mathf.Clamp(g.x, -5.6f, 5.6f), Mathf.Clamp(g.y, 0.6f, 5.3f), Mathf.Clamp(g.z, -2.1f, 2.1f));
                Eco.Tap(p);
                audio.Tap();
                ui.TapFlash(ToPanel(screen));
                return;
            }
            if (selected != null) Select(null);
        }

        // ---------------------------------------------------------------- frame
        void Update()
        {
            if (!started) return;
            float dt = Time.unscaledDeltaTime;
            bool immersive = mode == CameraMode.Observe || mode == CameraMode.Cinematic;
            Keys(immersive);
            Pointer_(immersive);

            director.Update(Mathf.Min(dt, 0.1f));
            day.underwater = director.under;
            audio.night = Eco.night;
            ui.SetLetterbox(director.letterbox, director.fade);
            Ring(immersive, dt);
            ui.Update(dt);
            var panel = document.rootVisualElement.panel;
            if (panel != null) ui.SetNarrow(document.rootVisualElement.layout.width < 720);
            if (Screen.fullScreen != wasFull) { wasFull = Screen.fullScreen; ui.SetFullScreen(wasFull); }

            // full screen (or the immersive modes) at rest: only the aquarium and its stories
            bool sleepy = (Screen.fullScreen || immersive) && !ui.Busy && Time.unscaledTime - lastInput > IdleSeconds && !PointerOverUi();
            ui.SetIdle(sleepy);
            UnityEngine.Cursor.visible = !sleepy;

            fpsN++;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timing) > 0) { cpuMs += (float)timing[0].cpuMainThreadFrameTime; gpuMs += (float)timing[0].gpuFrameTime; timed++; }
            if (Time.unscaledTime - fpsT > 1)
            {
                float fps = fpsN / (Time.unscaledTime - fpsT);
                string sc = quality.scale < 1 ? $" ({Mathf.RoundToInt(quality.scale * 100)}%)" : "";
                string ms = timed > 0 ? $" · CPU {cpuMs / timed:0.0} мс" + (gpuMs > 0 ? $" · GPU {gpuMs / timed:0.0} мс" : "") : "";
                ui.SetFps($"{Mathf.RoundToInt(fps)} fps{ms} · {Screen.width}×{Screen.height}{sc} · {QualitySettings.names[quality.Level]} · {SystemInfo.graphicsDeviceName}");
                if (!benchmarking) quality.Adapt(fps);
                LastFps = fps;
                LastCpuMs = timed > 0 ? cpuMs / timed : 0;
                LastGpuMs = timed > 0 ? gpuMs / timed : 0;
                fpsN = 0; fpsT = Time.unscaledTime; cpuMs = 0; gpuMs = 0; timed = 0;
            }
        }

        void Keys(bool immersive)
        {
            if (ui.Typing) return;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.anyKey.wasPressedThisFrame) lastInput = Time.unscaledTime;
                if (k.escapeKey.wasPressedThisFrame) { if (mode != CameraMode.Free) SetMode(CameraMode.Free); else Select(null); }
                if (k.fKey.wasPressedThisFrame) Feed();
                if (k.cKey.wasPressedThisFrame) SetMode(CameraMode.Cinematic);
                if (k.oKey.wasPressedThisFrame) SetMode(CameraMode.Observe);
                if (k.hKey.wasPressedThisFrame) ToggleUi(immersive);
                if (k.f11Key.wasPressedThisFrame) ToggleFullScreen();
            }
            var g = Gamepad.current;
            if (g != null)
            {
                if (g.buttonSouth.wasPressedThisFrame) Feed();
                if (g.buttonWest.wasPressedThisFrame) SetMode(CameraMode.Observe);
                if (g.buttonNorth.wasPressedThisFrame) SetMode(CameraMode.Cinematic);
                if (g.buttonEast.wasPressedThisFrame) { if (mode != CameraMode.Free) SetMode(CameraMode.Free); else Select(null); }
                if (g.selectButton.wasPressedThisFrame) ToggleUi(immersive);
                if (g.rightShoulder.wasPressedThisFrame) ui.Hint($"Камера: {director.CycleView()}");
                if (g.leftShoulder.wasPressedThisFrame) CycleDayMode();
                if (g.wasUpdatedThisFrame) lastInput = Time.unscaledTime;
            }
        }

        void ToggleUi(bool immersive)
        {
            hiddenUi = !hiddenUi;
            ui.SetImmersive(hiddenUi || immersive);
        }

        void Pointer_(bool immersive)
        {
            var p = Pointer.current;
            if (p == null) return;
            var pos = p.position.ReadValue();
            bool moved = (pos - lastPointer).sqrMagnitude > 0.5f;
            if (moved) { lastPointer = pos; lastInput = Time.unscaledTime; }
            if (Mouse.current != null && Mouse.current.scroll.ReadValue().sqrMagnitude > 0) lastInput = Time.unscaledTime;
            if (immersive)
            {
                if (moved && !(hiddenUi && !immersive)) ui.PokeExit();
                ui.SetHover(null, default);
                Eco.cursor = (false, default);
                down = false;
                return;
            }
            bool overUi = PointerOverUi();
            if (moved && !overUi)
            {
                var r = WebRay(pos);
                var c = PickCreature(r, out _);
                ui.SetHover(c, ToPanel(pos));
                // fish come to look at the cursor on the glass
                if (GlassPoint(r) is Vector3 g)
                {
                    g.z = Mathf.Min(g.z, Terrain.D / 2 - 0.35f);
                    Eco.cursor = (true, g);
                    lastMove = Time.unscaledTime;
                }
                else Eco.cursor = (false, default);
            }
            else if (overUi) ui.SetHover(null, default);
            if (Eco.cursor.active && Time.unscaledTime - lastMove > 3.5f) Eco.cursor = (false, default);

            if (p.press.wasPressedThisFrame) { lastInput = Time.unscaledTime; down = !overUi; downPos = pos; }
            if (p.press.wasReleasedThisFrame && down)
            {
                down = false;
                if ((pos - downPos).magnitude <= 6) Click(pos);
            }
        }

        /// <summary>The ring around the selected creature, on the screen.</summary>
        void Ring(bool immersive, float dt)
        {
            var s = selected;
            bool show = s != null && !immersive && !(s is FishT f && f.hidden);
            ringAlpha += ((show ? 1 : 0) - ringAlpha) * Mathf.Min(1, dt * 6);
            if (s == null || document.rootVisualElement.panel == null) { ui.SetRing(default, 0, 0); return; }
            Vector3 wp = s is FishT ff ? ff.pos : ((Critter)s).pos;
            float size = s is FishT fs ? fs.size : ((Critter)s).size;
            var u = W(wp);
            var sp = cam.WorldToScreenPoint(u);
            if (sp.z <= 0) { ui.SetRing(default, 0, 0); return; }
            float rw = size * 0.9f + 0.12f + Mathf.Sin(Time.time * 3) * 0.01f;
            var edge = cam.WorldToScreenPoint(u + cam.transform.right * rw);
            var c = ToPanel(sp);
            float r = (ToPanel(edge) - c).magnitude;
            ui.SetRing(c, r, ringAlpha);
        }

        void OnApplicationPause(bool paused) { if (paused && started) Eco.Save(); }
        void OnDestroy() => quality?.Restore();

        /// <summary>The benchmark sets presets itself; the automatic resolution keeps out of its way.</summary>
        [System.NonSerialized] public bool benchmarking;
    }
}
