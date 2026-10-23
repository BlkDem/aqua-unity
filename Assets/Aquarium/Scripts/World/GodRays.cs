// Turns the light shafts towards the camera around their tilted axis (a cylindrical billboard).
using UnityEngine;

namespace Aquarium.World
{
    [ExecuteAlways]
    public sealed class GodRays : MonoBehaviour
    {
        public Transform[] rays;
        public float[] tilts;

        void LateUpdate()
        {
            var cam = Camera.main;
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView) cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
            if (cam) Face(cam.transform.position);
        }

        public void Face(Vector3 camera)
        {
            if (rays == null) return;
            for (int i = 0; i < rays.Length; i++)
            {
                var d = camera - rays[i].position;
                d.y = 0;
                if (d.sqrMagnitude < 1e-6f) continue;
                rays[i].rotation = Quaternion.LookRotation(-d) * Quaternion.AngleAxis(tilts[i] * Mathf.Rad2Deg, Vector3.forward);
            }
        }
    }
}
