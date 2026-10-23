// Prefabs of the plants that grow (one per model variant), made by Aquarium > Build Scene.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    [CreateAssetMenu(menuName = "Aquarium/Plant Model Set")]
    public sealed class PlantModelSet : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string node;
            public GameObject prefab;
        }

        public List<Entry> plants = new List<Entry>();

        public GameObject Get(string node) => plants.Find(p => p.node == node)?.prefab;
    }
}
