// A measured run of the aquarium (command line: -benchmark report.txt): every quality preset
// from the front and close up, plus the cinematic mode; frame rate, CPU and GPU time per frame,
// draw calls and triangles, memory of textures and meshes. Writes the report and quits.
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UIElements;

namespace Aquarium.App
{
    public sealed class Benchmark : MonoBehaviour
    {
        public string output;
        AquariumShell shell;
        readonly StringBuilder report = new StringBuilder();

        public static string Arg()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-benchmark") return args[i + 1];
            return null;
        }

        IEnumerator Start()
        {
            shell = GetComponent<AquariumShell>();
            shell.benchmarking = true;
            shell.quality.SetAuto(false);
            // the same picture size on every run (the budget is set for 1080p)
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return Wait(8);   // shaders, the first stories, the away card
            foreach (var d in shell.document.rootVisualElement.Query(className: "away").ToList()) d.RemoveFromHierarchy();

            report.AppendLine($"Живой аквариум — замер {System.DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine($"{SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}, {SystemInfo.graphicsMemorySize} МБ), {SystemInfo.processorType}, {SystemInfo.systemMemorySize} МБ ОЗУ");
            report.AppendLine($"экран {Screen.width}×{Screen.height} ({Screen.fullScreenMode}, монитор {Screen.currentResolution.width}×{Screen.currentResolution.height}, dpi {Screen.dpi}), рыб {shell.app.Eco.fish.Count}, обитателей {shell.app.Eco.critters.Count}, предложен пресет «{QualitySettings.names[Quality.Suggest()]}»");
            report.AppendLine();
            report.AppendLine("пресет      ракурс      fps   кадр мс  95% мс  CPU мс  GPU мс  draw  setpass  треуг.");
            var views = new[] { ("фронт", Director.Views[0].pos, Director.Views[0].look), ("крупно", Director.Views[4].pos, Director.Views[4].look) };
            for (int level = 0; level < QualitySettings.names.Length; level++)
            {
                shell.quality.Apply(level);
                foreach (var (name, pos, look) in views)
                {
                    shell.orbit.Place(new Vector3(-pos.x, pos.y, pos.z), new Vector3(-look.x, look.y, look.z));
                    yield return Measure(QualitySettings.names[level], name);
                }
            }
            shell.quality.Apply(2);
            shell.SetMode(CameraMode.Cinematic);
            yield return Measure(QualitySettings.names[2], "кино");
            shell.SetMode(CameraMode.Free);

            long tex = 0, mesh = 0;
            int texN = 0, meshN = 0;
            foreach (var t in Resources.FindObjectsOfTypeAll<Texture>()) { tex += Profiler.GetRuntimeMemorySizeLong(t); texN++; }
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) { mesh += Profiler.GetRuntimeMemorySizeLong(m); meshN++; }
            report.AppendLine();
            report.AppendLine($"память: текстуры {tex / 1048576} МБ ({texN}), меши {mesh / 1048576} МБ ({meshN}), всего выделено {Profiler.GetTotalAllocatedMemoryLong() / 1048576} МБ, управляемая куча {System.GC.GetTotalMemory(false) / 1048576} МБ");
            var big = Resources.FindObjectsOfTypeAll<Texture>().OrderByDescending(Profiler.GetRuntimeMemorySizeLong).Take(12);
            report.AppendLine("самые тяжёлые текстуры: " + string.Join(", ", big.Select(t => $"{t.name} {t.width}×{t.height} {(t is Texture2D t2 ? t2.format.ToString() : t.GetType().Name)} {Profiler.GetRuntimeMemorySizeLong(t) / 1048576f:0.0} МБ")));
            File.WriteAllText(output, report.ToString());
            Application.Quit();
        }

        static IEnumerator Wait(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        IEnumerator Measure(string preset, string view)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 1000;
            yield return Wait(2.5f);
            var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            var setpass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var frames = new List<float>();
            var cpu = new List<float>();
            var gpu = new List<float>();
            var timing = new FrameTiming[1];
            long dSum = 0, sSum = 0, tSum = 0;
            int n = 0;
            float end = Time.realtimeSinceStartup + 5;
            while (Time.realtimeSinceStartup < end)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0)
                {
                    if (timing[0].cpuMainThreadFrameTime > 0) cpu.Add((float)timing[0].cpuMainThreadFrameTime);
                    if (timing[0].gpuFrameTime > 0) gpu.Add((float)timing[0].gpuFrameTime);
                }
                if (draws.Valid) { dSum += draws.LastValue; sSum += setpass.LastValue; tSum += tris.LastValue; n++; }
            }
            draws.Dispose(); setpass.Dispose(); tris.Dispose();
            frames.Sort();
            float avg = frames.Average(), p95 = frames[(int)(frames.Count * 0.95f)];
            string D(long v) => n > 0 ? (v / n).ToString() : "—";
            report.AppendLine($"{preset,-11} {view,-9} {1000 / avg,5:0} {avg,8:0.0} {p95,7:0.0} {(cpu.Count > 0 ? cpu.Average() : 0),7:0.0} {(gpu.Count > 0 ? gpu.Average() : 0),7:0.0} {D(dSum),5} {D(sSum),8} {(n > 0 ? (tSum / n / 1000) + "K" : "—"),8}");
        }
    }
}
