// The water around the decor, as a signed distance to its real shapes, baked by the scene builder
// from the decor's colliders: positive in the water, negative inside a rock, a castle wall or a closed
// hull. Water that is reached only through an opening (under an arch, in the wreck's hold) is water
// too, so fish go through arches and into the ship where they fit, and never into the decor itself.
// It also knows the way out of every nook: how far each cell of water is from open water, around the
// decor, so a fish caught in a crevice follows it out.
// Positions are the web version's (x mirrored); the sand is not part of it.
using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace Aquarium.World
{
    public sealed class WaterField
    {
        /// <summary>Distances are kept to ±this, in steps of 1/Steps m: further than that the decor is far.</summary>
        public const float Range = 127f / Steps;
        const float Steps = 200;
        const int Magic = 0x46574141;   // "AAWF"

        public readonly int nx, ny, nz;
        /// <summary>The centre of the first cell.</summary>
        public readonly Vector3 origin;
        public readonly float cell;
        readonly sbyte[] d;
        /// <summary>Per cell: how many cells of water away open water is (255: no way, or inside the decor).</summary>
        readonly byte[] exit;

        public WaterField(int nx, int ny, int nz, Vector3 origin, float cell, sbyte[] d, byte[] exit)
        {
            this.nx = nx; this.ny = ny; this.nz = nz;
            this.origin = origin;
            this.cell = cell;
            this.d = d;
            this.exit = exit;
        }

        public int Index(int x, int y, int z) => (z * ny + y) * nx + x;

        public static sbyte Quantize(float metres) => (sbyte)Mathf.Clamp(Mathf.RoundToInt(metres * Steps), -127, 127);

        public byte[] Save()
        {
            var mem = new MemoryStream();
            using (var w = new BinaryWriter(new GZipStream(mem, System.IO.Compression.CompressionLevel.Optimal)))
            {
                w.Write(Magic);
                w.Write(nx); w.Write(ny); w.Write(nz);
                w.Write(origin.x); w.Write(origin.y); w.Write(origin.z);
                w.Write(cell);
                var raw = new byte[d.Length];
                System.Buffer.BlockCopy(d, 0, raw, 0, d.Length);
                w.Write(raw);
                w.Write(exit);
            }
            return mem.ToArray();
        }

        public static WaterField Load(byte[] bytes)
        {
            using var r = new BinaryReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress));
            if (r.ReadInt32() != Magic) return null;
            int nx = r.ReadInt32(), ny = r.ReadInt32(), nz = r.ReadInt32();
            var origin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            float cell = r.ReadSingle();
            var raw = r.ReadBytes(nx * ny * nz);
            var d = new sbyte[raw.Length];
            System.Buffer.BlockCopy(raw, 0, d, 0, raw.Length);
            return new WaterField(nx, ny, nz, origin, cell, d, r.ReadBytes(nx * ny * nz));
        }

        /// <summary>How far p is from the decor, in metres: negative inside it, Range when far or outside the field.</summary>
        public float Distance(Vector3 p)
        {
            float fx = (p.x - origin.x) / cell, fy = (p.y - origin.y) / cell, fz = (p.z - origin.z) / cell;
            if (fx < 0 || fy < 0 || fz < 0 || fx >= nx - 1 || fy >= ny - 1 || fz >= nz - 1) return Range;
            int x = (int)fx, y = (int)fy, z = (int)fz;
            float tx = fx - x, ty = fy - y, tz = fz - z;
            int i = Index(x, y, z), sy = nx, sz = nx * ny;
            float c00 = Mathf.Lerp(d[i], d[i + 1], tx), c10 = Mathf.Lerp(d[i + sy], d[i + sy + 1], tx);
            float c01 = Mathf.Lerp(d[i + sz], d[i + sz + 1], tx), c11 = Mathf.Lerp(d[i + sz + sy], d[i + sz + sy + 1], tx);
            return Mathf.Lerp(Mathf.Lerp(c00, c10, ty), Mathf.Lerp(c01, c11, ty), tz) / Steps;
        }

        /// <summary>The way out of the decor at p (away from its nearest surface), unit length.</summary>
        public Vector3 Gradient(Vector3 p)
        {
            float e = cell;
            var g = new Vector3(
                Distance(new Vector3(p.x + e, p.y, p.z)) - Distance(new Vector3(p.x - e, p.y, p.z)),
                Distance(new Vector3(p.x, p.y + e, p.z)) - Distance(new Vector3(p.x, p.y - e, p.z)),
                Distance(new Vector3(p.x, p.y, p.z + e)) - Distance(new Vector3(p.x, p.y, p.z - e)));
            float m = g.magnitude;
            return m > 1e-6f ? g / m : Vector3.up;
        }

        /// <summary>How far open water is from p around the decor, in cells (255: no way, or inside the decor).</summary>
        public int ExitAt(Vector3 p)
        {
            int x = Mathf.RoundToInt((p.x - origin.x) / cell), y = Mathf.RoundToInt((p.y - origin.y) / cell), z = Mathf.RoundToInt((p.z - origin.z) / cell);
            return x < 0 || y < 0 || z < 0 || x >= nx || y >= ny || z >= nz ? 0 : exit[Index(x, y, z)];
        }

        /// <summary>The way out of a nook at p towards open water, around the decor (unit length; zero in open water).</summary>
        public Vector3 WayOut(Vector3 p)
        {
            int x = Mathf.RoundToInt((p.x - origin.x) / cell), y = Mathf.RoundToInt((p.y - origin.y) / cell), z = Mathf.RoundToInt((p.z - origin.z) / cell);
            if (x < 1 || y < 1 || z < 1 || x >= nx - 1 || y >= ny - 1 || z >= nz - 1) return Vector3.zero;
            int here = exit[Index(x, y, z)], best = here;
            var way = Vector3.zero;
            for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int e = exit[Index(x + dx, y + dy, z + dz)];
                // the lowest neighbour; of equal ones the more upward
                if (e < best || e == best && e < here && dy > way.y) { best = e; way = new Vector3(dx, dy, dz); }
            }
            return best < here || here == 255 ? way.normalized : Vector3.zero;
        }

        /// <summary>p, moved out of the decor until it is at least r from it.</summary>
        public Vector3 Clear(Vector3 p, float r)
        {
            for (int i = 0; i < 6; i++)
            {
                float dist = Distance(p);
                if (dist >= r) break;
                p += Gradient(p) * (r - dist + 0.01f);
            }
            return p;
        }
    }
}
