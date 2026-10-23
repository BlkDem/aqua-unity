// Brings the aquarium to life in the scene: loads the saved world (the same save as the web
// version), lets its fish swim, feeds them (F), saves now and then and when the app closes.
//   command line: -timescale 600 (one real second = ten world minutes), -away 48 (pretend the
//   last visit was 48 hours ago); both keep their own save.
using System;
using System.IO;
using Aquarium.Life;
using Aquarium.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aquarium.Fish
{
    public sealed class AquariumApp : MonoBehaviour
    {
        public FishModelSet fishModels;
        public CritterModelSet critterModels;
        public PlantModelSet plantModels;
        [Tooltip("Where the growing plants live; the editor's preview copies in it are replaced at start")]
        public Transform plantRoot;
        public AquariumLayout layout;
        public DayCycle day;
        public Mesh foodMesh, bubbleMesh;
        public Material foodMaterial, bubbleMaterial;

        public Aquarium.Life.World World { get; private set; }
        public Ecosystem Eco { get; private set; }
        /// <summary>What happened while the aquarium was closed (null after a short absence).</summary>
        public AwaySummary Away { get; private set; }
        FishRenderer fishRenderer;
        FoodRenderer foodRenderer;
        readonly Vector4[] push = new Vector4[8];
        static readonly int FishId = Shader.PropertyToID("_AqFish");
        float saveT = 20;

        void Start() => Boot();

        /// <summary>Set things up (also called by tools in the editor, with a store of their own).</summary>
        public void Boot(IWorldStore store = null, WorldOptions options = null)
        {
            if (Eco != null) return;
            store ??= new FileWorldStore(Path.Combine(Application.persistentDataPath, "world"));
            World = new Aquarium.Life.World(store, options ?? Options());
            // first the world catches up with the time it was closed, then it comes to life
            Away = Lifecycle.CatchUp(World, World.Now());
            Eco = new Ecosystem(World, layout);
            if (plantRoot)
            {
                for (int i = plantRoot.childCount - 1; i >= 0; i--)
                {
                    var old = plantRoot.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(old); else DestroyImmediate(old);
                }
                Eco.plants = new PlantSystem(Eco, plantModels, plantRoot);
                int sprouted = Eco.plants.Tick(World.Now());
                if (Away != null) Away.Plants = sprouted;
            }
            fishRenderer = new FishRenderer(fishModels);
            foreach (var f in Eco.fish) fishRenderer.Add(f);
            Eco.OnFish += fishRenderer.Add;
            foodRenderer = new FoodRenderer(foodMesh, foodMaterial);
            var critterRoot = transform.Find("Critters") ?? new GameObject("Critters").transform;
            critterRoot.SetParent(transform, false);
            Eco.critters.AddRange(CritterFactory.Build(Eco, critterModels, critterRoot));
            Eco.OnStory += s => Debug.Log($"[история] {s.text}");
            if (Away != null) Debug.Log($"[пока вас не было] {Describe(Away)}");
        }

        /// <summary>A short text for "while you were away" (the full card comes with the UI).</summary>
        public static string Describe(AwaySummary a)
        {
            var parts = new System.Collections.Generic.List<string> { $"прошло {Words.Duration(TimeSpan.FromMilliseconds(a.Away))}" };
            if (a.Pairs.Count > 0) parts.Add($"новых пар: {a.Pairs.Count}");
            int fry = 0;
            foreach (var b in a.Births) fry += b.N;
            if (fry > 0) parts.Add($"родилось {Words.FryWord(fry)}");
            if (a.GrewJuvenile > 0) parts.Add($"подросли: {a.GrewJuvenile}");
            if (a.GrewAdult > 0) parts.Add($"повзрослели: {a.GrewAdult}");
            foreach (var s in a.Schools) parts.Add($"стая «{s.Species.Title}»: {s.From} → {s.To}");
            if (a.Plants > 0) parts.Add($"новых побегов: {a.Plants}");
            return string.Join(", ", parts);
        }

        static WorldOptions Options()
        {
            var o = new WorldOptions();
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-timescale" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s)) o.TimeScale = s;
                if (args[i] == "-away" && double.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a)) o.AwayHours = a;
            }
            return o;
        }

        void Update()
        {
            if (Eco == null) return;
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) Eco.Feed();
            Step(Mathf.Min(Time.deltaTime, 1 / 20f));
            saveT -= Time.unscaledDeltaTime;
            if (saveT <= 0) { saveT = 20; Eco.Save(); }
        }

        /// <summary>Advance the aquarium by dt seconds and queue it for drawing.</summary>
        public void Step(float dt)
        {
            Simulate(dt);
            Draw();
        }

        /// <summary>Advance the aquarium by dt seconds without drawing (tools fast-forward with it).</summary>
        public void Simulate(float dt)
        {
            Eco.night = day ? day.NightAmount : 0;
            if (day) day.crystalFlash = Eco.crystalFlash;
            var cam = Camera.main;
            if (cam) Eco.cameraPos = Ecosystem.Web(cam.transform.position);
            Eco.Update(dt);
        }

        /// <summary>Queue the fish, food and bubbles for this frame (for one camera only, if given).</summary>
        public void Draw(Camera only = null)
        {
            Eco.PushPlants(push);
            Shader.SetGlobalVectorArray(FishId, push);
            fishRenderer.Draw(Eco.night, only);
            foodRenderer.Draw(Eco.food, only);
            Eco.bubbles.Draw(bubbleMesh, bubbleMaterial, only);
        }

        void OnApplicationPause(bool paused) { if (paused) Eco?.Save(); }
        void OnApplicationQuit() => Eco?.Save();
    }
}
