// Floating plants (water lettuce) drift slowly about and bob with the ripples (web: updatePlants).
using UnityEngine;

namespace Aquarium.World
{
    public sealed class Floater : MonoBehaviour
    {
        public float x, z, phase;   // web coordinates of its home and a phase of its own

        void Update()
        {
            float t = Shader.GetGlobalFloat("_AqTime");
            transform.localPosition = WebSpace.P(x + Mathf.Sin(t * 0.05f + phase) * 0.35f, Terrain.WaterY - 0.03f + Mathf.Sin(t * 0.9f + phase) * 0.01f, z + Mathf.Cos(t * 0.04f + phase * 1.3f) * 0.2f);
            transform.Rotate(Vector3.up, 0.0004f * Mathf.Rad2Deg * 60 * Time.deltaTime, Space.Self);
        }
    }
}
