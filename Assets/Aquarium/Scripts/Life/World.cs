// The persistent aquarium: who lives here, when they were born, who their parents are, how
// the plants have grown and what has happened so far. It runs on real time, so the world keeps
// living while the app is closed (see Lifecycle for what happens meanwhile). A port of the web
// version's world.js; the save is the same JSON.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Aquarium.Life
{
    /// <summary>Where the world is kept: a key-value store of JSON strings.</summary>
    public interface IWorldStore
    {
        string Load(string key);
        void Save(string key, string json);
    }

    /// <summary>One JSON file per key in a folder (the app passes its persistent data path).</summary>
    public sealed class FileWorldStore : IWorldStore
    {
        readonly string dir;
        public FileWorldStore(string dir) { this.dir = dir; Directory.CreateDirectory(dir); }
        string PathOf(string key) => Path.Combine(dir, key + ".json");
        public string Load(string key) => File.Exists(PathOf(key)) ? File.ReadAllText(PathOf(key)) : null;

        public void Save(string key, string json)
        {
            // write next to it, then swap: a crash mid-write never leaves a broken save
            string path = PathOf(key), tmp = path + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
    }

    public sealed class MemoryWorldStore : IWorldStore
    {
        public readonly Dictionary<string, string> Data = new Dictionary<string, string>();
        public string Load(string key) => Data.TryGetValue(key, out var s) ? s : null;
        public void Save(string key, string json) => Data[key] = json;
    }

    public sealed class WorldOptions
    {
        /// <summary>Trying things out: 600 makes one real second ten world minutes.</summary>
        public double TimeScale = 1;
        /// <summary>Trying things out: pretend the last visit was this many hours ago.</summary>
        public double AwayHours;
        public IRng Rng;
        /// <summary>Real time in ms since 1970; injectable for tests.</summary>
        public Func<double> RealNow;
    }

    public sealed class World
    {
        public const double Day = 86400000, Hour = 3600000;
        public const string Key = "living-aquarium.v2", DebugKey = "living-aquarium.v2.debug", OldKey = "living-aquarium.v1";

        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Include,
            FloatParseHandling = FloatParseHandling.Double,
            Culture = System.Globalization.CultureInfo.InvariantCulture,
        };

        public WorldState State { get; private set; }
        public readonly IRng Rand;
        /// <summary>Broods being staged live (nest built, fry not hatched yet) per species: they count toward the caps.</summary>
        public readonly Dictionary<string, int> Pending = new Dictionary<string, int>();
        /// <summary>True when this run started a new aquarium.</summary>
        public readonly bool Fresh;

        readonly IWorldStore store;
        readonly string key;
        readonly double scale, start, baseTime;
        readonly Func<double> realNow;
        readonly Dictionary<int, FishRecord> uids = new Dictionary<int, FishRecord>();

        public World(IWorldStore store, WorldOptions options = null)
        {
            options ??= new WorldOptions();
            this.store = store;
            Rand = options.Rng ?? new SystemRng();
            realNow = options.RealNow ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            scale = Math.Max(1, options.TimeScale);
            double away = Math.Max(0, options.AwayHours) * Hour;
            bool debug = scale > 1 || away > 0;
            key = debug ? DebugKey : Key;
            start = realNow();

            // a debug run keeps its own save, starting from a copy of the real one
            var saved = Parse(store.Load(key)) ?? (debug ? Parse(store.Load(Key)) : null);
            Fresh = saved == null;
            State = Fresh ? Create(ParseOld(store.Load(OldKey))) : saved;
            var st = State;
            // world clock: real time, or a sped-up clock that continues where the last debug run stopped
            baseTime = debug ? Math.Max(start, st.Clock ?? start) : start;
            if (away > 0)
            {
                // a brand new debug aquarium is moved into the past, as if set up and left back then
                if (Fresh) ShiftTime(st, -away);
                st.LastSeen = Now() - away;
            }
            st.Visits++;
            foreach (var r in st.Fish) uids[r.Uid] = r;
        }

        /// <summary>World time, ms since 1970.</summary>
        public double Now() => baseTime + (realNow() - start) * scale;

        public static WorldState Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                var st = JsonConvert.DeserializeObject<WorldState>(json, Json);
                return st != null && st.V == 2 ? st : null;
            }
            catch (JsonException) { return null; }
        }

        static JObject ParseOld(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JObject.Parse(json); } catch (JsonException) { return null; }
        }

        public string ToJson() => JsonConvert.SerializeObject(State, Json);

        public void Save()
        {
            State.Clock = Now();
            State.LastSeen = Now();
            store.Save(key, ToJson());
        }

        /// <summary>Replace the world with an exported one (e.g. carried over from the web version).</summary>
        public bool Import(string json)
        {
            var st = Parse(json);
            if (st == null) return false;
            State = st;
            uids.Clear();
            foreach (var r in st.Fish) uids[r.Uid] = r;
            store.Save(key, ToJson());
            return true;
        }

        /// <summary>Move every timestamp of a saved world by d milliseconds.</summary>
        static void ShiftTime(WorldState st, double d)
        {
            st.Created += d; st.LastSeen += d;
            if (st.Clock.HasValue) st.Clock += d;
            foreach (var r in st.Fish) { r.Born += d; if (r.NextBrood.HasValue) r.NextBrood += d; }
            foreach (var h in st.History) h.T += d;
            foreach (var k in st.Marks.Keys.ToList()) st.Marks[k] += d;
            foreach (var p in st.Plants.Values.Concat(st.ExtraPlants))
            {
                p.Planted += d;
                if (p.NextRunner.HasValue) p.NextRunner += d;
            }
            st.Feeder.Last += d;
        }

        /// <summary>A new world, or one carried over from the first version (names, personalities, secrets).</summary>
        WorldState Create(JObject old)
        {
            double t = start;   // the world clock is not set up yet
            var st = new WorldState
            {
                Salt = old?.Value<string>("salt") ?? RandomSalt(),
                Created = t, LastSeen = t, Visits = old?.Value<int?>("visits") ?? 0,
                Secrets = old?["secrets"]?.ToObject<List<string>>() ?? new List<string>(),
                Feeder = new Feeder { Last = t },
            };
            State = st;
            var names = old?["names"] as JObject;
            foreach (var sp in Species.All)
            {
                for (int i = 0; i < sp.Count; i++)
                {
                    string id = $"{sp.Id}-{i}";
                    // the same seed as before, so carried-over fish keep their personality and size
                    var r = new Mulberry32(Rng.HashStr(st.Salt + id));
                    var traits = new Dictionary<string, double>();
                    foreach (var (k, v) in sp.Traits) traits[k] = Rng.Clamp(v + (r.Next() - 0.5) * 0.55, 0, 1);
                    double size = 0.85 + r.Next() * 0.3;
                    var L = sp.Life;
                    // founders are grown up; in a brand new aquarium a few are still youngsters
                    bool young = old == null && Rand.Next() < 0.25;
                    double age = young ? Rand.Range(L.Juvenile, L.Adult * 0.9) : Rand.Range(L.Adult + 1, Math.Min(L.Span * 0.5, L.Adult + 25));
                    st.Fish.Add(new FishRecord
                    {
                        Uid = st.NextUid++, Sp = sp.Id, Name = names?.Value<string>(id),
                        Sex = L.Breed != null ? (i % 2 == 1 ? "m" : "f") : "m",
                        Born = t - age * Day, Traits = traits,
                        Gene = new Gene { Size = size, Variant = sp.Variants > 0 ? i % sp.Variants : 0 },
                    });
                }
            }
            foreach (var r in st.Fish) if (r.Name == null) r.Name = TakeName();
            Log("start", old != null
                ? $"Начата история аквариума: здесь живут {Words.FishWord(st.Fish.Count)}"
                : $"Аквариум запущен: {Words.FishWord(st.Fish.Count)} и первые растения", t);
            return st;
        }

        string RandomSalt()
        {
            const string abc = "0123456789abcdefghijklmnopqrstuvwxyz";
            var c = new char[11];
            for (int i = 0; i < c.Length; i++) c[i] = abc[Rand.Index(abc.Length)];
            return new string(c);
        }

        public List<FishRecord> Fish => State.Fish;
        public FishRecord ByUid(int uid) => uids.TryGetValue(uid, out var r) ? r : null;
        public int Count(string sp) => State.Fish.Count(r => r.Sp == sp);

        public string TakeName()
        {
            var used = new HashSet<string>(State.Fish.Select(r => r.Name));
            var free = Species.Names.Where(n => !used.Contains(n)).ToList();
            if (free.Count > 0) return Rand.Pick(free);
            foreach (var suffix in new[] { "II", "III", "IV", "V", "VI" })
            {
                var free2 = Species.Names.Where(n => !used.Contains($"{n} {suffix}")).ToList();
                if (free2.Count > 0) return $"{Rand.Pick(free2)} {suffix}";
            }
            return $"Рыбка {State.NextUid}";
        }

        /// <summary>A history entry that stays with the aquarium (the last 400 are kept).</summary>
        public void Log(string kind, string text, double? t = null)
        {
            var h = State.History;
            var e = new HistoryEntry { T = t ?? Now(), Kind = kind, Text = text };
            // keep the list in time order (stable: equal times stay in the order they were logged)
            int i = h.Count;
            while (i > 0 && h[i - 1].T > e.T) i--;
            h.Insert(i, e);
            if (h.Count > 400) h.RemoveRange(0, h.Count - 400);
        }

        /// <summary>Remember that something happened once; false if it already had.</summary>
        public bool Mark(string id)
        {
            if (State.Marks.ContainsKey(id)) return false;
            State.Marks[id] = Now();
            return true;
        }

        public bool Marked(string id) => State.Marks.ContainsKey(id);

        public FishRecord Add(FishRecord rec)
        {
            rec.Uid = State.NextUid++;
            State.Fish.Add(rec);
            uids[rec.Uid] = rec;
            return rec;
        }

        public void Remove(FishRecord rec)
        {
            State.Fish.Remove(rec);
            uids.Remove(rec.Uid);
            foreach (var r in State.Fish)
                if (r.Mate == rec.Uid) { r.Mate = null; r.NextBrood = null; }
        }

        /// <summary>A fish bought or brought in by the user: a youngster of the given species.</summary>
        public FishRecord Newcomer(string spId, double? at = null)
        {
            double t = at ?? Now();
            var sp = Species.Get(spId);
            var traits = new Dictionary<string, double>();
            foreach (var (k, v) in sp.Traits) traits[k] = Rng.Clamp(v + (Rand.Next() - 0.5) * 0.55, 0, 1);
            return Add(new FishRecord
            {
                Sp = spId, Name = TakeName(), Sex = sp.Life.Breed != null ? (Rand.Next() < 0.5 ? "f" : "m") : "m",
                Born = t - Rand.Range(sp.Life.Juvenile, sp.Life.Adult * 0.8) * Day, Traits = traits,
                Gene = new Gene { Size = Rand.Range(0.85, 1.15), Variant = sp.Variants > 0 ? Rand.Index(sp.Variants) : 0 },
            });
        }
    }
}
