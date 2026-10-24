// Builds the aquarium scene from code: tank, sand and water, the Blender-made decor and plants
// placed as in the web version (tank.js, decor.js, plants.js), lights, camera, post-processing
// and the day cycle. Run from the menu (Aquarium > Build Scene) or the command line
// (-executeMethod Aquarium.EditorTools.SceneBuilder.BuildAndSave). The result is an ordinary
// scene that can be inspected and tweaked; running the builder again replaces it.
//
// Positions are written in the web version's coordinates and converted with WebSpace.P.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Aquarium.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using static Aquarium.World.WebSpace;
using Terrain = Aquarium.World.Terrain;
using Random = System.Random;

namespace Aquarium.EditorTools
{
    public static class SceneBuilder
    {
        public const string ScenePath = "Assets/Aquarium/Scenes/Aquarium.unity";
        const string MeshDir = "Assets/Aquarium/Art/Generated/Meshes";
        const string Models = "Assets/Aquarium/Resources/Models";
        const float W = Terrain.W, D = Terrain.D, H = Terrain.H;

        static Random rnd;
        static float R() => (float)rnd.NextDouble();
        static float R(float a, float b) => a + R() * (b - a);
        static float Sand(float x, float z) => Terrain.SandHeight(x, z);

        static Transform root, tank, decor, plants, growing;
        static AquariumLayout layout;
        static DayCycle day;
        static readonly List<GameObject> walkables = new List<GameObject>();

        [MenuItem("Aquarium/Build Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildAndSave();
        }

        public static void BuildAndSave()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (var go in scene.GetRootGameObjects()) UnityEngine.Object.DestroyImmediate(go);
            Build();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Aquarium scene built");
        }

        static void Build()
        {
            rnd = new Random(2026);
            Materials.Reset();
            walkables.Clear();
            Directory.CreateDirectory(MeshDir);
            ConfigurePipeline();

            root = new GameObject("Aquarium").transform;
            tank = Group("Tank");
            decor = Group("Decor");
            plants = Group("Plants");
            var system = new GameObject("System");
            system.transform.SetParent(root, false);
            system.AddComponent<AquariumClock>();
            layout = system.AddComponent<AquariumLayout>();
            day = system.AddComponent<DayCycle>();

            BuildTank();
            BuildLights();
            BuildDecor();
            AddWalkColliders();
            BuildPlants();
            BuildRaysAndDust();
            BuildCameraAndPost();
            BakeEnvironment();
            BuildApp();
            BuildShell(root.GetComponentInChildren<Camera>(), day.volume);
            day.mode = DayCycle.Mode.Day;
            day.Snap();
        }

        static Transform Group(string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static Mesh SaveMesh(Mesh m)
        {
            string path = $"{MeshDir}/{m.name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old)
            {
                old.Clear();
                EditorUtility.CopySerialized(m, old);
                UnityEngine.Object.DestroyImmediate(m);
                return old;
            }
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static GameObject Obj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Quaternion? rot = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = true;
            return go;
        }

        // ------------------------------------------------------------------ pipeline
        static void ConfigurePipeline()
        {
            foreach (var path in new[] { "Assets/Settings/PC_RPAsset.asset", "Assets/Settings/Mobile_RPAsset.asset" })
            {
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (!asset) continue;
                var so = new SerializedObject(asset);
                void Set(string prop, Action<SerializedProperty> f) { var p = so.FindProperty(prop); if (p != null) f(p); }
                bool pc = path.Contains("PC");
                Set("m_ShadowDistance", p => p.floatValue = 25);
                Set("m_ShadowCascadeCount", p => p.intValue = pc ? 2 : 1);
                Set("m_MainLightShadowmapResolution", p => p.intValue = pc ? 2048 : 1024);
                Set("m_SoftShadowsSupported", p => p.boolValue = true);
                Set("m_MSAA", p => p.intValue = pc ? 4 : 2);
                Set("m_SupportsHDR", p => p.boolValue = true);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        // ------------------------------------------------------------------ tank (tank.js)
        static void BuildTank()
        {
            var room = Obj("Room", tank, SaveMesh(MeshGen.Sphere(80, 48, 24, "Room")), day.room = Materials.NewShader("Room", "Aquarium/Room"), Vector3.zero, null, false);
            room.GetComponent<MeshRenderer>().receiveShadows = false;

            var wood = TextureGen.Wood();
            var cabinetMat = Materials.NewLit("Cabinet", Color.white, 0.45f, 0, wood, fog: false, caustics: 0);
            Obj("Cabinet", tank, SaveMesh(MeshGen.RoundedBox(new Vector3(W + 1.2f, 3.2f, D + 1.2f), 0.12f, 4, "Cabinet")), cabinetMat, P(0, -1.75f, 0));
            var trimMat = Materials.NewLit("Trim", Hex("#111318"), 0.3f, 0.8f, fog: false, caustics: 0);
            Obj("Trim", tank, SaveMesh(MeshGen.RoundedBox(new Vector3(W + 0.5f, 0.18f, D + 0.5f), 0.06f, 3, "Trim")), trimMat, P(0, -0.08f, 0));
            Obj("Floor", tank, SaveMesh(MeshGen.Disc(40, 64, "Floor")), Materials.NewLit("Floor", Hex("#0d1016"), 0.9f, fog: false, caustics: 0), P(0, -3.35f, 0), null, false);

            // glass: flat-edged panels (rounded edges catch a razor-sharp sun glint that blooms into a blob)
            var glass = Materials.NewGlass("Glass", new Color(0.91f, 0.98f, 1, 0.1f));
            const float t = 0.14f;
            var panels = new Transform[4];
            int k = 0;
            foreach (var (s, p) in new[] { (new Vector3(W, H, t), P(0, H / 2, D / 2)), (new Vector3(W, H, t), P(0, H / 2, -D / 2)), (new Vector3(t, H, D), P(W / 2, H / 2, 0)), (new Vector3(t, H, D), P(-W / 2, H / 2, 0)) })
                panels[k] = Obj($"Glass{k++}", tank, SaveMesh(MeshGen.Box(s, $"GlassPanel{k}")), glass, p, null, false).transform;
            var edgeMat = Materials.NewGlass("GlassEdge", new Color(0.5f, 0.85f, 0.77f, 0.35f), 0.3f, 0);
            var sealMat = Materials.NewLit("Seal", Hex("#0b0d10"), 0.6f, fog: false, caustics: 0);
            var seal = SaveMesh(MeshGen.Cylinder(0.07f, 0.07f, H, 12, "Seal"));
            var edge = SaveMesh(MeshGen.Box(new Vector3(0.03f, H, 0.03f), "GlassEdgeBar"));
            foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
            {
                Obj("Seal", tank, seal, sealMat, P(sx * (W / 2 - 0.06f), H / 2, sz * (D / 2 - 0.06f)));
                Obj("Edge", tank, edge, edgeMat, P(sx * (W / 2 + 0.07f), H / 2, sz * (D / 2 + 0.07f)), null, false);
            }
            Obj("BaseFrame", tank, SaveMesh(MeshGen.RoundedBox(new Vector3(W + 0.3f, 0.22f, D + 0.3f), 0.08f, 3, "BaseFrame")), trimMat, P(0, 0.03f, 0));

            // lid with the LED bar and the blue moon LEDs
            var lidMat = Materials.NewLit("Lid", Hex("#15181e"), 0.55f, 0.3f, fog: false, caustics: 0);
            Obj("LidRim", tank, SaveMesh(MeshGen.RoundedBox(new Vector3(W + 0.32f, 0.16f, D + 0.32f), 0.06f, 3, "LidRim")), lidMat, P(0, H + 0.02f, 0));
            Obj("Cover", tank, SaveMesh(MeshGen.Box(new Vector3(W - 0.1f, 0.03f, D - 0.1f), "Cover")), Materials.NewLit("Cover", Hex("#1d2a33"), 0.6f, emission: Hex("#9fe6ff") * 0.06f, fog: false, caustics: 0), P(0, H + 0.06f, 0));
            Obj("LedBody", tank, SaveMesh(MeshGen.RoundedBox(new Vector3(W - 0.6f, 0.18f, 1.1f), 0.08f, 3, "LedBody")), lidMat, P(0, H + 0.55f, 0));
            day.led = Materials.NewLit("Led", Color.white, 0.5f, emission: Hex("#e8fbff") * 3, fog: false, caustics: 0);
            Obj("LedStrip", tank, SaveMesh(MeshGen.Box(new Vector3(W - 0.9f, 0.03f, 0.75f), "LedStrip")), day.led, P(0, H + 0.45f, 0), null, false);
            var leg = SaveMesh(MeshGen.Box(new Vector3(0.08f, 0.5f, 0.9f), "LidLeg"));
            foreach (var sx in new[] { -1, 1 }) Obj("LidLeg", tank, leg, lidMat, P(sx * (W / 2 - 0.4f), H + 0.3f, 0));
            day.moonLeds = Materials.NewLit("MoonLed", Color.black, 0.5f, emission: Hex("#5aa7ff"), fog: false, caustics: 0);
            var moonLed = SaveMesh(MeshGen.Sphere(0.035f, 8, 6, "MoonLed"));
            for (int i = 0; i < 12; i++) Obj("MoonLed", tank, moonLed, day.moonLeds, P(-W / 2 + 0.8f + i * ((W - 1.6f) / 11), H + 0.44f, 0.42f), null, false);

            // filter in the back right corner
            var filterMat = Materials.NewLit("Filter", Hex("#2b3540"), 0.35f, 0.2f, caustics: 0.5f);
            var slotMat = Materials.NewLit("FilterSlot", Hex("#0d1116"), 0.8f, caustics: 0);
            var filter = new GameObject("Filter").transform;
            filter.SetParent(tank, false);
            filter.localPosition = P(5.35f, 0, -1.95f);
            Obj("Body", filter, SaveMesh(MeshGen.RoundedBox(new Vector3(0.55f, 4.6f, 0.4f), 0.12f, 3, "FilterBody")), filterMat, P(0, 2.9f, 0));
            var slot = SaveMesh(MeshGen.Box(new Vector3(0.4f, 0.04f, 0.02f), "FilterSlot"));
            for (int i = 0; i < 9; i++) Obj("Slot", filter, slot, slotMat, P(0, 1.2f + i * 0.22f, 0.21f), null, false);
            Obj("Nozzle", filter, SaveMesh(MeshGen.Cylinder(0.09f, 0.07f, 0.4f, 16, "FilterNozzle")), filterMat, P(0, 5.05f, 0.35f), Rot(Mathf.PI / 2, 0, 0));
            layout.filterOutlet = P(5.35f, 5.05f, -1.45f);
            layout.filterIntake = P(5.35f, 1.6f, -1.6f);

            // deep painted background inside the back glass (turned to face the room)
            day.backdrop = Materials.NewShader("Backdrop", "Aquarium/Backdrop");
            Obj("Backdrop", tank, SaveMesh(MeshGen.Quad(W - 0.1f, H - 0.2f, "Backdrop")), day.backdrop, P(0, H / 2, -D / 2 - 0.12f), Quaternion.Euler(0, 180, 0), false);

            // sand: rises to the back, mounds under the decor, small ripples
            var sandMat = Materials.NewLit("Sand", Color.white, 0.92f, 0, TextureGen.Sand(), caustics: 1.3f, normal: TextureGen.NoiseNormal("SandNormal", 256, 28), normalScale: 0.4f);
            sandMat.SetTextureScale("_BaseMap", Vector2.one);
            var sandMesh = SaveMesh(MeshGen.Grid(W - 0.15f, D - 0.15f, 240, 100, (x, z) => Terrain.SandSurface(-x, z), 3, 1.4f, "Sand"));
            Obj("Sand", tank, sandMesh, sandMat, Vector3.zero, null, false);
            var sandSide = Materials.NewLit("SandSide", Hex("#b89a6a"), 1);
            sandSide.SetFloat("_Cull", 0);
            Obj("SandFront", tank, SaveMesh(MeshGen.Strip(W - 0.15f, 120, x => Sand(-x, D / 2 - 0.08f), 0.1f, "SandFront")), sandSide, P(0, 0, D / 2 - 0.08f), null, false);

            // water surface (waves in the shader) and the bright meniscus line on the front glass
            var water = Materials.NewShader("Water", "Aquarium/Water");
            Obj("WaterSurface", tank, SaveMesh(MeshGen.Grid(W - 0.15f, D - 0.15f, 96, 40, (x, z) => 0, 1, 1, "WaterSurface")), water, P(0, Terrain.WaterY, 0), null, false);
            Obj("Meniscus", tank, SaveMesh(MeshGen.Box(new Vector3(W - 0.14f, 0.025f, 0.01f), "Meniscus")), Materials.NewLit("Meniscus", Color.black, 1, emission: Hex("#cff8ff") * 0.35f, fog: false, caustics: 0), P(0, Terrain.WaterY, D / 2 - 0.08f), null, false);
        }

        // ------------------------------------------------------------------ lights
        static void BuildLights()
        {
            var lights = Group("Lights");
            Light Make(string name, LightType type, Vector3 webPos, Vector3 webTarget)
            {
                var go = new GameObject(name);
                go.transform.SetParent(lights, false);
                var p = P(webPos);
                go.transform.position = p;
                go.transform.rotation = Quaternion.LookRotation(P(webTarget) - p);
                var l = go.AddComponent<Light>();
                l.type = type;
                return l;
            }
            day.sun = Make("Sun", LightType.Directional, new Vector3(2.5f, 14, 3.5f), Vector3.zero);
            day.sun.shadows = LightShadows.Soft;
            day.sun.shadowStrength = 1;
            day.sun.shadowBias = 0.05f;
            day.sun.shadowNormalBias = 0.3f;
            day.fill = Make("Fill", LightType.Directional, new Vector3(-14, 4, 2), Vector3.zero);
            day.fill.shadows = LightShadows.None;
            day.moon = Make("Moon", LightType.Spot, new Vector3(-2, 11, 2), new Vector3(0, 0.5f, 0));
            day.moon.color = Hex("#5a7dff");
            day.moon.range = 16;
            day.moon.spotAngle = 2 * 0.6f * Mathf.Rad2Deg;
            day.moon.innerSpotAngle = day.moon.spotAngle * 0.2f;
            day.moon.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ models
        static GameObject Model(string file) => AssetDatabase.LoadAssetAtPath<GameObject>($"{Models}/{file}.glb");

        static readonly HashSet<string> PlantModels = new HashSet<string> { "vallisneria", "sword", "crypt", "anubias", "limnophila", "cabomba", "ludwigia", "pistia", "moss" };
        static string FileOf(string model) => PlantModels.Contains(model) ? $"Plants/{model}" : $"Decor/{model}";

        static Vector3 RandomDir()
        {
            float z = R(-1, 1), a = R(0, Mathf.PI * 2), r = Mathf.Sqrt(1 - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }

        /// <summary>A copy of a whole model with aquarium materials; Glow materials go to the day cycle.</summary>
        static GameObject Place(string model, Transform parent, Vector3 webPos, Vector3 webRot = default, float scale = 1, float glowDay = 0.15f, float glowNight = 3)
        {
            var src = Model(FileOf(model));
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.transform.localPosition = P(webPos);
            go.transform.localRotation = Rot(webRot.x, webRot.y, webRot.z);
            go.transform.localScale = Vector3.one * scale;
            Convert(go, model, glowDay, glowNight);
            return go;
        }

        /// <summary>A copy of one named node of a model (one rock variant, one plant...), transform reset.</summary>
        static GameObject Node(string model, string node, Transform parent, float glowDay = 0.15f, float glowNight = 3)
        {
            var all = Model(FileOf(model)).GetComponentsInChildren<Transform>();
            var src = all.FirstOrDefault(t => t.name == node) ?? all.First(t => t.TryGetComponent<MeshFilter>(out var f) && f.sharedMesh && f.sharedMesh.name == node);
            var go = UnityEngine.Object.Instantiate(src.gameObject, parent);
            go.name = node;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Convert(go, model, glowDay, glowNight, src);
            return go;
        }

        static void Convert(GameObject go, string model, float glowDay, float glowNight, Transform roleRoot = null)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                // the role sits on the nearest named ancestor (as in the web version)
                string role = Materials.RoleOf(r.transform, go.transform);
                if (role == "Static" && roleRoot) role = Materials.RoleOf(roleRoot, roleRoot.root);
                var mats = r.sharedMaterials.Select(m => Materials.FromGltf(m, model, role)).ToArray();
                r.sharedMaterials = mats;
                r.shadowCastingMode = role == "Glass" ? ShadowCastingMode.Off : ShadowCastingMode.On;
                if (role == "Glow") foreach (var m in mats) AddGlow(m, Materials.GltfEmission(m), glowDay, glowNight);
            }
        }

        static void AddGlow(Material m, Color baseEmission, float dayK, float nightK, bool flicker = true)
        {
            var g = day.glow.Find(x => x.material == m);
            if (g == null) day.glow.Add(g = new GlowMaterial { material = m });
            g.baseEmission = baseEmission;
            g.day = dayK;
            g.night = nightK;
            g.flicker = flicker;
        }

        static void Spot(string name, float x, float y, float z, string kind, float radius = 0.6f) =>
            layout.spots.Add(new Spot { name = name, position = P(x, y, z), kind = kind, radius = radius });
        static void SpotAt(Transform t, string name, Vector3 webLocal, string kind, float radius) =>
            layout.spots.Add(new Spot { name = name, position = t.TransformPoint(P(webLocal)), kind = kind, radius = radius });
        static void Col(float x, float y, float z, float r) => layout.obstacles.Add(new Obstacle { center = P(x, y, z), radius = r });
        static void ColAt(Transform t, Vector3 webLocal, float r) => layout.obstacles.Add(new Obstacle { center = t.TransformPoint(P(webLocal)), radius = r });

        // ------------------------------------------------------------------ decor (decor.js)
        static void BuildDecor()
        {
            // castle on a rocky mound (left)
            {
                float cx = -3.9f, cz = -1.05f;
                var castle = Place("castle", decor, new Vector3(cx, Sand(cx, cz) - 0.06f, cz), new Vector3(0, 0.18f, 0)).transform;
                walkables.Add(castle.gameObject);
                ColAt(castle, new Vector3(0, 0.15f, 0), 1.2f);
                ColAt(castle, new Vector3(-0.62f, 1.2f, 0.08f), 0.38f);
                ColAt(castle, new Vector3(0.62f, 1.1f, 0.08f), 0.38f);
                ColAt(castle, new Vector3(0, 1.6f, -0.34f), 0.38f);
                ColAt(castle, new Vector3(0, 0.8f, 0.06f), 0.42f);
                SpotAt(castle, "замок", new Vector3(0, 0.95f, 1.05f), "castle", 0.5f);
                SpotAt(castle, "ворота замка", new Vector3(0, 0.5f, 0.55f), "hide", 0.25f);
                SpotAt(castle, "башня", new Vector3(0, 3.9f, -0.34f), "landmark", 0.7f);
            }
            // driftwood centrepiece
            {
                float bx = 0.4f, bz = -1.45f, by = Sand(bx, bz);
                var wood = Place("driftwood", decor, new Vector3(bx, by, bz));
                walkables.Add(wood);
                var mesh = wood.GetComponentsInChildren<MeshFilter>().First(f => Materials.RoleOf(f.transform, wood.transform) == "Static");
                var verts = mesh.sharedMesh.vertices;
                for (int i = 0; i < verts.Length; i += Math.Max(1, verts.Length / 40))
                    layout.obstacles.Add(new Obstacle { center = mesh.transform.TransformPoint(verts[i]), radius = 0.22f });
                Spot("коряга", bx, by + 1.6f, bz + 0.5f, "landmark", 0.8f);
                Spot("под корягой", bx - 0.4f, by + 0.35f, bz + 0.6f, "hide", 0.35f);
                layout.woodCenter = P(bx, by + 1.5f, bz);
            }
            // shipwreck, treasure chest and the bottle (right)
            {
                float sx = 3.6f, sz = -0.75f, sy = Sand(sx, sz);
                walkables.Add(Place("ship", decor, new Vector3(sx, sy + 0.45f, sz), new Vector3(0.05f, -0.35f, -0.22f)));
                for (int i = -2; i <= 2; i++) Col(sx + i * 0.75f, sy + 0.4f, sz + i * 0.22f, 0.6f);
                Col(sx - 0.5f, sy + 1.6f, sz - 0.1f, 0.35f);
                Spot("затонувший корабль", sx, sy + 1.0f, sz + 0.9f, "ship", 0.8f);
                Spot("трюм корабля", sx + 0.3f, sy + 0.55f, sz + 0.25f, "hide", 0.3f);

                float cx = 2.2f, cz = 0.75f;
                var chest = Place("chest", decor, new Vector3(cx, Sand(cx, cz) - 0.06f, cz), new Vector3(0.04f, -0.5f, 0.08f), 1, 0.35f, 0.9f);
                walkables.Add(chest);
                layout.chestLid = chest.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "Lid");
                if (layout.chestLid) layout.chestLid.localRotation = Rot(-0.12f, 0, 0);
                Col(cx, Sand(cx, cz) + 0.2f, cz, 0.4f);
                Spot("сундук", cx, Sand(cx, cz) + 0.6f, cz + 0.3f, "landmark", 0.4f);
                layout.chestPosition = P(cx, Sand(cx, cz) + 0.35f, cz);

                Place("bottle", decor, new Vector3(-1.55f, Sand(-1.55f, 1.45f) + 0.05f, 1.45f), new Vector3(Mathf.PI / 2 - 0.1f, 0, 0.7f));
                layout.bottlePosition = P(-1.55f, Sand(-1.55f, 1.45f) + 0.1f, 1.45f);
                Spot("бутылка", -1.55f, Sand(-1.55f, 1.45f) + 0.4f, 1.45f, "landmark", 0.3f);
            }
            // stone arch and the cave behind it, with its glowing crystals
            {
                float ax = -1.3f, az = 0.05f, ay = Sand(ax, az);
                walkables.Add(Place("arch", decor, new Vector3(ax, ay - 0.05f, az)));
                for (int i = 0; i <= 12; i += 3)
                {
                    float a = i / 12f * Mathf.PI;
                    Col(ax + Mathf.Cos(a) * 0.85f, ay + Mathf.Sin(a) * 1.0f, az, 0.3f);
                }
                Spot("каменная арка", ax, ay + 0.55f, az, "arch", 0.35f);
                float cx = -2.0f, cz = -0.9f;
                var cave = Place("cave", decor, new Vector3(cx, Sand(cx, cz) - 0.05f, cz), default, 1, 0.5f, 4);
                walkables.Add(cave);
                Col(cx, Sand(cx, cz) + 0.5f, cz - 0.3f, 0.75f);
                Spot("пещера", cx + 0.05f, Sand(cx, cz) + 0.28f, cz + 0.6f, "cave", 0.35f);
                var crystals = cave.GetComponentsInChildren<MeshRenderer>().FirstOrDefault(r => Materials.RoleOf(r.transform, cave.transform) == "Glow");
                var crystalPos = crystals ? crystals.bounds.center : P(cx, Sand(cx, cz) + 0.3f, cz);
                layout.secretCave = crystalPos;
                var cl = new GameObject("CrystalLight").AddComponent<Light>();
                cl.transform.SetParent(decor, false);
                cl.transform.position = crystalPos + P(0, 0.25f, 0.2f);
                cl.type = LightType.Point; cl.color = Hex("#5fe6ff"); cl.range = 1.6f; cl.shadows = LightShadows.None;
                day.crystal = cl;
            }
            // little mushroom house (front right)
            {
                float hx = 4.6f, hz = 1.25f;
                walkables.Add(Place("mushroom", decor, new Vector3(hx, Sand(hx, hz) - 0.02f, hz), new Vector3(0, -0.5f, 0), 1, 0.1f, 3));
                Col(hx, Sand(hx, hz) + 0.4f, hz, 0.5f);
                Spot("грибной домик", hx - 0.2f, Sand(hx, hz) + 0.3f, hz + 0.45f, "hide", 0.3f);
            }
            // old lantern (front left)
            {
                float lx = -4.9f, lz = 1.4f, ly = Sand(lx, lz);
                Place("lantern", decor, new Vector3(lx, ly - 0.02f, lz), new Vector3(0.15f, 0.4f, -0.25f), 1.3f, 0.4f, 5);
                var pl = new GameObject("LanternLight").AddComponent<Light>();
                pl.transform.SetParent(decor, false);
                pl.transform.localPosition = P(lx, ly + 0.35f, lz);
                pl.type = LightType.Point; pl.color = Hex("#ffb35a"); pl.range = 2.8f; pl.intensity = 0; pl.shadows = LightShadows.None;
                day.lantern = pl;
                Spot("старый фонарь", lx, ly + 0.5f, lz, "landmark", 0.3f);
            }
            // diver statue, bubbles from the helmet
            {
                float dx = 1.5f, dz = -0.35f, dy = Sand(dx, dz);
                var diver = Place("diver", decor, new Vector3(dx, dy + 0.06f, dz), new Vector3(0, -0.4f, 0), 0.95f).transform;
                walkables.Add(diver.gameObject);
                Col(dx, dy + 0.6f, dz, 0.4f);
                Spot("водолаз", dx, dy + 1.1f, dz + 0.5f, "landmark", 0.4f);
                layout.diverHelmet = diver.TransformPoint(P(0.25f, 1.05f, -0.1f));
            }
            // overgrown ruins (back right)
            {
                float rx = 4.55f, rz = -1.95f;
                var ruins = Place("ruins", decor, new Vector3(rx, Sand(rx, rz) - 0.04f, rz), new Vector3(0, 0.12f, 0), 0.75f).transform;
                walkables.Add(ruins.gameObject);
                foreach (var (x, y, z, r) in new[] { (-0.62f, 0.9f, 0f, 0.26f), (0.62f, 0.9f, 0f, 0.26f), (0f, 2.3f, 0f, 0.35f), (1.35f, 0.5f, 0.25f, 0.24f) })
                    ColAt(ruins, new Vector3(x, y, z), r);
                SpotAt(ruins, "руины", new Vector3(0, 1.0f, 0.3f), "landmark", 0.6f);
            }
            // rubber duck (a rare guest on the surface, hidden until then)
            {
                var holder = new GameObject("Duck").transform;
                holder.SetParent(decor, false);
                holder.localPosition = P(-7, 5.65f, 0);
                var duck = Place("duck", holder, Vector3.zero, new Vector3(0, Mathf.PI / 2, 0));
                holder.gameObject.SetActive(false);
                layout.duck = holder;
            }
            // scattered rocks
            var scatter = new[] { (-0.4f, 1.3f, 0.38f), (0.7f, 1.55f, 0.25f), (5.6f, 0.75f, 0.45f), (-5.3f, 0.4f, 0.5f), (-0.9f, -2.0f, 0.55f), (1.6f, 1.2f, 0.18f), (-2.4f, 1.7f, 0.2f), (-5.4f, -0.8f, 0.45f) };
            for (int i = 0; i < scatter.Length; i++)
            {
                var (x, z, r) = scatter[i];
                float squash = 0.6f + R() * 0.3f, sink = 0.25f;
                var m = Node("rocks", $"Rock{(1000 + i) % 6}", decor).transform;
                m.localScale = new Vector3(r, r * (0.6f + squash * 0.55f), r);
                m.localPosition = P(x, Sand(x, z) - r * sink, z);
                m.localRotation = Yaw(R(0, Mathf.PI * 2));
                walkables.Add(m.gameObject);
                Col(x, Sand(x, z) - r * sink + r * 0.2f, z, r * 0.95f);
            }
            foreach (var (x, z, k, rot, sc) in new[] { (-3.0f, 1.15f, 0, 0.6f, 1.0f), (3.0f, 1.6f, 1, -0.4f, 1.1f) })
            {
                var pile = Node("rockpile", $"Pile{k}", decor).transform;
                pile.localPosition = P(x, Sand(x, z) - 0.05f, z);
                pile.localRotation = Yaw(rot);
                pile.localScale = Vector3.one * sc;
                walkables.Add(pile.gameObject);
                Col(x, Sand(x, z) + 0.3f * sc, z, 0.6f * sc);
            }
            foreach (var (node, x, z, rot) in new[] { ("Cannon", 3.4f, 0.3f, 2.3f), ("Barrel0", 4.35f, 0.55f, 0.3f), ("Barrel1", 3.85f, 0.95f, 1.1f) })
            {
                var o = Node("wreckage", node, decor).transform;
                o.localPosition = P(x, Sand(x, z) - 0.02f, z);
                o.localRotation = Yaw(rot);
                walkables.Add(o.gameObject);
                Col(x, Sand(x, z) + 0.2f, z, 0.3f);
            }
            Spot("пушка", 3.4f, Sand(3.4f, 0.3f) + 0.5f, 0.6f, "landmark", 0.35f);

            // a field of pebbles, merged into one mesh with a colour per pebble
            {
                var src = Model("Decor/pebbles").GetComponentsInChildren<MeshFilter>().First(f => f.name == "Pebble0");
                var mats = new List<Matrix4x4>();
                var cols = new List<Color>();
                for (int i = 0; i < 420; i++)
                {
                    float x = R(-5.8f, 5.8f), z = R(-2.3f, 2.3f), k = R(0.025f, 0.08f);
                    mats.Add(Matrix4x4.TRS(P(x, Sand(x, z) + 0.005f, z), Rot(R(-0.3f, 0.3f), R(0, 6.28f), R(-0.3f, 0.3f)), new Vector3(k, k * R(0.7f, 1.2f), k * R(0.8f, 1.2f))));
                    cols.Add(TextureHsl(R(0.02f, 0.12f), R(0.05f, 0.3f), R(0.55f, 1.0f)));
                }
                var mat = new Material(Materials.FromGltf(src.GetComponent<MeshRenderer>().sharedMaterial, "pebbles", "Static"));
                mat.SetFloat("_VertexColor", 1);
                Obj("Pebbles", decor, SaveMesh(Merge(src.sharedMesh, mats, cols, false, "Pebbles")), Materials.Save("pebbles_field", mat), Vector3.zero);
            }
            // seashells
            for (int i = 0; i < 9; i++)
            {
                var m = Node("shells", $"Shell{i % 2}", decor).transform;
                float x = R(-5.5f, 5.5f), z = R(0.2f, 2.2f);
                m.localPosition = P(x, Sand(x, z) - 0.005f, z);
                m.localRotation = Rot(R(-0.2f, 0.2f), R(0, 6), R(-0.2f, 0.2f));
                m.localScale = Vector3.one * R(0.8f, 1.3f);
            }
            // small aerator stone in the back left corner (bubbles stream from it)
            {
                float x = -2.65f, z = -2.05f;
                layout.aerator = P(x, Sand(x, z) + 0.05f, z);
                Obj("AeratorStone", decor, SaveMesh(MeshGen.Cylinder(0.12f, 0.1f, 0.08f, 16, "AeratorStone")), Materials.NewLit("AeratorStone", Hex("#4a4f57"), 0.9f), P(x, Sand(x, z) + 0.02f, z));
            }
            // open-water places (for explorers)
            Spot("у поверхности", 0, 5.0f, 0.2f, "open", 1.5f);
            Spot("у фильтра", 4.9f, 4.6f, -1.3f, "filter", 0.6f);
            Spot("середина аквариума", -0.5f, 3.0f, 0.6f, "open", 1.5f);
            Spot("у переднего стекла", 1.0f, 2.5f, 1.9f, "glass", 1.2f);
        }

        /// <summary>Walkable decor gets mesh colliders: critters climb it and moss settles on its tops.</summary>
        static void AddWalkColliders()
        {
            foreach (var go in walkables)
            foreach (var f in go.GetComponentsInChildren<MeshFilter>())
            {
                if (Materials.RoleOf(f.transform, go.transform) == "Sway") continue;
                f.gameObject.AddComponent<MeshCollider>().sharedMesh = f.sharedMesh;
            }
            Physics.SyncTransforms();
        }

        /// <summary>Instances of a mesh merged into one, with a colour per instance; for swaying ones uv3.x
        /// keeps the height above each instance's root.</summary>
        static Mesh Merge(Mesh src, List<Matrix4x4> mats, List<Color> colors, bool swayHeight, string name)
        {
            var sv = src.vertices; var sn = src.normals; var su = src.uv; var st = src.triangles;
            var sc = src.colors;
            int n = sv.Length;
            var v = new Vector3[n * mats.Count]; var nn = new Vector3[v.Length]; var uv = new Vector2[v.Length]; var uv3 = new Vector2[v.Length]; var c = new Color[v.Length];
            var t = new int[st.Length * mats.Count];
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                for (int j = 0; j < n; j++)
                {
                    v[i * n + j] = m.MultiplyPoint3x4(sv[j]);
                    nn[i * n + j] = m.MultiplyVector(sn.Length > 0 ? sn[j] : Vector3.up).normalized;
                    uv[i * n + j] = su.Length > 0 ? su[j] : Vector2.zero;
                    uv3[i * n + j] = new Vector2(sv[j].y, 0);
                    c[i * n + j] = (sc.Length > 0 ? sc[j] : Color.white) * colors[i];
                }
                for (int j = 0; j < st.Length; j++) t[i * st.Length + j] = st[j] + i * n;
            }
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = v; mesh.normals = nn; mesh.uv = uv; mesh.colors = c;
            if (swayHeight) mesh.SetUVs(3, uv3);
            mesh.triangles = t;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ------------------------------------------------------------------ plants (plants.js)
        static readonly Dictionary<string, (string node, int variants)> PlantNode = new Dictionary<string, (string, int)>
        {
            ["vallisneria"] = ("SwayVallis", 3), ["sword"] = ("SwaySword", 2), ["crypt"] = ("SwayCrypt", 2), ["limnophila"] = ("SwayLimno", 2),
            ["cabomba"] = ("SwayCabomba", 2), ["ludwigia"] = ("SwayLudwigia", 2), ["anubias"] = ("SwayAnubias", 2),
        };

        /// <summary>
        /// A preview copy of a plant for the editor: at run time the plant system replaces these
        /// with the saved world's plants (which grow and spread) made from the prefabs below.
        /// </summary>
        static GameObject Plant(string kind, float x, float z, float scale = 1, float? y = null)
        {
            // planted some days before the aquarium opened: grown to 73..92% of their size
            float gene = R(0.85f, 1.15f), age = R(3, 9), growth = 0.5f + 0.5f * (1 - Mathf.Exp(-age / 5));
            int variant = (int)R(0, 9);
            var (node, variants) = PlantNode[kind];
            var go = Node(kind, $"{node}{variant % variants}", growing);
            go.transform.localPosition = P(x, y ?? Sand(x, z) - 0.03f, z);
            go.transform.localRotation = Yaw(R(0, Mathf.PI * 2));
            go.transform.localScale = Vector3.one * scale * gene * growth;
            return go;
        }

        static void Clump(string name, float x, float z, float radius) =>
            layout.clumps.Add(new Spot { name = name, position = P(x, Sand(x, z) + 0.6f, z), kind = "plants", radius = radius });

        const string PlantDir = "Assets/Aquarium/Art/Generated/Plants";
        static Aquarium.Fish.PlantModelSet plantSet;

        /// <summary>Prefabs of every variant of the plants that grow (the plant system places them).</summary>
        static Aquarium.Fish.PlantModelSet BuildPlantModels()
        {
            Directory.CreateDirectory(PlantDir);
            string setPath = $"{PlantDir}/PlantModels.asset";
            var set = AssetDatabase.LoadAssetAtPath<Aquarium.Fish.PlantModelSet>(setPath);
            if (!set) { set = ScriptableObject.CreateInstance<Aquarium.Fish.PlantModelSet>(); AssetDatabase.CreateAsset(set, setPath); }
            set.plants.Clear();
            foreach (var kv in PlantNode)
                for (int v = 0; v < kv.Value.variants; v++)
                {
                    string node = $"{kv.Value.node}{v}";
                    var go = Node(kv.Key, node, null);
                    var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PlantDir}/{node}.prefab");
                    UnityEngine.Object.DestroyImmediate(go);
                    set.plants.Add(new Aquarium.Fish.PlantModelSet.Entry { node = node, prefab = prefab });
                }
            EditorUtility.SetDirty(set);
            return set;
        }

        static void BuildPlants()
        {
            growing = new GameObject("Growing").transform;
            growing.SetParent(plants, false);
            plantSet = BuildPlantModels();
            foreach (var (x, z) in new[] { (-5.3f, -2.0f), (-5.6f, -0.6f), (-2.6f, -2.05f), (-1.6f, -1.9f), (1.2f, -2.15f), (5.55f, -2.15f), (-0.4f, -2.1f), (3.5f, -2.15f) }) { Plant("vallisneria", x, z); Clump("заросли валлиснерии", x, z + 0.3f, 0.6f); }
            foreach (var (x, z) in new[] { (-2.95f, 0.4f), (-4.4f, 0.45f), (-0.5f, 0.95f), (5.0f, 0.35f) }) { Plant("sword", x, z, 0.95f); Clump("куст эхинодоруса", x, z, 0.7f); }
            foreach (var (x, z) in new[] { (-3.5f, 1.0f), (2.6f, 1.15f), (-1.9f, 1.35f), (4.4f, 1.6f) }) { Plant("crypt", x, z, 1.1f); Clump("криптокорина", x, z, 0.4f); }
            foreach (var (x, z, kind) in new[] { (-5.55f, -1.75f, "ludwigia"), (-1.25f, -2.15f, "limnophila"), (0.9f, -1.0f, "ludwigia"), (-5.4f, 0.9f, "limnophila") }) { Plant(kind, x, z); Clump(kind == "ludwigia" ? "красные стебли" : "лимнофила", x, z, 0.5f); }
            foreach (var (x, z) in new[] { (-3.3f, -2.25f), (2.7f, -2.2f), (-1.1f, -1.4f) }) { Plant("cabomba", x, z); Clump("кабомба", x, z + 0.3f, 0.5f); }
            // anubias tied to driftwood and stones
            foreach (var (x, z) in new[] { (-0.6f, -1.3f), (1.4f, -1.45f), (-3.0f, 1.15f), (3.0f, 1.6f) })
            {
                var u = P(x, 0, z);
                Plant("anubias", x, z, 1.3f, layout.SurfaceAt(u.x, u.z) - 0.04f);
            }
            // water lettuce floating on the surface
            foreach (var (x, z) in new[] { (-3.2f, -0.6f), (-1.0f, 0.9f), (1.9f, -1.2f), (3.6f, 0.4f), (-4.6f, 1.0f) })
            {
                int i = (int)R(0, 3);
                var f = new GameObject("Pistia").transform;
                f.SetParent(plants, false);
                f.localPosition = P(x, Terrain.WaterY - 0.03f, z);
                f.localRotation = Yaw(R(0, Mathf.PI * 2));
                var drift = f.gameObject.AddComponent<Floater>();
                drift.x = x; drift.z = z; drift.phase = R(0, Mathf.PI * 2);
                f.localScale = Vector3.one * R(0.9f, 1.3f);
                Node("pistia", $"SwayPistia{i}", f);
                Node("pistia", $"RootsPistia{i}", f);
            }
            BuildGrass();
            BuildMoss();
            BuildCorals();
        }

        static void BuildGrass()
        {
            // one tapered, slightly curved blade, coloured darker at the root
            var blade = new Mesh { name = "GrassBlade" };
            var bv = new List<Vector3>(); var bc = new List<Color>();
            for (int j = 0; j <= 4; j++)
            {
                float y = 0.22f * j / 4, tt = y / 0.22f;
                foreach (var x in new[] { -0.0125f, 0.0125f })
                {
                    bv.Add(new Vector3(x * (1 - tt), y, tt * tt * 0.05f));
                    bc.Add(TextureHsl(0.28f, 0.7f, 0.18f + y * 1.4f));
                }
            }
            var bt = new List<int>();
            for (int j = 0; j < 4; j++) { int a = j * 2; bt.AddRange(new[] { a, a + 2, a + 1, a + 1, a + 2, a + 3 }); }
            blade.SetVertices(bv); blade.SetColors(bc); blade.SetTriangles(bt, 0); blade.SetUVs(0, bv.Select(p => new Vector2(0.5f, p.y / 0.22f)).ToList());
            blade.RecalculateNormals();

            var patches = new[] { (-1.0f, 1.6f, 1.4f, 0.5f), (3.4f, 1.7f, 1.2f, 0.45f), (-4.0f, 1.5f, 1.1f, 0.5f), (0.6f, 0.4f, 0.9f, 0.5f), (-2.8f, 0.9f, 0.6f, 0.4f) };
            var mats = new List<Matrix4x4>();
            var cols = new List<Color>();
            for (int k = 0; k < 9000; k++)
            {
                var (px, pz, rx, rz) = patches[k % patches.Length];
                float a = R() * Mathf.PI * 2, r = Mathf.Sqrt(R());
                float x = px + Mathf.Cos(a) * r * rx, z = pz + Mathf.Sin(a) * r * rz;
                if (Mathf.Abs(x) > 5.8f || Mathf.Abs(z) > 2.3f) continue;
                float sc = R(0.6f, 1.4f) * (1 - r * 0.4f);
                mats.Add(Matrix4x4.TRS(P(x, Sand(x, z) - 0.01f, z), Rot(R(-0.25f, 0.25f), R(0, 6.28f), R(-0.25f, 0.25f)), Vector3.one * sc));
                cols.Add(Color.white);
            }
            var mat = Materials.NewLit("Grass", Color.white, 0.6f, caustics: 0);
            mat.SetFloat("_VertexColor", 1);
            mat.SetFloat("_Sway", 1.6f);
            mat.SetFloat("_SwayFromUV3", 1);
            mat.SetFloat("_Cull", 0);
            var go = Obj("Grass", plants, SaveMesh(Merge(blade, mats, cols, true, "Grass")), mat, Vector3.zero);
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static Color TextureHsl(float h, float s, float l)
        {
            float q = l < 0.5f ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
            float Ch(float t) { t = (t % 1 + 1) % 1; return t < 1 / 6f ? p + (q - p) * 6 * t : t < 0.5f ? q : t < 2 / 3f ? p + (q - p) * (2 / 3f - t) * 6 : p; }
            return new Color(Ch(h + 1 / 3f), Ch(h), Ch(h - 1 / 3f)).linear;
        }

        static void BuildMoss()
        {
            var src = Model("Plants/moss").GetComponentsInChildren<MeshFilter>().First(f => f.sharedMesh.name == "MossTuft");
            var mats = new List<Matrix4x4>();
            var cols = new List<Color>();
            // marimo balls on the sand
            foreach (var (x, z, r) in new[] { (0.2f, 1.85f, 0.16f), (-2.2f, 1.95f, 0.12f), (4.0f, 1.9f, 0.14f) })
            {
                var c = P(x, Sand(x, z) + r * 0.85f, z);
                for (int i = 0; i < 220; i++)
                {
                    var v = RandomDir();
                    var q = Quaternion.FromToRotation(Vector3.up, v) * Quaternion.AngleAxis(R(0, 360), Vector3.up);
                    mats.Add(Matrix4x4.TRS(c + v * r * R(0.82f, 0.92f), q, Vector3.one * R(0.7f, 1.2f)));
                    cols.Add(TextureHsl(0.27f + R(-0.02f, 0.02f), 0.35f, R(0.6f, 0.9f)));
                }
            }
            // moss carpets on the tops of rocks, the castle mound, the arch and the driftwood
            var centers = new[] { (-2.0f, -0.9f, 0.85f, 1.05f), (-3.9f, -0.9f, 0.9f, 0.85f), (0.4f, -1.4f, 0.6f, 0.65f), (-1.3f, 0.05f, 0.8f, 1.05f), (-5.3f, 0.4f, 0.45f, 0.3f), (3.0f, -1.9f, 0.5f, 0.5f) };
            int k = 0, guard = 0;
            while (mats.Count < 2600 && guard++ < 60000)
            {
                var (cx, cz, rx, rz) = centers[k % centers.Length];
                float x = cx + R(-rx, rx), z = cz + R(-rz, rz);
                var u = P(x, 0, z);
                float y = layout.SurfaceAt(u.x, u.z);
                if (y < Sand(x, z) + 0.06f) continue;
                var n = layout.SurfaceNormal(u.x, u.z);
                if (n.y < 0.5f) continue;
                var q = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.AngleAxis(R(0, 360), Vector3.up);
                float sc = R(0.7f, 1.5f);
                mats.Add(Matrix4x4.TRS(new Vector3(u.x, y - 0.01f, u.z), q, new Vector3(sc, sc * R(0.6f, 1.1f), sc)));
                cols.Add(TextureHsl(0.25f + R(-0.03f, 0.04f), 0.35f, R(0.55f, 0.95f)));
                k++;
            }
            var mat = new Material(Materials.FromGltf(src.GetComponent<MeshRenderer>().sharedMaterial, "moss", "Static"));
            mat.SetFloat("_VertexColor", 1);
            Obj("Moss", plants, SaveMesh(Merge(src.sharedMesh, mats, cols, false, "Moss")), Materials.Save("moss_field", mat), Vector3.zero);
        }

        static void BuildCorals()
        {
            // corals glow at night: their baked colour doubles as emission
            Material Glowing(GameObject go, string glowHex, string key)
            {
                Material last = null;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    var ms = r.sharedMaterials.Select(m =>
                    {
                        var c = new Material(m);
                        c.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                        c.SetColor("_EmissionColor", Hex(glowHex) * 0.04f);
                        var saved = Materials.Save($"coral_{key}_{m.name}", c);
                        AddGlow(saved, Hex(glowHex), 0.04f, 0.94f, false);
                        return last = saved;
                    }).ToArray();
                    r.sharedMaterials = ms;
                }
                return last;
            }
            foreach (var (x, z, k, glow) in new[] { (2.4f, 0.1f, 0, "#ff2f86"), (-5.2f, -0.5f, 1, "#8a3dff"), (0.1f, -0.5f, 2, "#ff5a1f"), (0.5f, 1.2f, 3, "#16c6ff") })
            {
                var m = Node("corals", $"Coral{k}", plants);
                m.transform.localPosition = P(x, Sand(x, z) - 0.03f, z);
                m.transform.localRotation = Yaw(R(0, Mathf.PI * 2));
                Glowing(m, glow, $"c{k}");
                Clump("коралл", x, z, 0.4f);
            }
            var brain = Node("corals", "Brain", plants);
            brain.transform.localPosition = P(-0.2f, Sand(-0.2f, 1.75f) - 0.03f, 1.75f);
            Glowing(brain, "#9dff3a", "brain");
            var anem = new GameObject("Anemone").transform;
            anem.SetParent(plants, false);
            anem.localPosition = P(1.3f, Sand(1.3f, 0.75f) - 0.03f, 0.75f);
            Node("corals", "AnemoneBase", anem);
            Node("corals", "SwayAnemone", anem);
            Glowing(anem.gameObject, "#ff63c8", "anemone");
            layout.anemone = P(1.3f, Sand(1.3f, 0.75f) + 0.35f, 0.75f);
            Clump("актиния", 1.3f, 0.75f, 0.35f);
        }

        /// <summary>
        /// Reflections come from the web version's studio environment: bake the EnvSky dome into a
        /// cubemap with a probe that sees nothing but the sky, and use it as the scene's default
        /// reflection at the web's envMapIntensity (0.5). DayCycle adds its diffuse part to the ambient.
        /// </summary>
        static void BakeEnvironment()
        {
            var sky = Materials.NewShader("EnvSky", "Aquarium/EnvSky");
            RenderSettings.skybox = sky;
            var go = new GameObject("EnvProbe");
            go.transform.position = P(0, 3, 0);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.cullingMask = 0;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.resolution = 128;
            probe.hdr = true;
            const string path = "Assets/Aquarium/Art/Generated/Environment.exr";
            bool ok = Lightmapping.BakeReflectionProbe(probe, path);
            UnityEngine.Object.DestroyImmediate(go);
            var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            Debug.Log($"environment baked: {ok}, {cube}");
            if (!cube) return;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 0.5f;
        }

        // ------------------------------------------------------------------ light shafts and drifting particles (particles.js)
        static void BuildRaysAndDust()
        {
            var fx = Group("Water");
            day.rays = Materials.NewShader("GodRays", "Aquarium/GodRays");
            var rays = fx.gameObject.AddComponent<GodRays>();
            rays.rays = new Transform[9];
            rays.tilts = new float[9];
            for (int i = 0; i < 9; i++)
            {
                float w = R(1.0f, 2.4f), h = 5.3f;
                var mesh = MeshGen.Quad(w, h, $"GodRay{i}");
                var v = mesh.vertices;
                for (int k = 0; k < v.Length; k++) v[k].y -= h / 2;   // hangs from the surface
                mesh.vertices = v;
                mesh.RecalculateBounds();
                var go = Obj($"Ray{i}", fx, SaveMesh(mesh), day.rays, P(-5 + i * 1.25f + R(-0.3f, 0.3f), Terrain.WaterY, R(-1.6f, 1.6f)), null, false);
                go.GetComponent<MeshRenderer>().receiveShadows = false;
                rays.rays[i] = go.transform;
                rays.tilts[i] = R(0.12f, 0.28f);
            }
            rays.Face(P(0, 3.3f, 14.5f));

            const int count = 5000;
            var verts = new Vector3[count * 4];
            var corner = new Vector2[count * 4];
            var seed = new Vector2[count * 4];
            var tris = new int[count * 6];
            var corners = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            for (int i = 0; i < count; i++)
            {
                var p = P(R(-5.9f, 5.9f), R(0.4f, 5.6f), R(-2.4f, 2.4f));
                float s = R();
                for (int k = 0; k < 4; k++) { verts[i * 4 + k] = p; corner[i * 4 + k] = corners[k]; seed[i * 4 + k] = new Vector2(s, 0); }
                int b = i * 4;
                tris[i * 6] = b; tris[i * 6 + 1] = b + 2; tris[i * 6 + 2] = b + 1;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = b + 3; tris[i * 6 + 5] = b + 2;
            }
            var dust = new Mesh { name = "Dust", indexFormat = IndexFormat.UInt32, vertices = verts, uv = corner, uv2 = seed, triangles = tris };
            // the particles drift by up to ~1 unit: widen the bounds so the mesh is never culled
            dust.bounds = new Bounds(P(0, 3, 0), new Vector3(W + 2, H + 2, D + 2));
            day.dust = Materials.NewShader("Dust", "Aquarium/Dust");
            Obj("Dust", fx, SaveMesh(dust), day.dust, Vector3.zero, null, false).GetComponent<MeshRenderer>().receiveShadows = false;
        }

        // ------------------------------------------------------------------ the living part
        static void BuildApp()
        {
            var go = new GameObject("App");
            go.transform.SetParent(root, false);
            var app = go.AddComponent<Aquarium.Fish.AquariumApp>();
            app.fishModels = FishModelBuilder.Build();
            app.critterModels = CritterModelBuilder.Build();
            app.plantModels = plantSet;
            app.plantRoot = growing;
            app.layout = layout;
            app.day = day;
            app.foodMesh = SaveMesh(MeshGen.Cylinder(1, 1, 0.35f, 7, "Food"));
            app.foodMaterial = Materials.NewLit("Food", Color.white, 0.7f, emission: Hex("#331100") * 0.4f, caustics: 0.5f);
            app.bubbleMesh = SaveMesh(MeshGen.Sphere(1, 12, 8, "Bubble"));
            app.bubbleMaterial = Materials.NewShader("Bubble", "Aquarium/Bubble");
        }

        // ------------------------------------------------------------------ camera and post
        static void BuildCameraAndPost()
        {
            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(root, false);
            camGo.transform.position = P(0, 3.3f, 14.5f);
            camGo.transform.LookAt(P(0, 2.6f, 0));
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 38;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 200;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Hex("#05070c");
            camGo.AddComponent<AudioListener>();
            var data = camGo.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            camGo.AddComponent<OrbitCamera>();

            const string profilePath = "Assets/Aquarium/Art/Generated/AquariumVolume.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var tm = profile.Add<Tonemapping>(true); tm.mode.value = TonemappingMode.ACES;
            var bloom = profile.Add<Bloom>(true); bloom.intensity.value = 0.5f; bloom.threshold.value = 0.95f; bloom.scatter.value = 0.65f;
            var ca = profile.Add<ColorAdjustments>(true); ca.postExposure.value = 0;
            var vig = profile.Add<Vignette>(true); vig.intensity.value = 0.22f; vig.smoothness.value = 0.45f;
            // depth of field for the observe and cinematic modes (the director turns it on and focuses)
            var dof = profile.Add<DepthOfField>(true); dof.mode.value = DepthOfFieldMode.Bokeh; dof.focalLength.value = 50; dof.aperture.value = 14; dof.focusDistance.value = 10;
            dof.active = false;
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            var volGo = new GameObject("Post");
            volGo.transform.SetParent(root, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            day.volume = vol;
        }

        /// <summary>The interface, the sound and the input around the aquarium (Aquarium.App).</summary>
        static void BuildShell(Camera cam, Volume vol)
        {
            const string uiDir = "Assets/Aquarium/UI";
            string panelPath = $"{uiDir}/AquariumPanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelPath);
            if (!panel) { panel = ScriptableObject.CreateInstance<PanelSettings>(); AssetDatabase.CreateAsset(panel, panelPath); }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>($"{uiDir}/AquariumTheme.tss");
            // sizes are CSS pixels, as in a browser: one pixel at 96 dpi, more on dense screens
            panel.scaleMode = PanelScaleMode.ConstantPhysicalSize;
            panel.referenceDpi = 96;
            panel.fallbackDpi = 96;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);

            var go = new GameObject("Shell");
            go.transform.SetParent(root, false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = panel;
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            var audio = go.AddComponent<Aquarium.App.AquaAudio>();
            var shell = go.AddComponent<Aquarium.App.AquariumShell>();
            shell.app = root.GetComponentInChildren<Aquarium.Fish.AquariumApp>();
            shell.day = day;
            shell.cam = cam;
            shell.orbit = cam.GetComponent<OrbitCamera>();
            shell.volume = vol;
            shell.audio = audio;
            shell.document = doc;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
