// Drives the shader globals shared by every aquarium material: time, the slow current that
// sways the plants, and (from DayCycle) caustic strength and night. Runs in the editor too, so
// the scene view shows the aquarium alive.
using UnityEngine;

namespace Aquarium.World
{
    [ExecuteAlways]
    public sealed class AquariumClock : MonoBehaviour
    {
        static readonly int TimeId = Shader.PropertyToID("_AqTime");
        static readonly int CurrentId = Shader.PropertyToID("_AqCurrent");
        static readonly int FishId = Shader.PropertyToID("_AqFish");

        /// <summary>Set to freeze the aquarium at a moment (screenshots, tests).</summary>
        public static float? Override;

        public float Seconds { get; private set; }

        void OnEnable() => Shader.SetGlobalVectorArray(FishId, new Vector4[8]);

        void Update() => Apply(Override ?? (Application.isPlaying ? Time.time : (float)(Time.realtimeSinceStartupAsDouble % 3600)));

        public void Apply(float t)
        {
            Seconds = t;
            Shader.SetGlobalFloat(TimeId, t);
            // web: (sin(t*0.05)*0.03 + sin(t*0.13)*0.015, cos(t*0.04)*0.02), x mirrored
            Shader.SetGlobalVector(CurrentId, new Vector4(-(Mathf.Sin(t * 0.05f) * 0.03f + Mathf.Sin(t * 0.13f) * 0.015f), Mathf.Cos(t * 0.04f) * 0.02f));
        }
    }
}
