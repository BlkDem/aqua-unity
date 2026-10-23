// The fish models prepared in the editor (Aquarium > Build Fish Models): per model a body mesh
// with painted eyes and a fin mesh whose pectorals know their pivots, plus their materials.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Aquarium.Fish
{
    [Serializable]
    public sealed class FishModel
    {
        public string key;
        public Mesh body, fins;
        public Material bodyMaterial, finMaterial;
    }

    [CreateAssetMenu(menuName = "Aquarium/Fish Model Set")]
    public sealed class FishModelSet : ScriptableObject
    {
        public List<FishModel> models = new List<FishModel>();

        public FishModel Get(string key) => models.Find(m => m.key == key);
    }
}
