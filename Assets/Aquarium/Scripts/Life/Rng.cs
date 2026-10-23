// Random numbers for the living world. The seeded generator and the string hash are the web
// version's mulberry32 and hashStr bit for bit, so founders created from the same salt get the
// same personalities and sizes in both versions.
using System;
using System.Collections.Generic;

namespace Aquarium.Life
{
    /// <summary>A source of uniform numbers in [0, 1).</summary>
    public interface IRng
    {
        double Next();
    }

    public sealed class SystemRng : IRng
    {
        readonly Random random;
        public SystemRng(int? seed = null) => random = seed.HasValue ? new Random(seed.Value) : new Random();
        public double Next() => random.NextDouble();
    }

    /// <summary>mulberry32: a tiny seeded generator, identical to the web version's.</summary>
    public sealed class Mulberry32 : IRng
    {
        uint a;
        public Mulberry32(uint seed) => a = seed;

        public double Next()
        {
            unchecked
            {
                a += 0x6d2b79f5;
                uint t = (a ^ (a >> 15)) * (1 | a);
                t = (t + (t ^ (t >> 7)) * (61 | t)) ^ t;
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }
    }

    public static class Rng
    {
        /// <summary>FNV-1a over UTF-16 code units (JavaScript's charCodeAt), as hashStr in the web version.</summary>
        public static uint HashStr(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s)
                {
                    h ^= c;
                    h *= 16777619;
                }
                return h;
            }
        }

        public static double Range(this IRng r, double a, double b) => a + r.Next() * (b - a);
        public static int Index(this IRng r, int count) => (int)Math.Floor(r.Next() * count);
        public static T Pick<T>(this IRng r, IReadOnlyList<T> list) => list[r.Index(list.Count)];
        public static double Clamp(double v, double a, double b) => Math.Max(a, Math.Min(b, v));

        /// <summary>JavaScript's Math.round: halves go up (C#'s Math.Round would go to even).</summary>
        public static long Round(double v) => (long)Math.Floor(v + 0.5);
    }
}
