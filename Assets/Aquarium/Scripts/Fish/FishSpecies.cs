// How each species looks and swims (the web version's species.js, apart from life, which is
// Aquarium.Life.Species). Sizes in metres of the aquarium, speeds in body-independent units/s.
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    public sealed class Swim
    {
        public float cruise, max, accel, turn, amp, burst;
        /// <summary>Wiggles more than it swims (clownfish).</summary>
        public bool wiggle;
    }

    public sealed class FishSpecies
    {
        public string id;
        public float len, height, width;
        /// <summary>Depth band it prefers, from..to (height above the floor of the tank).</summary>
        public float zoneMin, zoneMax;
        public float school, social;
        public string homeKind;
        public bool bottom, nocturnal, glow;
        /// <summary>Colour variants with their own texture (model key "id-k").</summary>
        public int texVariants;
        public string iris;
        public Swim swim;

        public Aquarium.Life.SpeciesInfo Life => Aquarium.Life.Species.Get(id);
        public string Title => Life.Title;
        public bool Rare => Life.Rare;

        public string ModelKey(int variant) => texVariants > 0 ? $"{id}-{variant % texVariants}" : id;

        static Swim S(float cruise, float max, float amp, float accel, float turn, float burst, bool wiggle = false) =>
            new Swim { cruise = cruise, max = max, amp = amp, accel = accel, turn = turn, burst = burst, wiggle = wiggle };

        public static readonly Dictionary<string, FishSpecies> All = new Dictionary<string, FishSpecies>
        {
            ["neon"] = new FishSpecies { id = "neon", len = 0.34f, height = 0.1f, width = 0.065f, zoneMin = 1.0f, zoneMax = 3.6f, school = 1.0f, social = 0.5f, iris = "#8fd6ff", swim = S(0.95f, 2.6f, 0.05f, 5, 5.5f, 0.7f) },
            ["guppy"] = new FishSpecies { id = "guppy", len = 0.36f, height = 0.11f, width = 0.07f, zoneMin = 2.6f, zoneMax = 5.3f, school = 0.45f, social = 0.7f, texVariants = 7, iris = "#d8c070", swim = S(0.6f, 1.9f, 0.05f, 3.5f, 4.5f, 0.4f) },
            ["goldfish"] = new FishSpecies { id = "goldfish", len = 0.85f, height = 0.5f, width = 0.36f, zoneMin = 1.2f, zoneMax = 4.8f, school = 0, social = 0.6f, iris = "#e0a030", swim = S(0.42f, 1.3f, 0.1f, 1.4f, 1.8f, 0) },
            ["angelfish"] = new FishSpecies { id = "angelfish", len = 0.62f, height = 0.75f, width = 0.12f, zoneMin = 1.8f, zoneMax = 4.6f, school = 0.15f, social = 0.5f, iris = "#d04a2a", swim = S(0.33f, 1.2f, 0.05f, 1.2f, 2.0f, 0) },
            ["clown"] = new FishSpecies { id = "clown", len = 0.46f, height = 0.21f, width = 0.13f, zoneMin = 0.9f, zoneMax = 3.0f, school = 0, social = 0.5f, homeKind = "anemone", iris = "#e07a20", swim = S(0.45f, 1.6f, 0.05f, 3, 4, 0.2f, true) },
            ["cory"] = new FishSpecies { id = "cory", len = 0.42f, height = 0.17f, width = 0.15f, zoneMin = 0, zoneMax = 0.6f, school = 0.35f, social = 0.8f, bottom = true, nocturnal = true, iris = "#b89a60", swim = S(0.35f, 1.4f, 0.05f, 3, 3.8f, 0.6f) },
            ["cichlid"] = new FishSpecies { id = "cichlid", len = 0.58f, height = 0.24f, width = 0.13f, zoneMin = 0.6f, zoneMax = 3.2f, school = 0, social = 0.2f, texVariants = 2, iris = "#ffd040", swim = S(0.55f, 2.0f, 0.05f, 3, 3.6f, 0.3f) },
            ["discus"] = new FishSpecies { id = "discus", len = 0.6f, height = 0.58f, width = 0.11f, zoneMin = 1.5f, zoneMax = 4.2f, school = 0.2f, social = 0.6f, texVariants = 2, iris = "#ff3b1f", swim = S(0.28f, 1.1f, 0.035f, 1.1f, 1.8f, 0) },
            ["barb"] = new FishSpecies { id = "barb", len = 0.38f, height = 0.19f, width = 0.08f, zoneMin = 1.0f, zoneMax = 4.0f, school = 0.85f, social = 0.6f, iris = "#ff5533", swim = S(1.0f, 2.6f, 0.05f, 5, 5.5f, 0.6f) },
            ["betta"] = new FishSpecies { id = "betta", len = 0.42f, height = 0.14f, width = 0.09f, zoneMin = 3.2f, zoneMax = 5.3f, school = 0, social = 0.1f, iris = "#5a3a80", swim = S(0.25f, 1.2f, 0.05f, 1.6f, 2.8f, 0) },
            ["rainbow"] = new FishSpecies { id = "rainbow", len = 0.48f, height = 0.2f, width = 0.07f, zoneMin = 3.0f, zoneMax = 5.2f, school = 0.8f, social = 0.6f, iris = "#e6c060", swim = S(1.1f, 2.6f, 0.045f, 4.5f, 4.0f, 0.4f) },
            ["moonfish"] = new FishSpecies { id = "moonfish", len = 0.5f, height = 0.3f, width = 0.1f, zoneMin = 1.5f, zoneMax = 5.0f, school = 0, social = 0.3f, nocturnal = true, glow = true, iris = "#b0e8ff", swim = S(0.35f, 1.3f, 0.06f, 1.6f, 2.5f, 0) },
        };

        public static IEnumerable<string> ModelKeys()
        {
            foreach (var s in All.Values)
                if (s.texVariants > 0) for (int k = 0; k < s.texVariants; k++) yield return $"{s.id}-{k}";
                else yield return s.id;
        }
    }
}
