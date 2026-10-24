// Quality presets at run time (the web version's setQuality / adaptResolution): the project's
// quality levels (a URP asset each, Editor/QualityBuilder.cs) plus what the asset does not
// cover — how much dust floats in the water, bloom filtering, the depth-of-field kind. The
// render scale follows the frame rate: below ~30 fps fewer pixels are drawn (down to half),
// with headroom they come back; still slow at half resolution, the preset steps down.
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Aquarium.App
{
    public sealed class Quality
    {
        public static readonly int DustCutId = Shader.PropertyToID("_AqDustCut");
        /// <summary>Share of the dust particles left out, per preset (the web version draws 40% / 70% / all).</summary>
        static readonly float[] DustCut = { 0.6f, 0.3f, 0, 0 };

        readonly Volume volume;
        public int Level { get; private set; }
        public bool auto = true;
        /// <summary>The automatic factor on the preset's render scale (0.5..1).</summary>
        public float scale = 1;
        float baseScale = 1, holdUntil;
        readonly List<float> history = new List<float>();
        int slowAtFloor;
        /// <summary>The preset was lowered automatically (for a hint).</summary>
        public System.Action<string> lowered;

        public Quality(Volume volume) { this.volume = volume; }

        public static string[] Names => QualitySettings.names;

        /// <summary>
        /// The preset for the first start: medium (low when drawing without a graphics card); on a
        /// slow machine the automatic resolution, and then a lower preset, take over.
        /// </summary>
        public static int Suggest()
        {
            string n = SystemInfo.graphicsDeviceName ?? "";
            bool software = n.Contains("Basic Render") || n.Contains("llvmpipe") || n.Contains("SwiftShader");
            return software ? 0 : 1;
        }

        public void Apply(int level)
        {
            level = Mathf.Clamp(level, 0, QualitySettings.names.Length - 1);
            Level = level;
            QualitySettings.SetQualityLevel(level, true);
            baseScale = Asset ? Asset.renderScale : 1;
            scale = 1;
            holdUntil = Time.unscaledTime + 4;
            history.Clear();
            Shader.SetGlobalFloat(DustCutId, DustCut[Mathf.Min(level, DustCut.Length - 1)]);
            if (volume && volume.sharedProfile)
            {
                if (volume.sharedProfile.TryGet(out Bloom bloom))
                {
                    bloom.highQualityFiltering.value = level >= 2;
                    bloom.downscale.overrideState = true;
                    bloom.downscale.value = level == 0 ? BloomDownscaleMode.Quarter : BloomDownscaleMode.Half;
                }
                if (volume.sharedProfile.TryGet(out DepthOfField dof)) dof.mode.value = level == 0 ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Bokeh;
            }
        }

        static UniversalRenderPipelineAsset Asset => QualitySettings.renderPipeline as UniversalRenderPipelineAsset ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;

        void SetScale(float s)
        {
            scale = s;
            if (Asset) Asset.renderScale = baseScale * s;
        }

        public void SetAuto(bool on)
        {
            auto = on;
            if (!on && scale != 1) SetScale(1);
        }

        /// <summary>Called once a second with the frame rate.</summary>
        public void Adapt(float fps)
        {
            history.Add(fps);
            if (history.Count > 3) history.RemoveAt(0);
            if (!auto || Time.unscaledTime < holdUntil || history.Count < 2) return;
            float avg = history.Average();
            // already at half resolution and still slow: step the preset down as well
            slowAtFloor = scale <= 0.5f && avg < 24 ? slowAtFloor + 1 : 0;
            if (slowAtFloor >= 4 && Level > 0)
            {
                slowAtFloor = 0;
                Apply(Level - 1);
                SetScale(0.7f);
                lowered?.Invoke(QualitySettings.names[Level]);
                return;
            }
            if (avg < 29 && scale > 0.5f) SetScale(Mathf.Max(0.5f, Mathf.Round((scale - 0.1f) * 10) / 10));
            else if (avg > 50 && scale < 1) SetScale(Mathf.Min(1, Mathf.Round((scale + 0.1f) * 10) / 10));
            else return;
            holdUntil = Time.unscaledTime + (avg < 29 ? 2.5f : 8);
            history.Clear();
        }

        /// <summary>Put the asset's render scale back (the asset is shared with the editor).</summary>
        public void Restore()
        {
            if (Asset) Asset.renderScale = baseScale;
        }
    }
}
