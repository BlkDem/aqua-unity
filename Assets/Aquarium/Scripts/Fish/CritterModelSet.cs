// Prefabs of the critters (made by Aquarium > Build Critter Models from the Blender models):
// rigid parts whose origins sit at the joints, animated by turning them.
using UnityEngine;

namespace Aquarium.Fish
{
    [CreateAssetMenu(menuName = "Aquarium/Critter Model Set")]
    public sealed class CritterModelSet : ScriptableObject
    {
        public GameObject crab, shrimp, snailMystery, snailNerite, snailRamshorn;
        // a sculpted shrimp's own pattern recoloured for each of the shrimp's colours (web hex, no #)
        public string[] shrimpSkinKeys = new string[0];
        public Texture2D[] shrimpSkins = new Texture2D[0];

        public Texture2D ShrimpSkin(string hex)
        {
            int i = System.Array.IndexOf(shrimpSkinKeys, hex.TrimStart('#').ToLowerInvariant());
            return i >= 0 ? shrimpSkins[i] : null;
        }

        public GameObject Snail(string type) => type switch
        {
            "mystery" => snailMystery,
            "nerite" => snailNerite,
            _ => snailRamshorn,
        };
    }
}
