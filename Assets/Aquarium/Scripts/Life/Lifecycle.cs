// Growth, pairs, broods and inheritance. These rules work on the saved fish records, so the same
// code runs live (the scene stages courtship and birth) and in a quick catch-up for the time the
// aquarium was closed. A port of the web version's lifecycle.js.
using System;
using System.Collections.Generic;
using System.Linq;

namespace Aquarium.Life
{
    public enum Stage { Fry, Juvenile, Adult, Elder }

    public sealed class RareColour
    {
        public string Title;
        public double[] Tint;
    }

    public enum LifeEventType { Pair, Due, Birth, Farewell }

    /// <summary>Something that happened in a life tick.</summary>
    public sealed class LifeEvent
    {
        public LifeEventType Type;
        public double T;
        /// <summary>Female and male of a pair (Pair, Due, Birth).</summary>
        public FishRecord F, M;
        /// <summary>Fry due in a live brood (Due).</summary>
        public int N;
        /// <summary>Newborns (Birth).</summary>
        public List<FishRecord> Kids;
        /// <summary>The fish that passed away (Farewell).</summary>
        public FishRecord R;
    }

    public sealed class SchoolGrowth
    {
        public SpeciesInfo Species;
        public int From, To;
    }

    /// <summary>"While you were away": the pleasant changes since the last visit.</summary>
    public sealed class AwaySummary
    {
        public double Away;
        public List<LifeEvent> Pairs, Births;
        public int GrewJuvenile, GrewAdult;
        public List<SchoolGrowth> Schools;
        /// <summary>Young plants grown from runners meanwhile (filled in by the plants).</summary>
        public int Plants;
    }

    public static class Lifecycle
    {
        /// <summary>All fish together: beyond this the aquarium gets crowded (and slow).</summary>
        public const int PopulationCap = 75;
        const double Day = World.Day, Hour = World.Hour;

        public static double AgeDays(FishRecord r, double t) => (t - r.Born) / Day;

        public static Stage StageOf(FishRecord r, double t)
        {
            var L = Species.Get(r.Sp).Life;
            double a = AgeDays(r, t);
            if (a < L.Juvenile) return Stage.Fry;
            if (a < L.Adult) return Stage.Juvenile;
            return a > L.Span * 0.75 ? Stage.Elder : Stage.Adult;
        }

        /// <summary>Body length relative to an adult: a fry hatches at 30% and grows quickly at first.</summary>
        public static double GrowthOf(FishRecord r, double t)
        {
            double u = Rng.Clamp(AgeDays(r, t) / Species.Get(r.Sp).Life.Adult, 0, 1);
            return 0.3 + 0.7 * (1 - (1 - u) * (1 - u));
        }

        public static string StageTitle(Stage s) => s switch
        {
            Stage.Fry => "малёк",
            Stage.Juvenile => "подросток",
            Stage.Adult => "взрослая рыбка",
            _ => "старожил",
        };

        /// <summary>The web version's stage names ("fry", "juvenile", "adult", "elder").</summary>
        public static string StageKey(Stage s) => s.ToString().ToLowerInvariant();

        /// <summary>Rare colours that show up now and then in a brood (multiplied over the body texture).</summary>
        public static readonly IReadOnlyDictionary<string, RareColour> Rare = new Dictionary<string, RareColour>
        {
            ["gold"] = new RareColour { Title = "золотистый окрас", Tint = new[] { 1.0, 0.8, 0.42 } },
            ["pearl"] = new RareColour { Title = "жемчужный окрас", Tint = new[] { 0.9, 0.94, 1.0 } },
            ["sapphire"] = new RareColour { Title = "сапфировый окрас", Tint = new[] { 0.62, 0.74, 1.0 } },
            ["rose"] = new RareColour { Title = "розовый окрас", Tint = new[] { 1.0, 0.72, 0.84 } },
        };
        static readonly string[] RareKeys = { "gold", "pearl", "sapphire", "rose" };

        static double Vary(IRng r, double a, double b, double j) => (a + b) / 2 + (r.Next() - 0.5) * j;

        /// <summary>A child record: looks and character are a mix of the parents with a little variation.</summary>
        public static FishRecord Inherit(World world, FishRecord mom, FishRecord dad, double t, string nursery)
        {
            var R = world.Rand;
            var sp = Species.Get(mom.Sp);
            var traits = new Dictionary<string, double>();
            foreach (var k in mom.Traits.Keys)
                traits[k] = Rng.Clamp(Vary(R, mom.Traits[k], dad.Traits.TryGetValue(k, out var d) ? d : mom.Traits[k], 0.25), 0, 1);
            int variant = R.Pick(new[] { mom, dad }).Gene.Variant;
            string rare = null;
            if (sp.Variants > 1 && R.Next() < 0.05) variant = R.Index(sp.Variants);
            if (R.Next() < 0.025) rare = R.Pick(RareKeys);
            else if (mom.Gene.Rare != null && mom.Gene.Rare == dad.Gene.Rare && R.Next() < 0.5) rare = mom.Gene.Rare;
            else if ((mom.Gene.Rare ?? dad.Gene.Rare) != null && R.Next() < 0.2) rare = mom.Gene.Rare ?? dad.Gene.Rare;
            var tint = rare != null
                ? (double[])Rare[rare].Tint.Clone()
                : mom.Gene.Tint.Select((v, i) => Rng.Clamp(Vary(R, v, dad.Gene.Tint[i], 0.07), 0.72, 1)).ToArray();
            return world.Add(new FishRecord
            {
                Sp = sp.Id, Name = world.TakeName(), Sex = R.Next() < 0.5 ? "f" : "m", Born = t, Traits = traits,
                Gene = new Gene { Size = Rng.Clamp(Vary(R, mom.Gene.Size, dad.Gene.Size, 0.1), 0.8, 1.25), Tint = tint, Variant = variant, Rare = rare },
                Parents = new[] { mom.Uid, dad.Uid }, ParentNames = new[] { mom.Name, dad.Name },
                Gen = Math.Max(Math.Max(mom.Gen, 1), Math.Max(dad.Gen, 1)) + 1, Nursery = nursery,
            });
        }

        public static void Pair(World world, FishRecord f, FishRecord m, double t)
        {
            var B = Species.Get(f.Sp).Life.Breed;
            f.Mate = m.Uid; m.Mate = f.Uid;
            // the very first pair of a new aquarium gets on with it, so there is something to discover early
            bool first = world.Mark("first-pair");
            f.NextBrood = t + (first ? world.Rand.Range(0.75, 1.5) * Hour : B.Every * world.Rand.Range(0.4, 0.7) * Day);
            world.Log("pair", $"{f.Name} и {m.Name} стали парой ({Species.Get(f.Sp).Title})", t);
        }

        public static List<FishRecord> Brood(World world, FishRecord f, FishRecord m, double t, int n, string nursery)
        {
            var sp = Species.Get(f.Sp);
            var kids = new List<FishRecord>();
            for (int i = 0; i < n; i++) kids.Add(Inherit(world, f, m, t, nursery));
            f.NextBrood = t + sp.Life.Breed.Every * world.Rand.Range(0.8, 1.25) * Day;
            world.Log("birth", $"{f.Name} и {m.Name} стали родителями: {Words.FryWord(n)} ({sp.Title})", t);
            foreach (var k in kids)
                if (k.Gene.Rare != null)
                    world.Log("rare", $"В новом выводке {f.Name} и {m.Name} — {k.Name}: у {(k.IsFemale ? "неё" : "него")} {Rare[k.Gene.Rare].Title}!", t);
            return kids;
        }

        /// <summary>
        /// One step of family life over dtDays. Live, a due brood is reported (Due) and the scene stages
        /// nesting and calls Brood itself; in catch-up it happens right away.
        /// </summary>
        public static void LifeTick(World world, double t, double dtDays, bool live, List<LifeEvent> events)
        {
            var R = world.Rand;
            var bySp = new Dictionary<string, List<FishRecord>>();
            var order = new List<string>();
            foreach (var r in world.Fish)
            {
                if (!bySp.TryGetValue(r.Sp, out var l)) { bySp[r.Sp] = l = new List<FishRecord>(); order.Add(r.Sp); }
                l.Add(r);
            }
            int pendingAll = world.Pending.Values.Sum();
            int roomAll = PopulationCap - world.Fish.Count - pendingAll;
            foreach (var id in order)
            {
                var list = bySp[id];
                var B = Species.Get(id).Life.Breed;
                if (B == null) continue;
                world.Pending.TryGetValue(id, out int pending);
                int room = Math.Min(roomAll, B.Cap - list.Count - pending);
                // pairing: now and then a single female and a male (one she already likes, preferably)
                // get together; the very first pair of an aquarium forms within its first hour or so
                if (room > 0)
                {
                    var singles = list.Where(r => r.Mate == null && StageOf(r, t) == Stage.Adult).ToList();
                    var males = singles.Where(r => !r.IsFemale).ToList();
                    foreach (var f in singles)
                    {
                        if (!f.IsFemale || males.Count == 0) continue;
                        bool eager = !world.Marked("first-pair");
                        bool liked = males.Any(m => f.Friend(m.Uid) > 0.3);
                        if (R.Next() > (eager ? 12 : liked ? 0.2 : 0.1) * dtDays) continue;
                        // the one she likes most, with a little chance
                        var m = males.Select(x => (x, k: f.Friend(x.Uid) + R.Range(-0.1, 0.1))).OrderByDescending(p => p.k).First().x;
                        males.Remove(m);
                        Pair(world, f, m, t);
                        events.Add(new LifeEvent { Type = LifeEventType.Pair, F = f, M = m, T = t });
                    }
                }
                // broods
                foreach (var f in list)
                {
                    if (!f.IsFemale || f.Mate == null || f.NextBrood == null || f.NextBrood > t) continue;
                    var m = world.ByUid(f.Mate.Value);
                    if (m == null) { f.Mate = null; f.NextBrood = null; continue; }
                    if (room <= 0) { f.NextBrood = t + B.Every * R.Range(0.8, 1.2) * Day; continue; }
                    int n = (int)Math.Min(room, Rng.Round(R.Range(B.BroodMin, B.BroodMax)));
                    room -= n;
                    roomAll -= n;
                    if (live)
                    {
                        f.NextBrood = null;   // the scene takes it from here
                        events.Add(new LifeEvent { Type = LifeEventType.Due, F = f, M = m, N = n, T = t });
                    }
                    else
                    {
                        // unseen broods grow up around the mother's favourite place
                        events.Add(new LifeEvent { Type = LifeEventType.Birth, F = f, M = m, Kids = Brood(world, f, m, t, n, f.Fav), T = t });
                    }
                }
            }
            // a very old fish may quietly reach the end of a long life (only ever while nobody watches)
            if (!live)
            {
                foreach (var r in world.Fish.ToList())
                {
                    var L = Species.Get(r.Sp).Life;
                    double age = AgeDays(r, t);
                    if (age > L.Span * (1 + (r.Uid % 7) * 0.05) && R.Next() < 0.15 * dtDays)
                    {
                        world.Remove(r);
                        world.Log("farewell", $"{r.Name} {(r.IsFemale ? "прожила" : "прожил")} долгую и спокойную жизнь — {Rng.Round(age)} дней", t);
                        events.Add(new LifeEvent { Type = LifeEventType.Farewell, R = r, T = t });
                    }
                }
            }
        }

        /// <summary>
        /// Catch up with the time the aquarium was closed and sum up the pleasant changes for
        /// "while you were away". Null after a short absence (under 20 minutes).
        /// </summary>
        public static AwaySummary CatchUp(World world, double t1)
        {
            double t0 = world.State.LastSeen;
            if (t1 - t0 < 20 * 60000) return null;
            var stage0 = world.Fish.ToDictionary(r => r.Uid, r => StageOf(r, t0));
            var school0 = SchoolSizes(world, t0);
            double span = Math.Min(t1 - t0, 60 * Day);
            double step = span > 10 * Day ? 6 * Hour : Hour;
            var events = new List<LifeEvent>();
            for (double t = t1 - span + step; t <= t1; t += step) LifeTick(world, t, step / Day, false, events);
            var s = new AwaySummary
            {
                Away = t1 - t0,
                Pairs = events.Where(e => e.Type == LifeEventType.Pair).ToList(),
                Births = events.Where(e => e.Type == LifeEventType.Birth).ToList(),
                Schools = new List<SchoolGrowth>(),
            };
            foreach (var r in world.Fish)
            {
                if (!stage0.TryGetValue(r.Uid, out var a)) continue;
                var b = StageOf(r, t1);
                if (a == Stage.Fry && b != Stage.Fry) s.GrewJuvenile++;
                if (a == Stage.Juvenile && (b == Stage.Adult || b == Stage.Elder)) s.GrewAdult++;
            }
            foreach (var kv in SchoolSizes(world, t1))
            {
                school0.TryGetValue(kv.Key, out int from);
                if (kv.Value > from) s.Schools.Add(new SchoolGrowth { Species = Species.Get(kv.Key), From = from, To = kv.Value });
            }
            return s;
        }

        /// <summary>Grown-up members of each schooling species (fry keep to themselves), in order of appearance.</summary>
        public static Dictionary<string, int> SchoolSizes(World world, double t)
        {
            var n = new Dictionary<string, int>();
            foreach (var r in world.Fish)
            {
                if (Species.Get(r.Sp).School > 0.6 && StageOf(r, t) != Stage.Fry)
                    n[r.Sp] = (n.TryGetValue(r.Sp, out int c) ? c : 0) + 1;
            }
            return n;
        }
    }
}
