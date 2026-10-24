// The aquarium as a user sees it: the scene running with a world of its own (three days
// away), the interface clicked through, pictures of the screen with the interface on top
// in Docs/unity/ui-*.png.
using System.Collections;
using System.IO;
using System.Linq;
using Aquarium.App;
using Aquarium.Fish;
using Aquarium.Life;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Aquarium.PlayTests
{
    public sealed class ShellTests
    {
        const int Width = 1600, Height = 900;
        RenderTexture uiRt;
        AquariumShell shell;

        [UnitySetUp]
        public IEnumerator Load()
        {
            AquariumApp.storeOverride = new MemoryWorldStore();
            AquariumApp.optionsOverride = new WorldOptions { AwayHours = 72 };
            yield return SceneManager.LoadSceneAsync("Aquarium");
            shell = Object.FindAnyObjectByType<AquariumShell>();
            // the interface draws into a texture of its own, put over the camera's picture
            uiRt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            shell.document.panelSettings.targetTexture = uiRt;
            shell.document.panelSettings.clearColor = true;
            shell.document.panelSettings.colorClearValue = Color.clear;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator Unload()
        {
            shell.document.panelSettings.targetTexture = null;
            shell.document.panelSettings.clearColor = false;
            AquariumApp.storeOverride = null;
            AquariumApp.optionsOverride = null;
            Object.Destroy(uiRt);
            yield return null;
        }

        static IEnumerator Wait(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Shot(string name)
        {
            yield return null;
            var cam = shell.cam;
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            var scene = Read(rt);
            var ui = Read(uiRt);
            var a = scene.GetPixels32();
            var b = ui.GetPixels32();
            for (int i = 0; i < a.Length; i++)
            {
                // the interface texture holds premultiplied colour
                float k = b[i].a / 255f;
                a[i] = new Color32((byte)Mathf.Min(255, b[i].r + a[i].r * (1 - k)), (byte)Mathf.Min(255, b[i].g + a[i].g * (1 - k)), (byte)Mathf.Min(255, b[i].b + a[i].b * (1 - k)), 255);
            }
            scene.SetPixels32(a);
            Directory.CreateDirectory("Docs/unity");
            File.WriteAllBytes($"Docs/unity/ui-{name}.png", scene.EncodeToPNG());
            Object.Destroy(scene);
            Object.Destroy(ui);
            rt.Release();
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            return t;
        }

        [UnityTest]
        public IEnumerator SoundIsAlive()
        {
            yield return Wait(0.5f);
            var a = shell.audio;
            a.enabled = false;   // the audio thread stops calling it; render here instead
            a.Tap();
            a.Chime();
            a.Sprinkle();
            var buf = new float[1024 * 2];
            double sum = 0;
            float peak = 0;
            int n = 0;
            for (int i = 0; i < AudioSettings.outputSampleRate * 2 / 1024; i++)
            {
                a.Render(buf, 2);
                foreach (var v in buf)
                {
                    Assert.That(float.IsNaN(v) || float.IsInfinity(v), Is.False);
                    sum += v * v; peak = Mathf.Max(peak, Mathf.Abs(v)); n++;
                }
            }
            a.enabled = true;
            float rms = Mathf.Sqrt((float)(sum / n));
            Debug.Log($"SOUND: rms {rms:F4}, peak {peak:F3}");
            Assert.That(rms, Is.GreaterThan(0.002f), "the water and the music are heard");
            Assert.That(peak, Is.LessThanOrEqualTo(1));
        }

        [UnityTest]
        public IEnumerator ClickThrough()
        {
            Assert.That(shell.app.Away, Is.Not.Null, "three days away should give a summary");
            yield return Wait(6);   // shaders finish compiling in the editor
            Assert.That(shell.document.rootVisualElement.Q(className: "away"), Is.Not.Null, "the away card is shown");
            yield return Shot("away");

            // close it, open the roster
            shell.document.rootVisualElement.Q(className: "away").RemoveFromHierarchy();
            shell.ui.TogglePanel("roster");
            yield return Wait(1);
            yield return Shot("roster");
            shell.ui.ShowRosterTab("hist");
            yield return Wait(0.5f);
            yield return Shot("history");

            shell.ui.TogglePanel("settings");
            yield return Wait(1);
            yield return Shot("settings");
            shell.ui.TogglePanel("settings");

            // select a goldfish: the card, the camera follows
            var fish = shell.app.Eco.fish.First(f => f.sp.id == "goldfish" && !f.hidden);
            shell.Select(fish);
            yield return Wait(3);
            Assert.That(shell.director.mode, Is.EqualTo(CameraMode.Follow));
            yield return Shot("card");

            // feed, knock on the glass, open the chest
            shell.Feed();
            shell.app.Eco.Tap(new Vector3(0, 3, 2.4f));
            shell.app.Eco.ClickProp("chest");
            yield return Wait(2);
            Assert.That(shell.app.World.State.Secrets, Does.Contain("chest"));

            shell.Select(null);
            shell.SetMode(CameraMode.Cinematic);
            yield return Wait(4);
            Assert.That(shell.director.letterbox, Is.GreaterThan(0.8f));
            yield return Shot("cinematic");
            shell.SetMode(CameraMode.Free);
            yield return Wait(1);
            Assert.That(shell.director.mode, Is.EqualTo(CameraMode.Free));
        }
    }
}
