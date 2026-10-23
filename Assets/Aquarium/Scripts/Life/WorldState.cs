// The saved world, field for field the web version's save (format v2), so an aquarium can move
// between the two. Times are milliseconds since 1970 (JavaScript's Date.now()), possibly
// fractional. Keys this code does not know are kept as they are ([JsonExtensionData]).
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aquarium.Life
{
    public sealed class WorldState
    {
        [JsonProperty("v")] public int V = 2;
        [JsonProperty("salt")] public string Salt;
        [JsonProperty("created")] public double Created;
        [JsonProperty("lastSeen")] public double LastSeen;
        [JsonProperty("clock", NullValueHandling = NullValueHandling.Ignore)] public double? Clock;
        [JsonProperty("visits")] public int Visits;
        [JsonProperty("secrets")] public List<string> Secrets = new List<string>();
        [JsonProperty("nextUid")] public int NextUid = 1;
        [JsonProperty("fish")] public List<FishRecord> Fish = new List<FishRecord>();
        /// <summary>Planted spots by id: when, vigour, variant, turn, next runner.</summary>
        [JsonProperty("plants")] public Dictionary<string, PlantRecord> Plants = new Dictionary<string, PlantRecord>();
        /// <summary>Young plants grown from runners (they also know their kind and place).</summary>
        [JsonProperty("extraPlants")] public List<PlantRecord> ExtraPlants = new List<PlantRecord>();
        [JsonProperty("history")] public List<HistoryEntry> History = new List<HistoryEntry>();
        [JsonProperty("feeder")] public Feeder Feeder = new Feeder();
        /// <summary>Things that happen once (first pair, school sizes...), with when.</summary>
        [JsonProperty("marks")] public Dictionary<string, double> Marks = new Dictionary<string, double>();

        [JsonExtensionData] public IDictionary<string, JToken> Extra;
    }

    public sealed class FishRecord
    {
        [JsonProperty("uid")] public int Uid;
        [JsonProperty("sp")] public string Sp;
        [JsonProperty("name")] public string Name;
        /// <summary>"f" or "m".</summary>
        [JsonProperty("sex")] public string Sex;
        [JsonProperty("born")] public double Born;
        [JsonProperty("traits")] public Dictionary<string, double> Traits = new Dictionary<string, double>();
        [JsonProperty("gene")] public Gene Gene = new Gene();
        [JsonProperty("parents")] public int[] Parents;
        [JsonProperty("parentNames", NullValueHandling = NullValueHandling.Ignore)] public string[] ParentNames;
        [JsonProperty("gen")] public int Gen = 1;
        [JsonProperty("mate")] public int? Mate;
        [JsonProperty("nextBrood")] public double? NextBrood;
        /// <summary>Name of the favourite spot in the aquarium.</summary>
        [JsonProperty("fav")] public string Fav;
        /// <summary>Best friends by uid (as a string, as JavaScript writes object keys), closeness 0..1.</summary>
        [JsonProperty("friends")] public Dictionary<string, double> Friends = new Dictionary<string, double>();
        /// <summary>Where a fry grows up (a spot name), null once independent or for founders.</summary>
        [JsonProperty("nursery", NullValueHandling = NullValueHandling.Ignore)] public string Nursery;

        [JsonExtensionData] public IDictionary<string, JToken> Extra;

        [JsonIgnore] public bool IsFemale => Sex == "f";
        public double Friend(int uid) => Friends.TryGetValue(uid.ToString(), out var v) ? v : 0;
    }

    public sealed class Gene
    {
        [JsonProperty("size")] public double Size = 1;
        /// <summary>Colour multiplied over the body texture, RGB.</summary>
        [JsonProperty("tint")] public double[] Tint = { 1, 1, 1 };
        [JsonProperty("variant")] public int Variant;
        /// <summary>A rare colour (key of Lifecycle.Rare) or null.</summary>
        [JsonProperty("rare")] public string Rare;
    }

    public sealed class PlantRecord
    {
        [JsonProperty("planted")] public double Planted;
        [JsonProperty("gene")] public double Gene = 1;
        [JsonProperty("variant")] public int Variant;
        [JsonProperty("rot")] public double Rot;
        [JsonProperty("nextRunner", NullValueHandling = NullValueHandling.Ignore)] public double? NextRunner;

        /// <summary>id, kind, x, z, scale for plants grown from runners.</summary>
        [JsonExtensionData] public IDictionary<string, JToken> Extra;
    }

    public sealed class HistoryEntry
    {
        [JsonProperty("t")] public double T;
        /// <summary>start, pair, birth, rare, farewell, plant, arrival...</summary>
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("text")] public string Text;
    }

    public sealed class Feeder
    {
        [JsonProperty("on")] public bool On = true;
        [JsonProperty("perDay")] public int PerDay = 3;
        /// <summary>flakes, pellets or worms.</summary>
        [JsonProperty("food")] public string Food = "flakes";
        [JsonProperty("last")] public double Last;
    }
}
