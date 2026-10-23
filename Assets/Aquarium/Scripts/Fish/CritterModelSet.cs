// Prefabs of the critters (made by Aquarium > Build Critter Models from the Blender models):
// rigid parts whose origins sit at the joints, animated by turning them.
using UnityEngine;

namespace Aquarium.Fish
{
    [CreateAssetMenu(menuName = "Aquarium/Critter Model Set")]
    public sealed class CritterModelSet : ScriptableObject
    {
        public GameObject crab, shrimp, snailMystery, snailNerite, snailRamshorn;

        public GameObject Snail(string type) => type switch
        {
            "mystery" => snailMystery,
            "nerite" => snailNerite,
            _ => snailRamshorn,
        };
    }
}
