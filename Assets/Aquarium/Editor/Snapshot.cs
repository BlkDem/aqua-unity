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
                MeshRenderer finRenderer = null;
                if (m.fins)
                {
                    var finGo = new GameObject("sweep fins", typeof(MeshFilter), typeof(MeshRenderer));
                    finGo.transform.SetParent(go.transform, false);
                    finGo.GetComponent<MeshFilter>().sharedMesh = m.fins;
                    finRenderer = finGo.GetComponent<MeshRenderer>();
                    finRenderer.sharedMaterial = m.finMaterial;
                }
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
                    if (finRenderer) finRenderer.SetPropertyBlock(props);
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

        /// <summary>One fish model at rest (no wave, fins still) and paddling, from the side and from above, instanced as in the game; AQ_OPEN opens its mouth and gill covers.</summary>
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
                    props.SetVectorArray("_Face", new[] { System.Environment.GetEnvironmentVariable("AQ_OPEN") != null ? new Vector4(1, 1, 0, 0) : Vector4.zero });
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
        /// Every fish model close up in the tank's daylight, its fins moving: from the side, from the
        /// front quarter and against the light, into Logs/fins (AQ_OUT). AQ_FISH picks the models,
        /// AQ_NOFINS leaves out the fin mesh, AQ_LOD=0|1 takes the simpler meshes, AQ_DOF adds the
        /// cinematic mode's depth of field and a shot of the tail's root, AQ_QUALITY picks the preset.
        /// </summary>
        public static void BuildFinLook()
        {
            FishModelBuilder.Build();
            FinLook();
        }

        public static void FinLook()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            cam.fieldOfView = 30;
            Object.FindAnyObjectByType<AquariumClock>().Apply(12);
            var day = Object.FindAnyObjectByType<DayCycle>();
            day.mode = DayCycle.Mode.Day; day.Snap();
            var set = AssetDatabase.LoadAssetAtPath<Aquarium.Fish.FishModelSet>(FishModelBuilder.SetPath);
            string dir = System.Environment.GetEnvironmentVariable("AQ_OUT") ?? "Logs/fins";
            Directory.CreateDirectory(dir);
            var at = new Vector3(0, 3.2f, 1.5f);
            int wasQuality = QualitySettings.GetQualityLevel();
            if (int.TryParse(System.Environment.GetEnvironmentVariable("AQ_QUALITY"), out var quality)) QualitySettings.SetQualityLevel(quality, true);
            bool blur = System.Environment.GetEnvironmentVariable("AQ_DOF") != null;
            UnityEngine.Rendering.Universal.DepthOfField dof = null;
            if (blur) Object.FindAnyObjectByType<Volume>().sharedProfile.TryGet(out dof);
            if (dof) { dof.active = true; dof.aperture.value = 3.2f; }
            int w = blur ? 1280 : 640, h = blur ? 960 : 480;
            foreach (var key in Only(set.models.Select(x => x.key).ToArray()))
            {
                var m = set.Get(key);
                if (m == null) continue;
                float len = Aquarium.Fish.FishSpecies.All[key.Split('-')[0]].len;
                var root = new GameObject("fin look");
                root.transform.position = at;
                root.transform.rotation = Quaternion.Euler(0, 90, 0);   // nose along +x, the side towards the front glass
                var parts = new List<MeshRenderer>();
                void Part(Mesh mesh, Material mat)
                {
                    if (!mesh) return;
                    var go = new GameObject("part");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    parts.Add(r);
                }
                int lod = int.TryParse(System.Environment.GetEnvironmentVariable("AQ_LOD"), out var level) ? level : -1;
                Part(lod >= 0 ? m.bodyLods[lod] : m.body, m.bodyMaterial);
                var finMesh = lod >= 0 && m.finLods.Length > lod ? m.finLods[lod] : m.fins;
                if (System.Environment.GetEnvironmentVariable("AQ_NOFINS") == null) Part(finMesh, m.finMaterial);
                if (blur && finMesh && set.finDepth)
                {
                    m.FinDepthMaterials(set.finDepth, out var last, out var early);
                    Part(finMesh, last);
                    Part(finMesh, early);
                }
                var props = new MaterialPropertyBlock();
                props.SetVector("_Wag", new Vector4(1.1f, 0.02f * len, 0, len));
                props.SetFloat("_Pect", 0.45f);
                props.SetVector("_Tint", Vector4.one);
                props.SetVector("_FinA", new Vector4(1.3f, 1, 0.4f, 0.3f));
                props.SetVector("_FinB", new Vector4(0.15f, -0.1f, 0.2f, 1.0f));
                foreach (var r in parts) r.SetPropertyBlock(props);
                var views = new List<(string name, Vector3 offset, Vector3 look)>
                {
                    ("side", new Vector3(0, 0.05f, 2.6f), Vector3.zero),
                    ("quarter", new Vector3(1.5f, 0.35f, 2.0f), Vector3.zero),
                    ("up", new Vector3(0.4f, -1.3f, 2.2f), Vector3.zero),   // from below, against the light from the lid
                };
                if (blur) views.Add(("tail", new Vector3(-0.45f, 0.05f, 1.0f), new Vector3(-0.45f, 0, 0)));
                foreach (var (view, offset, look) in views)
                {
                    cam.transform.position = at + offset * len;
                    cam.transform.LookAt(at + look * len);
                    if (dof) dof.focusDistance.value = (offset - look).magnitude * len;
                    Save(cam, $"{dir}/{key}-{view}.png", w, h);
                }
                Object.DestroyImmediate(root);
            }
            if (dof) dof.active = false;
            QualitySettings.SetQualityLevel(wasQuality, true);
            Debug.Log("Fin look rendered");
        }

        /// <summary>
        /// Every fish model's head from the side, without perspective (so that a picture measures the
        /// model: 1000 pixels of height are 2 * AQ_HEAD_SIZE units around z = AQ_HEAD_Z share of the length,
        /// y = 0), into Logs/face (AQ_OUT). AQ_FISH picks the models.
        /// </summary>
        public static void HeadLook()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            cam.orthographic = true;
            Object.FindAnyObjectByType<AquariumClock>().Apply(12);
            var day = Object.FindAnyObjectByType<DayCycle>();
            day.mode = DayCycle.Mode.Day; day.Snap();
            var set = AssetDatabase.LoadAssetAtPath<Aquarium.Fish.FishModelSet>(FishModelBuilder.SetPath);
            string dir = System.Environment.GetEnvironmentVariable("AQ_OUT") ?? "Logs/face";
            Directory.CreateDirectory(dir);
            var at = new Vector3(0, 3.2f, 1.5f);
            var report = new System.Text.StringBuilder();
            foreach (var key in Only(set.models.Select(x => x.key).ToArray()))
            {
                var m = set.Get(key);
                if (m == null) continue;
                float len = Aquarium.Fish.FishSpecies.All[key.Split('-')[0]].len;
                var root = new GameObject("head look");
                root.transform.position = at;
                root.transform.rotation = Quaternion.Euler(0, 90, 0);
                var parts = new List<MeshRenderer>();
                foreach (var (mesh, mat) in new[] { (m.body, m.bodyMaterial), (m.fins, m.finMaterial) })
                {
                    if (!mesh) continue;
                    var go = new GameObject("part");
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = mat;
                    parts.Add(r);
                }
                var props = new MaterialPropertyBlock();
                props.SetVector("_Wag", new Vector4(0, 0, 0, len));
                props.SetFloat("_Pect", 0);
                props.SetVector("_Tint", Vector4.one);
                props.SetVector("_FinA", new Vector4(0, 1, 0, 0));
                props.SetVector("_FinB", Vector4.zero);
                foreach (var r in parts) r.SetPropertyBlock(props);
                float centre = 0.32f * len, half = 0.3f * len;   // model z, half the height of the picture
                cam.orthographicSize = half;
                foreach (var (tag, open, turn) in new[] { ("", 0f, 0f), ("-half", 0.35f, 0f), ("-open", 1f, 0f), ("-open-q", 1f, 40f), ("-half-q", 0.35f, 40f) })
                {
                    props.SetVector("_Face", new Vector4(open, open, 0, 0));
                    foreach (var r in parts) r.SetPropertyBlock(props);
                    root.transform.rotation = Quaternion.Euler(0, 90 + turn, 0);
                    var head = root.transform.TransformPoint(new Vector3(0, 0, centre));
                    cam.transform.position = head + new Vector3(0, 0, 2);
                    cam.transform.rotation = Quaternion.LookRotation(Vector3.back);
                    Save(cam, $"{dir}/{key}{tag}.png", 1000, 1000);
                }
                report.AppendLine($"{key} len={len} centre_z={centre} half={half} bounds z {m.body.bounds.min.z:0.###}..{m.body.bounds.max.z:0.###} y {m.body.bounds.min.y:0.###}..{m.body.bounds.max.y:0.###} x {m.body.bounds.min.x:0.###}..{m.body.bounds.max.x:0.###}");
                Object.DestroyImmediate(root);
            }
            File.WriteAllText($"{dir}/heads.txt", report.ToString());
            Debug.Log("Head look rendered");
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
                props.SetVector("_FinA", f.finA);
                props.SetVector("_FinB", f.finB);
                props.SetVector("_Face", f.faceOpen);
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

        /// <summary>
        /// A fresh aquarium: how each kind of fish breathes (numbers in the log) and frames of its head
        /// following it, then frames of a fish going for food, into Logs/face-strip (AQ_OUT).
        /// </summary>
        public static void FaceStrip()
        {
            EditorSettings.asyncShaderCompilation = false;
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var cam = Camera.main;
            cam.GetComponent<OrbitCamera>().enabled = false;
            cam.fieldOfView = 30;
            var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(5);
            for (int i = 0; i < 300; i++) app.Simulate(1 / 30f);
            string dir = System.Environment.GetEnvironmentVariable("AQ_OUT") ?? "Logs/face-strip";
            Directory.CreateDirectory(dir);

            // breaths per second, how far the mouth and the gill covers go
            var byKind = app.Eco.fish.GroupBy(f => f.sp.id).ToDictionary(g => g.Key, g => g.First());
            var maxM = byKind.Keys.ToDictionary(k => k, k => 0f);
            var maxG = byKind.Keys.ToDictionary(k => k, k => 0f);
            var beats = byKind.Keys.ToDictionary(k => k, k => 0);
            var was = byKind.Keys.ToDictionary(k => k, k => false);
            for (int i = 0; i < 600; i++)
            {
                app.Simulate(1 / 30f);
                foreach (var (k, f) in byKind)
                {
                    maxM[k] = Mathf.Max(maxM[k], f.faceOpen.x);
                    maxG[k] = Mathf.Max(maxG[k], f.faceOpen.y);
                    bool open = f.faceOpen.x > 0.05f;
                    if (open && !was[k]) beats[k]++;
                    was[k] = open;
                }
            }
            foreach (var k in byKind.Keys) Debug.Log($"BREATH {k}: {beats[k] / 20f:0.00} per second, mouth up to {maxM[k]:0.00}, gill covers up to {maxG[k]:0.00}");

            void Follow(Aquarium.Fish.Fish f, string name, int frames, int steps)
            {
                for (int k = 0; k < frames; k++)
                {
                    for (int s = 0; s < steps; s++) app.Simulate(1 / 30f);
                    var rot = Aquarium.Fish.FishRenderer.ToUnity(f.rotation);
                    var pos = new Vector3(-f.pos.x, f.pos.y, f.pos.z);
                    var head = pos + rot * new Vector3(0, 0, 0.3f * f.sp.len * f.scaleXYZ.z);
                    cam.transform.position = head + rot * new Vector3(0.9f * f.sp.len, 0.1f * f.sp.len, 0.25f * f.sp.len);
                    cam.transform.LookAt(head, Vector3.up);
                    Debug.Log($"FRAME {name}-{k}: mouth {f.faceOpen.x:0.00} gills {f.faceOpen.y:0.00} state {f.state}");
                    // this fish alone, as an ordinary renderer: the instanced draws would pile up
                    var model = app.fishModels.Get(f.modelKey);
                    var props = new MaterialPropertyBlock();
                    props.SetVector("_Wag", new Vector4(f.wag.x, -f.wag.y, -f.wag.z, f.wag.w));
                    props.SetFloat("_Pect", f.pect);
                    props.SetVector("_Tint", Vector4.one);
                    props.SetVector("_FinA", f.finA);
                    props.SetVector("_FinB", f.finB);
                    props.SetVector("_Face", f.faceOpen);
                    var root = new GameObject("followed");
                    root.transform.SetPositionAndRotation(pos, rot);
                    root.transform.localScale = f.scaleXYZ;
                    foreach (var (mesh, mat) in new[] { (model.body, model.bodyMaterial), (model.fins, model.finMaterial) })
                    {
                        if (!mesh) continue;
                        var go = new GameObject("part");
                        go.transform.SetParent(root.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = go.AddComponent<MeshRenderer>();
                        r.sharedMaterial = mat;
                        r.SetPropertyBlock(props);
                    }
                    Save(cam, $"{dir}/{name}-{k}.png", 400, 300);
                    Object.DestroyImmediate(root);
                }
            }
            foreach (var key in new[] { "neon", "barb", "goldfish", "cichlid" })
                if (byKind.TryGetValue(key, out var f)) Follow(f, key, 8, 2);

            app.Eco.Feed(0);
            Aquarium.Fish.Fish eater = null;
            for (int i = 0; i < 30 * 40 && eater == null; i++)
            {
                app.Simulate(1 / 30f);
                eater = app.Eco.fish.FirstOrDefault(f => f.sp.len > 0.4f && f.state == Aquarium.Fish.FishState.EAT && f.faceOpen.x > 0.5f);
            }
            if (eater != null) Follow(eater, "eat", 12, 1);
            Debug.Log("Face strip rendered");
        }

        /// <summary>
        /// How much the fish overlap in a fresh aquarium, with a feeding halfway, without and with the
        /// bodies keeping apart (numbers, no pictures).
        /// </summary>
        public static void OverlapCheck()
        {
            const float Dt = 1 / 30f;
            foreach (bool on in new[] { false, true })
            {
                Aquarium.Fish.FishBodies.On = on;
                Aquarium.Fish.FishBodies.Pushed = 0;
                EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
                var app = Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
                app.Boot(new Aquarium.Life.MemoryWorldStore());
                UnityEngine.Random.InitState(5);
                for (int i = 0; i < 300; i++) app.Simulate(Dt);
                var fish = app.Eco.fish;
                int pairFrames = 0, deepFrames = 0, frames = 30 * 180, crossed = 0, nearN = 0;
                float worst = 0, speedSum = 0, nearSum = 0;
                string worstPair = "";
                var sw = new System.Diagnostics.Stopwatch();
                // a pair close together, and which side of b the body of a was on: a flip is one through the other
                var was = new Dictionary<(int, int), Vector3>();
                var critters = app.Eco.critters;
                var wasC = new Dictionary<(int, int), Vector3>();
                int critterFrames = 0, critterDeep = 0, crossedC = 0;
                float worstC = 0;
                string worstCPair = "";
                for (int k = 0; k < frames; k++)
                {
                    if (k == frames / 2) app.Eco.Feed(0);
                    sw.Start();
                    app.Simulate(Dt);
                    sw.Stop();
                    for (int i = 0; i < fish.Count; i++)
                    {
                        if (fish[i].hidden) continue;
                        for (int j = i + 1; j < fish.Count; j++)
                        {
                            if (fish[j].hidden || (fish[i].pos - fish[j].pos).sqrMagnitude > 4) continue;
                            float depth = -Aquarium.Fish.FishBodies.Gap(fish[i], fish[j], out var side);
                            if (depth > -0.12f)
                            {
                                if (was.TryGetValue((i, j), out var before) && Vector3.Dot(before, side) < -0.5f) crossed++;
                                was[(i, j)] = side;
                            }
                            else was.Remove((i, j));
                            if (depth <= 0) continue;
                            pairFrames++;
                            if (depth > 0.05f) deepFrames++;
                            if (depth > worst) { worst = depth; worstPair = $"{fish[i].sp.id} {fish[i].state} / {fish[j].sp.id} {fish[j].state}"; }
                        }
                    }
                    for (int i = 0; i < fish.Count; i++)
                    {
                        if (fish[i].hidden) continue;
                        for (int c = 0; c < critters.Count; c++)
                        {
                            if ((fish[i].pos - critters[c].pos).sqrMagnitude > 4) continue;
                            float depth = -Aquarium.Fish.FishBodies.Gap(fish[i], critters[c], out var side);
                            if (depth > -0.12f)
                            {
                                if (wasC.TryGetValue((i, c), out var before) && Vector3.Dot(before, side) < -0.5f) crossedC++;
                                wasC[(i, c)] = side;
                            }
                            else wasC.Remove((i, c));
                            if (depth <= 0) continue;
                            critterFrames++;
                            if (depth > 0.05f) critterDeep++;
                            if (depth > worstC) { worstC = depth; worstCPair = $"{fish[i].sp.id} {fish[i].state} / {critters[c].kind} {critters[c].name}"; }
                        }
                    }
                    if (k % 15 != 0) continue;
                    // the swimming goes on, and the schools hold together
                    foreach (var f in fish)
                    {
                        if (f.hidden) continue;
                        speedSum += f.vel.magnitude / f.sp.swim.cruise;
                        if (f.sp.school < 0.8f) continue;
                        float best = float.MaxValue;
                        foreach (var o in fish)
                            if (o != f && !o.hidden && o.sp == f.sp) best = Mathf.Min(best, Vector3.Distance(f.pos, o.pos) / f.sp.len);
                        if (best < float.MaxValue) { nearSum += best; nearN++; }
                    }
                }
                float minutes = frames * Dt / 60;
                int meals = fish.Sum(f => f.mem.meals), shown = fish.Count(f => !f.hidden);
                Debug.Log($"OVERLAP bodies {(on ? "on" : "off")}: {shown} fish, touching {pairFrames * Dt / minutes:0.0} pair-seconds a minute, more than 0.05 deep {deepFrames * Dt / minutes:0.0}, deepest {worst:0.000} ({worstPair}), went through {crossed} times, speed {speedSum / (frames / 15 * shown):0.00} of cruise, nearest schoolmate {nearSum / Mathf.Max(1, nearN):0.00} lengths, meals {meals}, food left {app.Eco.food.Count}, pushed {Aquarium.Fish.FishBodies.Pushed / shown / (frames * Dt):0.0000} units a second per fish, simulation {sw.Elapsed.TotalMilliseconds / frames:0.00} ms a step");
                Debug.Log($"OVERLAP critters bodies {(on ? "on" : "off")}: {critters.Count} critters, touching {critterFrames * Dt / minutes:0.0} pair-seconds a minute, more than 0.05 deep {critterDeep * Dt / minutes:0.0}, deepest {worstC:0.000} ({worstCPair}), went through {crossedC} times");
            }
            Aquarium.Fish.FishBodies.On = true;
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
