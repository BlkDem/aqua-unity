using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Aquarium.Life.Tests
{
    /// <summary>
    /// The living world. Fixtures/ hold data produced by the web version itself (see
    /// Tools/web-reference/harness.mjs): RNG values, a save after 30 days, a real browser save
    /// with plants and friendships, and statistics of 60 offline days over 60 runs.
    /// </summary>
    public class LifeTests
    {
        const double T0 = 1_800_000_000_000;   // a fixed "now" for deterministic worlds
        const double Day = World.Day, Hour = World.Hour;

        static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(UnityEngine.Application.dataPath, "Aquarium/Tests/EditMode/Fixtures", name));

        static World NewWorld(int seed, MemoryWorldStore store = null, WorldOptions o = null)
        {
            o ??= new WorldOptions();
            o.Rng ??= new SystemRng(seed);
            o.RealNow ??= () => T0;
            return new World(store ?? new MemoryWorldStore(), o);
        }

        // ------------------------------------------------------------- compatibility with the web version

        [Test]
        public void RngMatchesWebVersion()
        {
            var j = JObject.Parse(Fixture("rng.json"));
            foreach (var p in ((JObject)j["hash"]).Properties())
                Assert.AreEqual((uint)p.Value, Rng.HashStr(p.Name), $"hashStr(\"{p.Name}\")");
            var r = new Mulberry32(Rng.HashStr("k3x9neon-0"));
            foreach (var v in j["seq"]) Assert.AreEqual((double)v, r.Next(), 1e-15);
        }

        [Test]
        public void FoundersFromTheSameSaltMatchTheWebSave()
        {
            // a world carried over from the old format keeps its salt: founders get the same seeded
            // personality and size as in the web version's save made from that salt
            var web = World.Parse(Fixture("web-save.json"));
            var store = new MemoryWorldStore();
            store.Save(World.OldKey, new JObject { ["salt"] = web.Salt }.ToString());
            var w = NewWorld(1, store);
            int compared = 0;
            foreach (var f in web.Fish.Where(f => f.Gen == 1))
            {
                var mine = w.ByUid(f.Uid);
                Assert.AreEqual(f.Sp, mine.Sp);
                Assert.AreEqual(f.Gene.Size, mine.Gene.Size, 1e-12, $"size of founder {f.Uid}");
                foreach (var kv in f.Traits) Assert.AreEqual(kv.Value, mine.Traits[kv.Key], 1e-12, $"{kv.Key} of founder {f.Uid}");
                compared++;
            }
            Assert.Greater(compared, 40);
        }

        [Test]
        public void ReadsBrowserSaveAndWritesItBackUnchanged()
        {
            string json = Fixture("browser-save.json");
            var st = World.Parse(json);
            Assert.NotNull(st);
            Assert.AreEqual(67, st.Fish.Count);
            Assert.AreEqual(27, st.Plants.Count);
            Assert.AreEqual(7, st.ExtraPlants.Count);
            Assert.IsTrue(st.ExtraPlants[0].Extra.ContainsKey("kind"), "unknown plant fields are kept");
            Assert.IsTrue(st.Fish.All(f => f.Fav != null), "favourite spots are kept");

            var store = new MemoryWorldStore();
            store.Save(World.Key, json);
            var w = NewWorld(1, store);
            AssertSameJson(JToken.Parse(json), JToken.Parse(w.ToJson()), "", ignore: new[] { "visits" });
        }

        [Test]
        public void CatchUpOverTwoMonthsMatchesWebStatistics()
        {
            var web = JObject.Parse(Fixture("stats-60d.json"));
            const int runs = 60;
            var acc = new Dictionary<string, List<double>> { ["fish"] = new(), ["births"] = new(), ["kids"] = new(), ["pairs"] = new(), ["farewell"] = new() };
            for (int i = 0; i < runs; i++)
            {
                var w = NewWorld(1000 + i);
                var a = Lifecycle.CatchUp(w, w.Now() + 60 * Day);
                acc["fish"].Add(w.Fish.Count);
                acc["births"].Add(a.Births.Count);
                acc["kids"].Add(a.Births.Sum(e => e.Kids.Count));
                acc["pairs"].Add(a.Pairs.Count);
                acc["farewell"].Add(w.State.History.Count(h => h.Kind == "farewell"));
                Assert.LessOrEqual(w.Fish.Count, Lifecycle.PopulationCap);
            }
            foreach (var kv in acc)
            {
                double mean = kv.Value.Average(), sd = Math.Sqrt(kv.Value.Average(v => (v - mean) * (v - mean)));
                double webMean = (double)web[kv.Key]["mean"], webSd = (double)web[kv.Key]["sd"];
                // difference of two sample means: allow 4 standard errors (and a little slack for tiny spreads)
                double tol = 4 * Math.Sqrt((sd * sd + webSd * webSd) / runs) + 0.3;
                UnityEngine.Debug.Log($"{kv.Key}: unity {mean:F2}±{sd:F2}, web {webMean:F2}±{webSd:F2}");
                Assert.AreEqual(webMean, mean, tol, kv.Key);
            }
        }

        // ------------------------------------------------------------- world

        [Test]
        public void NewAquariumHasAllFounders()
        {
            var w = NewWorld(7);
            Assert.IsTrue(w.Fresh);
            Assert.AreEqual(Species.All.Sum(s => s.Count), w.Fish.Count);
            Assert.AreEqual(w.Fish.Count, w.Fish.Select(f => f.Name).Distinct().Count(), "names are unique");
            CollectionAssert.AreEqual(Enumerable.Range(1, w.Fish.Count), w.Fish.Select(f => f.Uid));
            Assert.AreEqual("start", w.State.History[0].Kind);
            StringAssert.Contains("52 рыбки", w.State.History[0].Text);
            foreach (var f in w.Fish)
            {
                Assert.IsTrue(f.Traits.Values.All(v => v >= 0 && v <= 1));
                Assert.That(f.Gene.Size, Is.InRange(0.85, 1.15));
                if (Species.Get(f.Sp).Life.Breed == null) Assert.AreEqual("m", f.Sex);
            }
            Assert.IsTrue(w.Fish.Where(f => f.Sp == "guppy").Select(f => f.Gene.Variant).Distinct().Count() > 1, "guppies come in colours");
        }

        [Test]
        public void SaveAndLoadKeepTheWorld()
        {
            var store = new MemoryWorldStore();
            var w1 = NewWorld(3, store);
            w1.Fish[0].Name = "Валера";
            w1.Save();
            var w2 = NewWorld(4, store);
            Assert.IsFalse(w2.Fresh);
            Assert.AreEqual("Валера", w2.Fish[0].Name);
            Assert.AreEqual(w1.Fish.Count, w2.Fish.Count);
            Assert.AreEqual(2, w2.State.Visits);
        }

        [Test]
        public void FriendshipsSurviveASave()
        {
            var store = new MemoryWorldStore();
            var w = NewWorld(6, store);
            w.Fish[0].Friends["17"] = 0.64;
            w.Fish[0].Fav = "Замок";
            w.Save();
            var back = NewWorld(6, store).Fish[0];
            Assert.AreEqual(0.64, back.Friend(17), 1e-12);
            Assert.AreEqual(0, back.Friend(18));
            Assert.AreEqual("Замок", back.Fav);
        }

        [Test]
        public void ImportRejectsGarbageAndAcceptsAWebSave()
        {
            var w = NewWorld(5);
            Assert.IsFalse(w.Import("not json"));
            Assert.IsFalse(w.Import("{\"v\":1}"));
            Assert.IsTrue(w.Import(Fixture("browser-save.json")));
            Assert.AreEqual(67, w.Fish.Count);
            Assert.NotNull(w.ByUid(w.Fish[5].Uid));
        }

        [Test]
        public void DebugAwayMovesANewAquariumIntoThePast()
        {
            var store = new MemoryWorldStore();
            var w = NewWorld(9, store, new WorldOptions { AwayHours = 48 });
            Assert.AreEqual(w.Now() - 48 * Hour, w.State.LastSeen, 1);
            var a = Lifecycle.CatchUp(w, w.Now());
            Assert.NotNull(a);
            Assert.AreEqual(48 * Hour, a.Away, 1);
            w.Save();
            Assert.NotNull(store.Load(World.DebugKey), "debug runs keep their own save");
            Assert.IsNull(store.Load(World.Key), "the real save is untouched");
        }

        [Test]
        public void TimeScaleSpeedsUpTheClock()
        {
            double real = T0;
            var w = NewWorld(2, null, new WorldOptions { TimeScale = 600, RealNow = () => real });
            double before = w.Now();
            real += 1000;
            Assert.AreEqual(600_000, w.Now() - before, 1e-6);
        }

        [Test]
        public void HistoryStaysInOrderAndKeepsTheLast400()
        {
            var w = NewWorld(11);
            for (int i = 0; i < 500; i++) w.Log("test", $"e{i}", T0 + (i % 2 == 0 ? i : -i) * 1000);
            var h = w.State.History;
            Assert.AreEqual(400, h.Count);
            for (int i = 1; i < h.Count; i++) Assert.LessOrEqual(h[i - 1].T, h[i].T);
        }

        [Test]
        public void MarksHappenOnce()
        {
            var w = NewWorld(12);
            Assert.IsTrue(w.Mark("x"));
            Assert.IsFalse(w.Mark("x"));
        }

        [Test]
        public void NamesRunOutGracefully()
        {
            var w = NewWorld(13);
            var names = new HashSet<string>(w.Fish.Select(f => f.Name));
            for (int i = 0; i < 300; i++) Assert.IsTrue(names.Add(w.Newcomer("neon").Name), "every name is new");
        }

        // ------------------------------------------------------------- lifecycle

        [Test]
        public void StagesAndGrowthFollowAge()
        {
            var r = new FishRecord { Sp = "neon", Born = T0 };   // juvenile at 1 day, adult at 3, span 90
            Assert.AreEqual(Stage.Fry, Lifecycle.StageOf(r, T0 + 0.5 * Day));
            Assert.AreEqual(Stage.Juvenile, Lifecycle.StageOf(r, T0 + 2 * Day));
            Assert.AreEqual(Stage.Adult, Lifecycle.StageOf(r, T0 + 10 * Day));
            Assert.AreEqual(Stage.Elder, Lifecycle.StageOf(r, T0 + 70 * Day));
            Assert.AreEqual(0.3, Lifecycle.GrowthOf(r, T0), 1e-9);
            Assert.AreEqual(1.0, Lifecycle.GrowthOf(r, T0 + 3 * Day), 1e-9);
            Assert.Greater(Lifecycle.GrowthOf(r, T0 + 1.5 * Day) - 0.3, (1.0 - 0.3) / 2, "grows quickly at first");
        }

        [Test]
        public void FirstPairFormsWithinTheFirstHours()
        {
            var w = NewWorld(21);
            var ev = new List<LifeEvent>();
            double step = 5 * 60000;
            for (double t = T0; t < T0 + 3 * Hour && !w.Marked("first-pair"); t += step) Lifecycle.LifeTick(w, t, step / Day, false, ev);
            Assert.IsTrue(w.Marked("first-pair"));
            var p = ev.First(e => e.Type == LifeEventType.Pair);
            Assert.AreEqual(p.M.Uid, p.F.Mate);
            Assert.AreEqual(p.F.Uid, p.M.Mate);
            Assert.That(p.F.NextBrood.Value - p.T, Is.InRange(0.75 * Hour, 1.5 * Hour), "the first brood comes soon");
        }

        [Test]
        public void LiveTickReportsDueBroodsInsteadOfBirths()
        {
            var w = NewWorld(22);
            var f = w.Fish.First(r => r.Sp == "guppy" && r.IsFemale && Lifecycle.StageOf(r, T0) == Stage.Adult);
            var m = w.Fish.First(r => r.Sp == "guppy" && !r.IsFemale);
            Lifecycle.Pair(w, f, m, T0);
            f.NextBrood = T0 - 1;
            int count = w.Fish.Count;
            var ev = new List<LifeEvent>();
            Lifecycle.LifeTick(w, T0, 0, true, ev);
            var due = ev.Single(e => e.Type == LifeEventType.Due);
            Assert.AreSame(f, due.F);
            Assert.That(due.N, Is.InRange(2, 5));
            Assert.IsNull(f.NextBrood, "the scene stages the birth");
            Assert.AreEqual(count, w.Fish.Count, "nobody is born yet");
        }

        [Test]
        public void ChildrenInheritFromBothParents()
        {
            var w = NewWorld(31);
            var mom = w.Fish.First(r => r.Sp == "barb" && r.IsFemale);
            var dad = w.Fish.First(r => r.Sp == "barb" && !r.IsFemale);
            for (int i = 0; i < 200; i++)
            {
                var k = Lifecycle.Inherit(w, mom, dad, T0, "Замок");
                Assert.AreEqual(Math.Max(mom.Gen, dad.Gen) + 1, k.Gen);
                CollectionAssert.AreEqual(new[] { mom.Uid, dad.Uid }, k.Parents);
                Assert.AreEqual("Замок", k.Nursery);
                Assert.AreSame(k, w.ByUid(k.Uid));
                foreach (var key in mom.Traits.Keys)
                {
                    double mid = (mom.Traits[key] + dad.Traits[key]) / 2;
                    Assert.That(k.Traits[key], Is.InRange(Math.Max(0, mid - 0.125), Math.Min(1, mid + 0.125)), key);
                }
                if (k.Gene.Rare == null) Assert.IsTrue(k.Gene.Tint.All(c => c >= 0.72 && c <= 1));
                else CollectionAssert.AreEqual(Lifecycle.Rare[k.Gene.Rare].Tint, k.Gene.Tint);
            }
        }

        [Test]
        public void RareColoursShowUpAndRunInFamilies()
        {
            var w = NewWorld(32);
            var mom = w.Fish.First(r => r.Sp == "neon" && r.IsFemale);
            var dad = w.Fish.First(r => r.Sp == "neon" && !r.IsFemale);
            int rare = Enumerable.Range(0, 2000).Count(_ => Lifecycle.Inherit(w, mom, dad, T0, null).Gene.Rare != null);
            Assert.That(rare / 2000.0, Is.InRange(0.01, 0.045), "about 2.5% out of the blue");
            mom.Gene.Rare = dad.Gene.Rare = "gold";
            int gold = Enumerable.Range(0, 2000).Count(_ => Lifecycle.Inherit(w, mom, dad, T0, null).Gene.Rare == "gold");
            Assert.That(gold / 2000.0, Is.InRange(0.4, 0.6), "about half when both parents have it");
        }

        [Test]
        public void RemovingAFishFreesItsMate()
        {
            var w = NewWorld(41);
            var f = w.Fish.First(r => r.Sp == "cory" && r.IsFemale);
            var m = w.Fish.First(r => r.Sp == "cory" && !r.IsFemale);
            Lifecycle.Pair(w, f, m, T0);
            w.Remove(m);
            Assert.IsNull(f.Mate);
            Assert.IsNull(f.NextBrood);
            Assert.IsNull(w.ByUid(m.Uid));
        }

        [Test]
        public void ShortAbsenceNeedsNoSummary()
        {
            var w = NewWorld(51);
            Assert.IsNull(Lifecycle.CatchUp(w, w.State.LastSeen + 10 * 60000));
            Assert.NotNull(Lifecycle.CatchUp(w, w.State.LastSeen + 30 * 60000));
        }

        [Test]
        public void SpeciesCapsAreRespected()
        {
            var w = NewWorld(61);
            Lifecycle.CatchUp(w, w.Now() + 60 * Day);
            foreach (var sp in Species.All.Where(s => s.Life.Breed != null))
                Assert.LessOrEqual(w.Count(sp.Id), Math.Max(sp.Life.Breed.Cap, sp.Count), sp.Id);
        }

        // ------------------------------------------------------------- helpers

        /// <summary>Same JSON by meaning: numbers by value (1 == 1.0), a null field == a missing one.</summary>
        static void AssertSameJson(JToken a, JToken b, string path, string[] ignore)
        {
            if (a is JObject oa && b is JObject ob)
            {
                var keys = oa.Properties().Select(p => p.Name).Union(ob.Properties().Select(p => p.Name));
                foreach (var k in keys)
                {
                    if (ignore.Contains(k)) continue;
                    var va = oa[k] ?? JValue.CreateNull();
                    var vb = ob[k] ?? JValue.CreateNull();
                    AssertSameJson(va, vb, $"{path}.{k}", ignore);
                }
                return;
            }
            if (a is JArray aa && b is JArray ab)
            {
                Assert.AreEqual(aa.Count, ab.Count, $"{path}: length");
                for (int i = 0; i < aa.Count; i++) AssertSameJson(aa[i], ab[i], $"{path}[{i}]", ignore);
                return;
            }
            bool num(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
            if (num(a) && num(b)) { Assert.AreEqual((double)a, (double)b, Math.Abs((double)a) * 1e-15, path); return; }
            Assert.IsTrue(JToken.DeepEquals(a, b), $"{path}: {a} != {b}");
        }
    }
}
