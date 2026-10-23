// Renders the scene from the same viewpoints and times of day as the web version's reference
// shots (Docs/reference), into Docs/unity, for side-by-side comparison.
//   -executeMethod Aquarium.EditorTools.Snapshot.BuildAndRender
using System.IO;
using Aquarium.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using static Aquarium.World.WebSpace;

namespace Aquarium.EditorTools
{
    public static class Snapshot
    {
        const string Out = "Docs/unity";

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
            var day = Object.FindFirstObjectByType<DayCycle>();
            var clock = Object.FindFirstObjectByType<AquariumClock>();
            cam.GetComponent<OrbitCamera>().enabled = false;
            clock.Apply(12);
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
                foreach (var (name, pos, look) in views)
                {
                    if (name == "close" && preset != "day" && preset != "night") continue;
                    cam.transform.position = pos;
                    cam.transform.LookAt(look);
                    Object.FindFirstObjectByType<GodRays>()?.Face(pos);
                    Save(cam, $"{Out}/unity-{preset}-{name}.png", 1600, 900);
                }
            }
            Debug.Log("Snapshots rendered");
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
