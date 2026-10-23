// The web version (three.js) is right-handed; Unity is left-handed. glTFast imports the shared
// models by mirroring X, so positions from the web code map as (x, y, z) -> (-x, y, z), and a
// rotation R becomes M R M with M = diag(-1, 1, 1): X turns keep their sign, Y and Z flip.
using UnityEngine;

namespace Aquarium.World
{
    public static class WebSpace
    {
        public static Vector3 P(float x, float y, float z) => new Vector3(-x, y, z);
        public static Vector3 P(Vector3 web) => new Vector3(-web.x, web.y, web.z);

        /// <summary>three.js Euler angles (radians, order XYZ) to a Unity rotation.</summary>
        public static Quaternion Rot(float x, float y, float z) =>
            Quaternion.AngleAxis(x * Mathf.Rad2Deg, Vector3.right)
            * Quaternion.AngleAxis(-y * Mathf.Rad2Deg, Vector3.up)
            * Quaternion.AngleAxis(-z * Mathf.Rad2Deg, Vector3.forward);

        public static Quaternion Yaw(float y) => Rot(0, y, 0);

        /// <summary>A colour given as in the web code (sRGB hex) for a material or light in linear space.</summary>
        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex.StartsWith("#") ? hex : "#" + hex, out var c);
            return c;
        }
    }

    /// <summary>The tank and its sand, in web coordinates (convert with WebSpace.P).</summary>
    public static class Terrain
    {
        public const float W = 12, D = 5, H = 6.3f, WaterY = 5.6f;

        /// <summary>Cheap smooth noise used for the sand (the web version's noise3).</summary>
        public static float Noise3(float x, float y, float z) =>
            Mathf.Sin(x * 1.7f + Mathf.Sin(y * 1.3f + z * 0.7f) * 1.3f) * 0.5f
            + Mathf.Sin(y * 2.3f + Mathf.Sin(z * 1.9f + x * 0.6f) * 1.1f) * 0.3f
            + Mathf.Sin(z * 2.9f + Mathf.Sin(x * 2.2f + y * 1.4f)) * 0.2f;

        /// <summary>Height of the sand at web (x, z): rising towards the back, mounds under the decor.</summary>
        public static float SandHeight(float x, float z)
        {
            float back = (-z + D / 2) / D;
            float h = 0.42f + back * back * 0.55f;
            h += Noise3(x * 0.6f, 0.3f, z * 0.7f) * 0.07f + Noise3(x * 2.1f, 1.7f, z * 2.3f) * 0.025f;
            h += 0.35f * Mathf.Exp(-((x + 3.9f) * (x + 3.9f) + (z + 0.9f) * (z + 0.9f)) / 2.2f);
            h += 0.22f * Mathf.Exp(-((x - 3.6f) * (x - 3.6f) + (z + 0.4f) * (z + 0.4f)) / 3.0f);
            h += 0.18f * Mathf.Exp(-((x - 0.2f) * (x - 0.2f) + (z + 1.5f) * (z + 1.5f)) / 1.6f);
            return h;
        }

        /// <summary>The sand with its small ripples, as the mesh is built.</summary>
        public static float SandSurface(float x, float z) => SandHeight(x, z) + Mathf.Sin(x * 5.0f + Mathf.Sin(z * 2.0f) * 1.5f) * 0.012f;
    }
}
