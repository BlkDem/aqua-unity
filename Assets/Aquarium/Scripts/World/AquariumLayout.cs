// What the inhabitants need to know about the scenery: named places (fish have favourite
// spots, fry grow up in a nursery, the history mentions them by name), obstacles to swim
// around, and a few anchor points. Filled in by the scene builder (the web version's decor.js).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.World
{
    [Serializable]
    public sealed class Spot
    {
        public string name;
        public Vector3 position;
        /// <summary>castle, hide, landmark, ship, arch, cave, open, filter, glass...</summary>
        public string kind;
        public float radius;
    }

    [Serializable]
    public struct Obstacle
    {
        public Vector3 center;
        public float radius;
    }

    public sealed class AquariumLayout : MonoBehaviour
    {
        public List<Spot> spots = new List<Spot>();
        public List<Obstacle> obstacles = new List<Obstacle>();
        public Vector3 filterOutlet, filterIntake, diverHelmet, woodCenter, anemone, secretCave, chestPosition;
        public Transform chestLid, duck;
        [Tooltip("Colliders of things critters can walk on (decor tops); the sand is analytic")]
        public LayerMask walkable = ~0;

        public Spot SpotByName(string name) => spots.Find(s => s.name == name);

        /// <summary>Height of whatever is underfoot at (x, z): the sand, or the top of the decor there.</summary>
        public float SurfaceAt(float x, float z)
        {
            float sand = Terrain.SandHeight(-x, z);
            if (Physics.Raycast(new Vector3(x, 20, z), Vector3.down, out var hit, 30, walkable, QueryTriggerInteraction.Ignore))
                return Mathf.Max(sand, hit.point.y);
            return sand;
        }

        public Vector3 SurfaceNormal(float x, float z, float e = 0.05f) =>
            new Vector3(SurfaceAt(x - e, z) - SurfaceAt(x + e, z), 2 * e, SurfaceAt(x, z - e) - SurfaceAt(x, z + e)).normalized;
    }
}
