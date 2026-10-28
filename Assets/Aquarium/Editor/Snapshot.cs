// Renders the scene from the same viewpoints and times of day as the web version's reference
// shots (Docs/reference), into Docs/unity, for side-by-side comparison.
//   -executeMethod Aquarium.EditorTools.Snapshot.BuildAndRender
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquarium.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static Aquarium.World.WebSpace;

namespace Aquarium.EditorTools
{
    public static class Snapshot
    {
        static string Out => System.Environment.GetEnvironmentVariable("AQ_OUT") ?? "Docs/unity";

        public static void BuildAndRender()
        {
            SceneBuilder.BuildAndSave();
            Render();
        }

        [MenuItem("Aquarium/Render Snapshots")]
        public static void Render()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            var day = Object.FindAnyObjectByType<DayCycle>();
            var clock = Object.FindAnyObjectByType<AquariumClock>();
            cam.GetComponent<OrbitCamera>().enabled = false;
            clock.Apply(12);
            // a fresh aquarium in memory (the real save is not touched), alive for a while
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app?.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(7);
            for (int i = 0; i < 900 && app; i++) app.Simulate(1 / 30f);
            Directory.CreateDirectory(Out);
            var views = new (string name, Vector3 pos, Vector3 look)[]
            {
                ("overview", P(0, 3.3f, 14.5f), P(0, 2.6f, 0)),
                ("close", P(-2.2f, 1.6f, 4.2f), P(-1.2f, 1.3f, 0)),
            };
            foreach (var (preset, mode, phase) in new[] { ("day", DayCycle.Mode.Day, 0.25f), ("evening", DayCycle.Mode.Evening, 0.55f), ("night", DayCycle.Mode.Night, 0.75f), ("morning", DayCycle.Mode.Auto, 0.02f) })
            {
                day.mode = mode;
                day.phase = phase;
                day.Snap();
                for (int i = 0; i < 90 && app; i++) app.Simulate(1 / 30f);
                foreach (var (name, pos, look) in views)
                {
                    if (name == "close" && preset != "day" && preset != "night") continue;
                    cam.transform.position = pos;
                    cam.transform.LookAt(look);
                    Object.FindAnyObjectByType<GodRays>()?.Face(pos);
                    Shot(cam, app, $"{Out}/unity-{preset}-{name}.png", 1600, 900);
                }
            }
            Debug.Log("Snapshots rendered");
        }

        /// <summary>Close-ups of the walkers on the decor (shrimp on the wreck first): does any of them sink in?</summary>
        public static void Walkers()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            Object.FindAnyObjectByType<AquariumClock>().Apply(12);
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(5);
            Directory.CreateDirectory(Out);
            var eco = app.Eco;
            int shots = 0;
            var seen = new System.Collections.Generic.HashSet<Aquarium.Fish.Critter>();
            for (int i = 0; i < 30 * 600 && shots < 8; i++)
            {
                app.Simulate(1 / 30f);
                if (i % 15 != 0) continue;
                foreach (var c in eco.critters)
                {
                    bool onDecor = c.pos.y > Aquarium.World.Terrain.SandHeight(c.pos.x, c.pos.z) + 0.2f;
                    bool onShip = c.pos.x > 2.5f && c.pos.x < 4.8f && c.pos.z < 0.2f;
                    if (!onDecor || seen.Contains(c) || (shots < 4 && !onShip) || c.Status == "Прыгает") continue;
                    seen.Add(c);
                    var f = c.Forward; f.y = 0; f.Normalize();
                    var side = new Vector3(f.z, 0, -f.x);
                    var eye = c.pos + side * 0.55f + Vector3.up * 0.22f + new Vector3(0, 0, 0.25f);
                    cam.transform.position = P(eye);
                    cam.transform.LookAt(P(c.pos + Vector3.up * 0.03f));
                    cam.fieldOfView = 40;
                    Shot(cam, app, $"{Out}/walker-{shots}-{c.kind}.png", 900, 600);
                    Debug.Log($"WALKER shot {shots}: {c.kind} {c.name} at {c.pos} ({c.Status})");
                    shots++;
                    break;
                }
            }
        }

        /// <summary>The same close view lit in turn: as built, without the sun, without the lid's shadow, without the fill
        /// and the ambient (what each light gives).</summary>
        public static void LightParts()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            Object.FindAnyObjectByType<AquariumClock>().Apply(12);
            var day = Object.FindAnyObjectByType<DayCycle>();
            day.mode = DayCycle.Mode.Day; day.Snap();
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(7);
            for (int i = 0; i < 300; i++) app.Simulate(1 / 30f);
            Directory.CreateDirectory(Out);
            cam.transform.position = P(1.0f, 1.9f, 4.6f);
            cam.transform.LookAt(P(2.6f, 1.0f, -0.6f));
            var lid = GameObject.Find("Tank").GetComponentsInChildren<MeshRenderer>().Where(r => r.name.StartsWith("Lid") || r.name == "Cover" || r.name.StartsWith("Led")).ToArray();
            void Variant(string name, System.Action on, System.Action off) { on(); Shot(cam, app, $"{Out}/light-{name}.png", 1200, 700); off(); }
            Variant("as-built", () => { }, () => { });
            Variant("no-sun", () => day.sun.enabled = false, () => day.sun.enabled = true);
            Variant("no-lid-shadow", () => { foreach (var r in lid) r.shadowCastingMode = ShadowCastingMode.Off; }, () => { foreach (var r in lid) r.shadowCastingMode = ShadowCastingMode.On; });
            Variant("sun-only", () => { day.fill.enabled = false; RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = Color.black; foreach (var r in lid) r.shadowCastingMode = ShadowCastingMode.Off; },
                () => { day.fill.enabled = true; day.Snap(); foreach (var r in lid) r.shadowCastingMode = ShadowCastingMode.On; });
            Debug.Log("LIGHT parts: " + string.Join(", ", lid.Select(r => $"{r.name} {r.shadowCastingMode}")) + $"; sun {day.sun.intensity:0.00} at {day.sun.transform.forward}, fill {day.fill.intensity:0.00}");
        }

        /// <summary>Views given from outside, in the web version's coordinates: AQ_VIEW="name:x,y,z>x,y,z;name:...".
        /// Fish keep away (none drawn) so the decor itself shows.</summary>
        public static void Views()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            Object.FindAnyObjectByType<AquariumClock>().Apply(12);
            var day = Object.FindAnyObjectByType<DayCycle>();
            day.mode = DayCycle.Mode.Day; day.Snap();
            Directory.CreateDirectory(Out);
            Vector3 V(string t) { var f = t.Split(',').Select(x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture)).ToArray(); return new Vector3(f[0], f[1], f[2]); }
            foreach (var view in (System.Environment.GetEnvironmentVariable("AQ_VIEW") ?? "").Split(';').Where(v => v.Contains(':')))
            {
                var name = view.Split(':')[0];
                var ends = view.Split(':')[1].Split('>');
                cam.transform.position = P(V(ends[0]));
                cam.transform.LookAt(P(V(ends[1])));
                cam.fieldOfView = 40;
                Object.FindAnyObjectByType<GodRays>()?.Face(cam.transform.position);
                Save(cam, $"{Out}/view-{name}.png", 1400, 900);
            }
            Debug.Log("Views rendered");
        }

        // instanced draws pile up within one frame, so one fish per run comes out clean: AQ_FISH=betta
        static string[] Only(params string[] all) => System.Environment.GetEnvironmentVariable("AQ_FISH")?.Split(',') ?? all;

        /// <summary>A strip of frames following one fish from the side, to check how it swims.</summary>
        public static void FishStrip()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(3);
            for (int i = 0; i < 300; i++) app.Simulate(1 / 30f);
            Directory.CreateDirectory("Logs/strip");
            foreach (var sp in Only("goldfish", "neon", "angelfish", "betta"))
            {
                var f = app.Eco.fish.Find(x => x.sp.id == sp && !x.hidden);
                if (f == null) continue;
                for (int k = 0; k < 12; k++)
                {
                    for (int s = 0; s < 2; s++) app.Simulate(1 / 24f);
                    Object.FindAnyObjectByType<AquariumClock>().Apply(10 + k / 12f);
                    var p = new Vector3(-f.pos.x, f.pos.y, f.pos.z);
                    var side = Vector3.Cross(Vector3.up, new Vector3(-f.heading.x, 0, f.heading.z)).normalized;
                    if (side.z < 0) side = -side;   // look from the front glass side, away from the decor
                    float d = f.size * 3.2f;
                    cam.transform.position = p + side * d + Vector3.up * f.size * 0.6f;
                    cam.transform.LookAt(p);
                    Shot(cam, app, $"Logs/strip/{sp}-{k:00}.png", 400, 260);
                }
            }
            Debug.Log("Strip rendered");
        }

        /// <summary>
        /// A fish holding station, its fins fully busy: frames 1/10 s apart, side and top, each drawn
        /// in a place of its own (instanced draws pile up within a frame), into Logs/sweep.
        /// AQ_FISH picks the models.
        /// </summary>
        public static void FinSweep()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            var set = AssetDatabase.LoadAssetAtPath<Aquarium.Fish.FishModelSet>(FishModelBuilder.SetPath);
            Directory.CreateDirectory("Logs/sweep");
            var at = new Vector3(0, 3.5f, 2.2f);
            foreach (var key in Only("goldfish", "betta"))
            {
                var m = set.Get(key);
                float len = Aquarium.Fish.FishSpecies.All[key.Split('-')[0]].len;
                // an ordinary renderer per frame, so the frames do not pile up
                var go = new GameObject("sweep", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.position = at;
                go.GetComponent<MeshFilter>().sharedMesh = m.body;
                var mr = go.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m.bodyMaterial;
                for (int k = 0; k < 8; k++)
                {
                    float t = k * 0.1f;
                    var props = new MaterialPropertyBlock();
                    props.SetVector("_Wag", new Vector4(t * 8.4f, 0.015f * len, 0, len));
                    props.SetFloat("_Pect", 0.6f + Mathf.Sin(t * 9) * 0.5f);
                    props.SetVector("_Tint", Vector4.one);
                    props.SetVector("_FinA", new Vector4(t * 10, 1, 1, 0.5f + 0.5f * Mathf.Sin(t * 3)));
                    props.SetVector("_FinB", new Vector4(0.35f * Mathf.Sin(t * 4.5f), -0.35f * Mathf.Sin(t * 4.5f), 0.6f, t * 6.5f));
                    mr.SetPropertyBlock(props);
                    foreach (var (view, offset) in new[] { ("side", new Vector3(-len * 2.6f, 0, 0)), ("top", new Vector3(0, len * 2.6f, 0.001f)) })
                    {
                        cam.transform.position = at + offset;
                        cam.transform.LookAt(at);
                        Save(cam, $"Logs/sweep/{key}-{view}-{k}.png", 360, 300);
                    }
                }
                Object.DestroyImmediate(go);
            }
            Debug.Log("Sweep rendered");
        }

        /// <summary>One fish model at rest (no wave, fins still) and paddling, from the side and from above.</summary>
        public static void FishRest()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            var set = AssetDatabase.LoadAssetAtPath<Aquarium.Fish.FishModelSet>(FishModelBuilder.SetPath);
            Directory.CreateDirectory("Logs/rest");
            var at = new Vector3(0, 3.5f, 2.2f);
            foreach (var key in Only("goldfish", "neon", "angelfish"))
            {
                var m = set.Get(key);
                float len = Aquarium.Fish.FishSpecies.All[key.Split('-')[0]].len;
                foreach (var (tag, pect) in new[] { ("rest", 0f), ("paddle", 1.2f) })
                foreach (var (view, offset) in new[] { ("side", new Vector3(-len * 2.5f, 0, 0)), ("top", new Vector3(0, len * 2.5f, 0.001f)) })
                {
                    var props = new MaterialPropertyBlock();
                    props.SetVectorArray("_Wag", new[] { new Vector4(0, 0, 0, len) });
                    props.SetFloatArray("_Pect", new[] { pect });
                    props.SetVectorArray("_Tint", new[] { Vector4.one });
                    props.SetVectorArray("_FinA", new[] { new Vector4(0, 1, 0, 0) });   // fins up, still
                    props.SetVectorArray("_FinB", new[] { Vector4.zero });
                    var mat = new[] { Matrix4x4.TRS(at, Quaternion.identity, Vector3.one) };
                    Graphics.RenderMeshInstanced(new RenderParams(m.bodyMaterial) { matProps = props }, m.body, 0, mat, 1);
                    if (m.fins) Graphics.RenderMeshInstanced(new RenderParams(m.finMaterial) { matProps = props }, m.fins, 0, mat, 1);
                    cam.transform.position = at + offset;
                    cam.transform.LookAt(at);
                    Save(cam, $"Logs/rest/{key}-{tag}-{view}.png", 400, 300);
                }
            }
            Debug.Log("Rest rendered");
        }

        /// <summary>
        /// Render a shot. The game draws fish, food and bubbles instanced, and such draws last until the
        /// end of the frame — a tool renders many shots in one frame, so here they are stood in for by
        /// ordinary renderers that exist only for the shot.
        /// </summary>
        static void Shot(Camera cam, Aquarium.Fish.AquariumApp app, string path, int w, int h)
        {
            var proxies = app ? Proxies(app) : null;
            Save(cam, path, w, h);
            if (proxies) Object.DestroyImmediate(proxies);
        }

        static GameObject Proxies(Aquarium.Fish.AquariumApp app)
        {
            var root = new GameObject("snapshot proxies");
            void Add(Mesh mesh, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, MaterialPropertyBlock props)
            {
                var go = new GameObject("proxy");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = scale;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                if (props != null) r.SetPropertyBlock(props);
            }
            var eco = app.Eco;
            app.Draw();   // shader globals (plants pushed aside by the fish)
            foreach (var f in eco.fish)
            {
                if (f.hidden) continue;
                var m = app.fishModels.Get(f.modelKey);
                if (m == null) continue;
                var props = new MaterialPropertyBlock();
                props.SetVector("_Wag", new Vector4(f.wag.x, -f.wag.y, -f.wag.z, f.wag.w));
                props.SetFloat("_Pect", f.pect);
                var t = f.rec.Gene.Tint;
                props.SetVector("_Tint", new Vector4((float)t[0], (float)t[1], (float)t[2], 1));
                var pos = new Vector3(-f.pos.x, f.pos.y, f.pos.z);
                var rot = Aquarium.Fish.FishRenderer.ToUnity(f.rotation);
                Add(m.body, m.bodyMaterial, pos, rot, f.scaleXYZ, props);
                if (m.fins) Add(m.fins, m.finMaterial, pos, rot, f.scaleXYZ, props);
            }
            if (eco.plants != null)
                foreach (var (mesh, sub, mat, m) in eco.plants.Instances())
                {
                    var mats = new Material[mesh.subMeshCount];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    Add(mesh, mat, m.GetPosition(), m.rotation, m.lossyScale, null);
                    if (mats.Length > 1) root.transform.GetChild(root.transform.childCount - 1).GetComponent<MeshRenderer>().sharedMaterials = mats;
                }
            foreach (var f in eco.food)
            {
                var props = new MaterialPropertyBlock();
                props.SetColor("_BaseColor", f.color);
                Add(app.foodMesh, app.foodMaterial, new Vector3(-f.pos.x, f.pos.y, f.pos.z), WebSpace.Rot(f.rot.x, f.rot.y, f.rot.z), new Vector3(f.r, f.r * f.stretch, f.r), props);
            }
            foreach (var b in eco.bubbles.list)
                Add(app.bubbleMesh, app.bubbleMaterial, new Vector3(-b.pos.x, b.pos.y, b.pos.z), Quaternion.identity, Vector3.one * b.r, null);
            return root;
        }

        /// <summary>Frames following each kind of critter, from the front glass side.</summary>
        public static void CritterStrip()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(int.TryParse(System.Environment.GetEnvironmentVariable("AQ_SEED"), out var seed) ? seed : 4);
            for (int i = 0; i < 600; i++) app.Simulate(1 / 30f);
            app.Eco.Feed(0);
            for (int i = 0; i < 450; i++) app.Simulate(1 / 30f);
            Directory.CreateDirectory("Logs/critters");
            var picks = new[] { ("crab", "crab"), ("shrimp", "shrimp"), ("snail", "mystery"), ("snail", "nerite"), ("snail", "ramshorn") };
            foreach (var (kind, type) in picks)
            {
                var c = app.Eco.critters.Find(x => x.kind == kind && (kind != "snail" || ((Aquarium.Fish.Snail)x).type == type));
                if (c == null) continue;
                for (int k = 0; k < 6; k++)
                {
                    for (int s = 0; s < 10; s++) app.Simulate(1 / 30f);
                    var p = c.root.position;
                    float d = kind == "crab" ? 0.9f : 0.55f;
                    cam.transform.position = p + new Vector3(0.15f, 0.25f, d);
                    cam.transform.LookAt(p + Vector3.up * 0.05f);
                    Shot(cam, app, $"Logs/critters/{type}-{k}.png", 400, 300);
                }
            }
            Debug.Log("Critter strip rendered");
        }

        /// <summary>Feed a fresh aquarium and report how the fish react (numbers, no pictures).</summary>
        public static void FeedCheck()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(5);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 300; i++) app.Simulate(1 / 30f);
            double msPerStep = sw.Elapsed.TotalMilliseconds / 300;
            app.Eco.Feed(0);
            int maxEating = 0, dropped = 0;
            for (int i = 0; i < 30 * 40; i++)
            {
                app.Simulate(1 / 30f);
                maxEating = Mathf.Max(maxEating, app.Eco.fish.Count(f => f.state == Aquarium.Fish.FishState.EAT));
                dropped = Mathf.Max(dropped, app.Eco.food.Count);
                if (i == 30 * 10) Debug.Log($"after 10 s: {app.Eco.food.Count} pieces left");
            }
            Debug.Log($"FEED CHECK: {app.Eco.fish.Count} fish, simulation {msPerStep:F3} ms/step; up to {maxEating} fish eating at once; food peak {dropped}, left after 40 s: {app.Eco.food.Count} (grounded {app.Eco.food.Count(f => f.grounded)}); stories: {string.Join(" | ", app.Eco.storyLog.Take(8).Select(x => x.text))}");
        }

        /// <summary>
        /// The world in fast-forward: three days away, then 20 minutes of simulation in which the
        /// world clock runs 2 hours per second; reports what happened (stories, broods, plants).
        /// </summary>
        public static void LifeCheck()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            double clock = 1.7e12;
            UnityEngine.Random.InitState(7);
            app.Boot(new Aquarium.Life.MemoryWorldStore(), new Aquarium.Life.WorldOptions { RealNow = () => clock, AwayHours = 72 });
            var eco = app.Eco;
            Debug.Log($"LIFE CHECK away: {(app.Away == null ? "none" : Aquarium.Fish.AquariumApp.Describe(app.Away))}; fish {eco.fish.Count}, plants {app.plantRoot.childCount}");
            var kinds = new Dictionary<string, int>();
            eco.OnStory += s => kinds[s.kind] = (kinds.TryGetValue(s.kind, out int n) ? n : 0) + 1;
            int fish0 = eco.fish.Count;
            const float dt = 1 / 30f;
            for (int i = 0; i < 30 * 60 * 20; i++)
            {
                clock += dt * 2 * 3600 * 1000;
                app.Simulate(dt);
                if (i == 30 * 60 * 5)
                {
                    eco.ClickProp("chest"); eco.ClickProp("bottle"); eco.ClickProp("crystal"); eco.StartDuck();
                }
                if (i == 30 * 60 * 5 + 30 * 3)
                    Debug.Log($"props: lid {app.layout.chestLid.localEulerAngles}, duck {(app.layout.duck.gameObject.activeSelf ? app.layout.duck.localPosition.ToString() : "hidden")}, crystal flash {eco.crystalFlash:F2}");
            }
            Debug.Log($"LIFE CHECK after {(clock - 1.7e12) / 3600e3:F0} world hours: fish {fish0} -> {eco.fish.Count}, plants {app.plantRoot.childCount} (runners {app.World.State.ExtraPlants.Count}), duck {(app.layout.duck.gameObject.activeSelf ? "still out" : "gone")}, secrets [{string.Join(", ", app.World.State.Secrets)}]");
            Debug.Log("LIFE CHECK stories: " + string.Join(", ", kinds.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            Debug.Log("LIFE CHECK history: " + string.Join(" | ", app.World.State.History.Skip(Mathf.Max(0, app.World.State.History.Count - 12)).Select(h => h.Text)));
        }

        static void Save(Camera cam, string path, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.aspect = (float)w / h;
            cam.Render();
            cam.Render();   // a second frame: temporal effects and the volume system settle
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            rt.Release();
        }
    }
}
