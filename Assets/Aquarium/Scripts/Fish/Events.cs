// The life of the aquarium beyond single fish (the web version's ecosystem.js): every few
// seconds the world ages (pairs, broods, fry growing up, milestones for the history, the
// automatic feeder), and at random moments little and rare stories are staged — a neon race,
// a shrimp on the driftwood, a carousel of all fish, a rubber duck on the surface.
using System;
using System.Collections.Generic;
using System.Linq;
using Aquarium.Life;
using Aquarium.World;
using UnityEngine;
using Terrain = Aquarium.World.Terrain;

namespace Aquarium.Fish
{
    public sealed partial class Ecosystem
    {
        /// <summary>Automatic feeding times (hour of the day) for 2, 3 or 4 meals a day.</summary>
        public static readonly Dictionary<int, int[]> FeedTimes = new Dictionary<int, int[]>
        {
            [2] = new[] { 9, 19 }, [3] = new[] { 9, 14, 20 }, [4] = new[] { 8, 12, 16, 20 },
        };
        static readonly int[] SchoolMarks = { 10, 15, 20, 25 };
        const double Day = Aquarium.Life.World.Day;

        float lifeT, nextEvent = Rnd.R(6, 12), nextRare = Rnd.R(50, 90), nextMemory = 30, friendLogT = -1e9f;
        double lastLife;
        int chestOpens;

        // props the stories move: the chest lid (0 closed .. 1 open), the crystal's flash, the duck
        float chestOpen, chestTarget;
        public float crystalFlash;
        (float t, float dur, float z)? duckRun;

        void Schedule(float dt)
        {
            lifeT -= dt;
            if (lifeT <= 0) { lifeT = 4; LifeUpdate(); }
            // random intervals, never periodic
            if (time > nextEvent) { nextEvent = time + Rnd.R(9, 24); MiniEvent(); }
            if (time > nextRare) { nextRare = time + Rnd.R(45, 110); RareEvent(); }
            if (time > nextMemory) { nextMemory = time + Rnd.R(25, 40); MemoryTick(); }

            // the moonfish goes back into the cave at dawn
            foreach (var f in fish)
                if (f.sp.Rare && !f.hidden && night < 0.3f && f.state != FishState.SCRIPT)
                {
                    f.SetState(FishState.SCRIPT, new FishCtx { point = SpotByName("пещера").pos, speed = 1.2f, label = "Возвращается в пещеру" }, 20);
                    var m = f;
                    After(8, () => m.hidden = true);
                }
            UpdateProps(dt);
        }

        void UpdateProps(float dt)
        {
            chestOpen += (chestTarget - chestOpen) * Mathf.Min(1, dt * 2.5f);
            if (layout && layout.chestLid) layout.chestLid.localRotation = WebSpace.Rot(-0.12f - chestOpen * 1.6f, 0, 0);
            crystalFlash = Mathf.Max(0, crystalFlash - dt * 0.4f);
            if (duckRun is var (t, dur, z) && layout && layout.duck)
            {
                t += dt;
                var d = layout.duck;
                d.localPosition = WebSpace.P(-6.3f + t / dur * 12.6f, Terrain.WaterY + 0.04f + Mathf.Sin(time * 2) * 0.02f, z + Mathf.Sin(t * 0.3f) * 0.4f);
                d.localRotation = WebSpace.Rot(0, 0, Mathf.Sin(time * 1.7f) * 0.08f);
                duckRun = (t, dur, z);
                if (t > dur) { d.gameObject.SetActive(false); duckRun = null; }
            }
        }

        // ----------------------------------------------------------- the history
        /// <summary>A notable moment: shown now and kept in the aquarium's history.</summary>
        public void Chronicle(string kind, string text, Vector3? pos = null, object focus = null)
        {
            world.Log(kind, text);
            Tell(kind, text, pos, 0, focus, true);
        }

        /// <summary>The user adds a young fish of a species (if there is room for it).</summary>
        public Fish AddFish(string spId)
        {
            var info = Species.Get(spId);
            if (world.Count(spId) >= (info.Life.Breed?.Cap ?? info.Count)) return null;
            var rec = world.Newcomer(spId);
            var f = Spawn(rec, new Vector3(Rnd.R(-3, 3), 4.9f, Rnd.R(-0.5f, 1)));
            f.SetState(FishState.EXPLORE, new FishCtx { spot = Rnd.Pick(spots), point = f.RandomZonePoint() }, 10);
            Chronicle("newcomer", $"В аквариуме новый житель: {info.Title} {f.name}", f.pos, f);
            Save();
            return f;
        }

        public void Secret(string id)
        {
            if (world.State.Secrets.Contains(id)) return;
            world.State.Secrets.Add(id);
            Save();
        }

        // ----------------------------------------------------------- the world ages
        /// <summary>Every few seconds: growth, pairs, broods, milestones, the feeder.</summary>
        void LifeUpdate()
        {
            double t = world.Now();
            foreach (var f in fish)
            {
                var prev = f.stage;
                f.UpdateLife(t);
                if (prev != f.stage) OnStage(f, prev);
            }
            var events = new List<LifeEvent>();
            Lifecycle.LifeTick(world, t, (t - lastLife) / Day, true, events);
            lastLife = t;
            foreach (var e in events)
            {
                byUid.TryGetValue(e.F.Uid, out var f);
                byUid.TryGetValue(e.M.Uid, out var m);
                if (e.Type == LifeEventType.Pair) Tell("pair", $"{e.F.Name} и {e.M.Name} стали парой", f?.pos, 0, f, true);
                if (e.Type == LifeEventType.Due) StartNesting(e, f, m);
            }
            // schools growing, friendships forming, first visits to the cave
            foreach (var kv in Lifecycle.SchoolSizes(world, t))
                foreach (int mk in SchoolMarks)
                    if (kv.Value >= mk && world.Mark($"school-{kv.Key}-{mk}"))
                        Chronicle("school", $"Стая «{Species.Get(kv.Key).Title}» выросла до {Words.FishWord(kv.Value)}", schools.TryGetValue(kv.Key, out var s) ? s.target : (Vector3?)null);
            foreach (var f in fish)
            {
                if (f.hidden) continue;
                // only a lasting friendship makes it into the history, and not too often
                var b = f.BestFriend();
                if (b != null && f.mem.friends[b.id] > 0.8f && time - friendLogT > 600 && world.Mark($"friends-{Math.Min(f.uid, b.uid)}-{Math.Max(f.uid, b.uid)}"))
                {
                    friendLogT = time;
                    Chronicle("friends", $"{f.name} и {b.name} подружились — теперь их часто видят вместе", f.pos, f);
                }
                if (f.mem.discoveries.Contains("пещера") && f.stage != Stage.Fry && world.Mark($"cave-{f.uid}") && Rnd.Chance(0.5f))
                    Chronicle("cave", $"{f.name} впервые заплыл{(f.sex == "f" ? "а" : "")} в пещеру", SpotByName("пещера")?.pos, f);
            }
            if (plants != null && plants.Tick(t) > 0) Tell("plant", "Одно из растений пустило новый побег", null, 60, rare: true);
            FeederCheck(t);
        }

        void OnStage(Fish f, Stage prev)
        {
            var r = f.rec;
            var family = r.ParentNames ?? Array.Empty<string>();
            string brood = $"{r.Sp}-{Math.Round(r.Born / 60000)}", a = f.sex == "f" ? "а" : "";
            if (prev == Stage.Fry && f.stage == Stage.Juvenile && world.Mark($"grew-{brood}"))
                Chronicle("grew", family.Length == 2 ? $"Подросли мальки пары {family[0]} и {family[1]} — теперь они осваивают аквариум" : $"{f.name} подрос{(a == "а" ? "ла" : "")}", f.pos, f);
            else if (prev == Stage.Juvenile && f.stage == Stage.Adult && world.Mark($"adult-{brood}"))
                Chronicle("adult", family.Length == 2 ? $"Повзрослели дети пары {family[0]} и {family[1]}" : $"{f.name} стал{a} взрослой рыбкой", f.pos, f);
            else if (prev == Stage.Adult && f.stage == Stage.Elder)
                Chronicle("elder", $"{f.name} теперь старожил аквариума", f.pos, f);
        }

        /// <summary>A brood is due: the pair picks a sheltered spot, circles over it, then the fry appear.</summary>
        void StartNesting(LifeEvent e, Fish f, Fish m)
        {
            var sp = FishSpecies.All[e.F.Sp];
            var breed = sp.Life.Life.Breed;
            world.Pending[sp.id] = (world.Pending.TryGetValue(sp.id, out int p) ? p : 0) + e.N;
            var near = (f ?? m)?.fav?.pos ?? Vector3.zero;
            var nest = sp.homeKind == "anemone" ? SpotByName("актиния") : null;
            nest ??= spots.Where(s => s.kind == "plants" || s.kind == "hide")
                .OrderBy(s => Vector3.Distance(s.pos, near) + Rnd.R(-1, 1)).First();
            foreach (var q in new[] { f, m })
                if (q != null && !q.hidden) q.SetState(FishState.NEST, new FishCtx { spot = nest, ang = Rnd.R(0, 6.28f), side = q == f ? 1 : -1 }, 40);
            if (f != null)
                Tell("nest", breed.Live ? $"{f.name} ждёт малышей у {nest.name}" : $"{e.F.Name} и {e.M.Name} выбрали место для икры: {nest.name}", nest.pos, 0, f, true);
            After(Rnd.R(28, 40), () =>
            {
                world.Pending[sp.id] -= e.N;
                var kids = Lifecycle.Brood(world, e.F, e.M, world.Now(), e.N, nest.name);
                Fish first = null;
                foreach (var k in kids)
                {
                    var fk = Spawn(k, nest.pos + new Vector3(Rnd.R(-0.25f, 0.25f), Rnd.R(0, 0.2f), Rnd.R(-0.15f, 0.15f)));
                    first ??= fk;
                }
                sandSrc.pos = nest.pos;
                sandSrc.burst = 0.3f;
                Tell("birth", $"{e.F.Name} и {e.M.Name} стали родителями: {Words.FryWord(kids.Count)}!", nest.pos, 0, first, true);
                var rare = kids.FirstOrDefault(k => k.Gene.Rare != null);
                if (rare != null)
                    After(6, () => Tell("rare-color", $"Один из малышей — {rare.Name} — с необычным окрасом: {Lifecycle.Rare[rare.Gene.Rare].Title}", nest.pos, 0,
                        byUid.TryGetValue(rare.Uid, out var rf) ? rf : null, true));
                if (breed.Care)
                    foreach (var q in new[] { f, m })
                        if (q != null) q.guardUntil = world.Now() + Day * 0.5;
                Save();
            });
        }

        /// <summary>Feed by the schedule; a meal missed while the app was closed is served shortly after opening.</summary>
        void FeederCheck(double t)
        {
            var F = world.State.Feeder;
            if (!F.On) return;
            var today = DateTimeOffset.FromUnixTimeMilliseconds((long)t).LocalDateTime.Date;
            double slot = 0;
            foreach (int back in new[] { 0, 1 })
                foreach (int hr in FeedTimes.TryGetValue(F.PerDay, out var hours) ? hours : FeedTimes[3])
                {
                    double at = new DateTimeOffset(today.AddDays(-back).AddHours(hr)).ToUnixTimeMilliseconds();
                    if (at <= t) slot = Math.Max(slot, at);
                }
            if (slot > F.Last)
            {
                F.Last = t;
                Feed(auto: true);
            }
        }

        // ----------------------------------------------------------- scheduled little stories
        Fish PickFish(Func<Fish, bool> filter) => Rnd.Pick(fish.Where(f => !f.hidden && filter(f)).ToList());

        static void RunWeighted(IList<(float w, Action fn)> events)
        {
            float r = Rnd.R() * events.Sum(e => e.w);
            foreach (var (w, fn) in events)
            {
                r -= w;
                if (r <= 0) { fn(); break; }
            }
        }

        static List<T> Shuffled<T>(IEnumerable<T> list) => list.OrderBy(_ => Rnd.R()).ToList();

        void MiniEvent()
        {
            RunWeighted(new (float, Action)[]
            {
                (1 - night, () =>   // neon race
                {
                    if (!schools.TryGetValue("neon", out var s)) return;
                    s.mode = "race"; s.raceT = 0;
                    After(Rnd.R(7, 11), () => s.mode = "roam");
                    Tell("race", Rnd.Pick(new[] { "Неоны устроили гонку!", "Стайка неонов носится наперегонки" }), s.target, 60);
                }),
                (0.9f, () =>   // guppies found a new place
                {
                    var s = Rnd.Pick(spots.Where(q => q.kind != "hide").ToList());
                    foreach (var f in fish)
                        if (f.sp.id == "guppy" && !f.hidden && f.state != FishState.EAT)
                            f.SetState(FishState.EXPLORE, new FishCtx { spot = s, point = f.RandomPointNear(s.pos, 0.8f) }, Rnd.R(10, 16));
                    Tell("guppy", $"Гуппи нашли новое место: {s.name}", s.pos, 60);
                }),
                (0.8f, () =>   // a catfish explores the cave
                {
                    var f = PickFish(q => q.sp.id == "cory");
                    if (f == null) return;
                    f.SetState(FishState.INVESTIGATE, new FishCtx { spot = SpotByName("пещера"), ang = 0, side = 1 }, 12);
                    Tell("cory-cave", $"Сомик {f.name} исследует пещеру", f.pos, 60, f);
                }),
                (0.8f, () =>   // a shrimp climbs the driftwood
                {
                    if (!(Rnd.Pick(critters.OfType<Shrimp>().ToList()) is Shrimp s)) return;
                    var wc = Web(layout.woodCenter);
                    s.StartHop(SurfacePoint(wc.x + Rnd.R(-1, 1), wc.z + Rnd.R(-0.2f, 0.3f)), 0.5f);
                    Tell("shrimp-wood", $"Креветка {s.name} забралась на корягу", wc, 60, s);
                }),
                (0.7f, () =>   // a goldfish chases a bubble
                {
                    var f = PickFish(q => q.sp.id == "goldfish");
                    if (f == null) return;
                    var p = RandomSurfacePoint(true);
                    var b = bubbles.Spawn(p + new Vector3(0, 0.05f, 0), 0.06f, new Vector3(0, 0.05f, 0));
                    if (b == null) return;
                    f.SetState(FishState.SCRIPT, new FishCtx { point = b.pos, speed = 1.1f, label = "Преследует пузырь" }, 7);
                    Tell("gold-bubble", $"Золотая рыбка {f.name} преследует пузырь", p, 60, f);
                }),
                (0.6f, () =>   // a dispute over a shelter
                {
                    var a = PickFish(q => q.sp.school <= 0 && q.size > 0.35f && q.state != FishState.EAT);
                    var b = a == null ? null : PickFish(q => q != a && q.sp.school <= 0 && q.size > 0.35f && q.state != FishState.EAT);
                    if (a == null || b == null) return;
                    var s = Rnd.Pick(spots.Where(q => q.kind == "hide" || q.kind == "cave").ToList());
                    a.SetState(FishState.SCRIPT, new FishCtx { point = s.pos + new Vector3(0.2f, 0.2f, 0.3f), speed = 1, label = $"Плывёт к укрытию ({s.name})" }, 8);
                    b.SetState(FishState.SCRIPT, new FishCtx { point = s.pos + new Vector3(-0.2f, 0.2f, 0.3f), speed = 1, label = $"Плывёт к укрытию ({s.name})" }, 8);
                    After(6, () =>
                    {
                        if (a.state == FishState.SCRIPT && b.state == FishState.SCRIPT && a.Dist(b) < 1.5f)
                        {
                            a.StartStandoff(b);
                            cool["standoff"] = 0;
                            Tell("dispute", $"{a.name} и {b.name} спорят за укрытие", s.pos, 60, a, true);
                        }
                    });
                }),
                (0.7f, () =>   // a snail sets off on a trip
                {
                    if (!(Rnd.Pick(critters.OfType<Snail>().ToList()) is Snail s)) return;
                    s.Journey();
                    Tell("snail-trip", $"Улитка {s.name} отправилась в путешествие", s.pos, 90, s);
                }),
                (0.6f, () =>   // barb mischief
                {
                    var b = PickFish(q => q.sp.id == "barb");
                    var t = PickFish(q => q.sp.id == "angelfish" || q.sp.id == "goldfish" || q.sp.id == "betta");
                    if (b == null || t == null) return;
                    b.SetState(FishState.PLAY, new FishCtx { other = t, role = "chase" }, 6);
                    t.SetState(FishState.PLAY, new FishCtx { other = b, role = "run" }, 6);
                    Tell("mischief", $"Барбус {b.name} дразнит {t.name} — хулиган!", b.pos, 60, b);
                }),
                (0.6f, () =>   // circling a plant
                {
                    var c = Rnd.Pick(clumps);
                    var group = Shuffled(fish.Where(f => !f.hidden && !f.sp.bottom && f.p("playfulness") > 0.4f && f.state != FishState.EAT)).Take((int)Rnd.R(3, 6)).ToList();
                    if (c == null || group.Count < 3) return;
                    var center = c.pos + new Vector3(0, 0.8f, 0);
                    for (int i = 0; i < group.Count; i++)
                        group[i].SetState(FishState.CIRCLE, new FishCtx { center = center, r = c.radius + 0.6f, ang = (float)i / group.Count * Mathf.PI * 2, side = 1 }, Rnd.R(8, 12));
                    Tell("circle-group", $"Несколько рыбок кружат вокруг растения ({c.name})", center, 80, group[0]);
                }),
                (0.5f, () => PickFish(q => q.sp.id == "cory")?.Enter(FishState.AIR)),   // a catfish gulps air
                (0.6f, () => { var f = PickFish(q => q.BestFriend() != null); f?.Enter(FishState.SOCIALIZE, f.BestFriend()); }),
                (0.5f, () => PickFish(q => q.p("playfulness") > 0.6f && !q.sp.bottom)?.Enter(FishState.PLAY)),
                (0.4f, () =>   // the diver burps
                {
                    diverSrc.burst = 0.6f;
                    Tell("diver", "Водолаз выпустил облачко пузырей", diverSrc.pos, 60);
                }),
                (0.5f, () =>   // bubbles out of the sand
                {
                    var p = RandomSurfacePoint(true);
                    sandSrc.pos = p;
                    sandSrc.burst = 0.25f;
                    var f = PickFish(q => q.p("curiosity") > 0.6f && Vector3.Distance(q.pos, p) < 3);
                    if (f == null) return;
                    f.SetState(FishState.SCRIPT, new FishCtx { point = p + new Vector3(0, 0.3f, 0), speed = 0.9f, label = "Изучает странные пузырьки", face = p }, 6);
                    Tell("sand-bubble", $"Из песка вырвались пузырьки — {f.name} спешит разобраться", p, 60, f);
                }),
                (night * 1.4f, () =>   // night owls
                {
                    var f = PickFish(q => q.sp.nocturnal);
                    if (f != null) Tell("night-active", $"Ночью {f.name} особенно активен", f.pos, 120, f);
                }),
            });
        }

        void RareEvent()
        {
            RunWeighted(new (float, Action)[]
            {
                (1, () =>   // a whole school turns at once
                {
                    if (schools.Count == 0) return;
                    var s = Rnd.Pick(schools.Values.ToList());
                    var c = s.target;
                    s.goal = new Vector3(-c.x * 0.8f, c.y, -c.z * 0.5f);
                    s.target = s.goal;
                    s.next = time + 8;
                    foreach (var f in fish)
                        if (f.sp == s.sp && !f.hidden) { f.vel *= 0.3f; f.burst = 0.3f; f.SetState(FishState.SCHOOL); }
                    Tell("school-turn", $"Вся стая «{s.sp.Title}» синхронно развернулась!", c, 200, rare: true);
                }),
                (0.8f, () =>   // a fish watches the viewer
                {
                    var f = PickFish(q => q.p("curiosity") > 0.5f && !q.sp.bottom && q.sp.school < 0.6f);
                    if (f == null) return;
                    f.SetState(FishState.WATCH, new FishCtx { ox = Rnd.R(-1.5f, 1.5f) }, Rnd.R(10, 16));
                    Tell("watcher", $"{f.name} уже долго наблюдает за вами…", f.pos, 240, f, true);
                }),
                (0.8f, () =>   // out of the cave all of a sudden
                {
                    var f = PickFish(q => !q.sp.bottom && q.size < 0.7f && q.sp.school < 0.6f);
                    var cave = SpotByName("пещера");
                    if (f == null || cave == null) return;
                    f.pos = cave.pos + new Vector3(0, 0, -0.25f);
                    f.vel = new Vector3(0, 0.3f, 1.5f);
                    f.heading = new Vector3(0, 0.2f, 1).normalized;
                    f.SetState(FishState.FLEE, new FishCtx { dir = new Vector3(Rnd.R(-0.4f, 0.4f), 0.3f, 1).normalized, prev = FishState.IDLE }, 1.2f);
                    Tell("cave-surprise", $"Неожиданно из пещеры вынырнул {f.name}!", cave.pos, 200, f, true);
                }),
                (0.7f, () =>   // a shrimp in the oddest place
                {
                    if (!(Rnd.Pick(critters.OfType<Shrimp>().ToList()) is Shrimp s)) return;
                    var best = RandomSurfacePoint(false);
                    for (int i = 0; i < 300; i++) { var p = RandomSurfacePoint(false); if (p.y > best.y) best = p; }
                    s.StartHop(best, 1.2f);
                    Tell("shrimp-odd", $"Креветка {s.name} забралась в совершенно неожиданное место", best, 240, s, true);
                }),
                (0.6f, () =>   // a snail heads for the top of the glass
                {
                    if (Rnd.Pick(critters.OfType<Snail>().Where(c => c.mode == "glass").ToList()) is Snail s) { s.goalTop = true; s.reachedTop = false; }
                }),
                (0.6f, () =>   // tag for everyone
                {
                    var players = Shuffled(fish.Where(f => !f.hidden && f.p("playfulness") > 0.5f && !f.sp.bottom && f.size < 0.6f)).Take(6).ToList();
                    for (int i = 0; i + 1 < players.Count; i += 2)
                    {
                        players[i].SetState(FishState.PLAY, new FishCtx { other = players[i + 1], role = "chase" }, 10);
                        players[i + 1].SetState(FishState.PLAY, new FishCtx { other = players[i], role = "run" }, 10);
                    }
                    if (players.Count >= 4) Tell("group-play", "Несколько рыбок затеяли общую игру в догонялки!", players[0].pos, 200, players[0], true);
                }),
                (0.25f, () =>   // the big carousel (a secret)
                {
                    var center = new Vector3(0, 3.0f, 0.1f);
                    var list = fish.Where(f => !f.hidden && !f.sp.bottom).ToList();
                    for (int i = 0; i < list.Count; i++)
                        list[i].SetState(FishState.CAROUSEL, new FishCtx
                        {
                            center = center, r = 3.6f, rx = 3.8f + Rnd.R(-0.4f, 0.4f), rz = 1.5f + Rnd.R(-0.2f, 0.2f), dy = Rnd.R(-1, 1.2f),
                            ang = (float)i / list.Count * Mathf.PI * 2, angSpeed = 0.35f, side = 1,
                        }, Rnd.R(14, 18));
                    Tell("carousel", "✨ Редкое событие: все рыбки закружились в большой карусели!", center, 600, rare: true);
                    Secret("carousel");
                }),
                (0.2f, () => StartDuck()),
                (night > 0.6f ? 2 : 0, () =>   // the moonfish comes out (a secret)
                {
                    var m = fish.Find(f => f.sp.id == "moonfish" && f.hidden);
                    var cave = SpotByName("пещера");
                    if (m == null || cave == null) return;
                    m.hidden = false;
                    m.pos = cave.pos;
                    m.SetState(FishState.EXPLORE, new FishCtx { spot = Rnd.Pick(spots), point = m.RandomZonePoint() }, 14);
                    Tell("moonfish", $"✨ Из пещеры выплыла светящаяся Лунная рыбка — {m.name}", cave.pos, 300, m, true);
                    Secret("moonfish");
                }),
            });
        }

        /// <summary>A rubber duck floats across the surface (a secret); the curious come up to look.</summary>
        public void StartDuck()
        {
            if (!layout || !layout.duck || duckRun != null) return;
            float z = Rnd.R(-1, 1);
            var d = layout.duck;
            d.gameObject.SetActive(true);
            d.localPosition = WebSpace.P(-6.3f, Terrain.WaterY + 0.05f, z);
            duckRun = (0, 45, z);
            Tell("duck", "По поверхности проплывает… резиновая уточка?!", new Vector3(-6.3f, Terrain.WaterY, z), 900, rare: true);
            Secret("duck");
            foreach (var f in fish.Where(q => !q.hidden && q.p("curiosity") > 0.6f).Take(5).ToList())
                After(Rnd.R(2, 8), () =>
                {
                    var at = Web(d.localPosition);
                    f.SetState(FishState.SCRIPT, new FishCtx { point = new Vector3(at.x, 5.0f, at.z), speed = 0.8f, label = "Разглядывает уточку", face = at }, 8);
                });
        }

        /// <summary>Fish come to like a new place when they keep going there; old visits fade.</summary>
        void MemoryTick()
        {
            foreach (var f in fish)
            {
                if (f.hidden || f.fav == null) continue;
                string best = null;
                float bv = 0;
                foreach (var kv in f.mem.visits) if (kv.Value > bv) { bv = kv.Value; best = kv.Key; }
                f.mem.visits.TryGetValue(f.fav.name, out float favV);
                if (best != null && best != f.fav.name && bv > favV + 5 && Rnd.Chance(0.25f) && SpotByName(best) is Place s)
                {
                    f.fav = s;
                    if (f.p("territoriality") < 0.5f) f.home = s;
                    Tell("new-fav", $"{f.name}, похоже, нашёл новое любимое место: {s.name}", s.pos, 90, f);
                }
                foreach (var k in f.mem.visits.Keys.ToList()) f.mem.visits[k] *= 0.97f;
            }
        }

        // ----------------------------------------------------------- things to click
        static readonly string[] BottleNotes =
        {
            "Записка в бутылке: «Если смотреть достаточно долго, рыбки начнут смотреть в ответ»",
            "Записка в бутылке: «Здесь был Капитан. Сокровище — в сундуке. Пароль: пузырь»",
            "Записка в бутылке: «Ночью в пещере что-то светится…»",
            "Записка в бутылке: «Говорят, когда-то здесь проплывала резиновая уточка»",
        };

        /// <summary>A click on one of the secret props: chest, bottle or crystal.</summary>
        public void ClickProp(string kind)
        {
            if (kind == "chest")
            {
                chestTarget = 1;
                chestSrc.burst = 1.2f;
                After(7, () => chestTarget = 0);
                var at = chestSrc.pos;
                foreach (var f in fish.Where(q => !q.hidden && q.p("curiosity") > 0.4f).OrderBy(q => Vector3.Distance(q.pos, at)).Take(4).ToList())
                    After(Rnd.R(0.5f, 2), () => f.SetState(FishState.INVESTIGATE, new FishCtx { spot = SpotByName("сундук"), ang = Rnd.R(0, 6), side = 1 }, Rnd.R(5, 9)));
                chestOpens++;
                Tell("chest", chestOpens == 3 ? "Сундук снова открыт… кажется, рыбки уже привыкли к сокровищам" : "Сундук приоткрылся — из него вырвались пузыри!", at, 3, rare: true);
                Secret("chest");
            }
            else if (kind == "bottle")
            {
                Tell("bottle", Rnd.Pick(BottleNotes), null, 2, rare: true);
                Secret("bottle");
            }
            else if (kind == "crystal")
            {
                crystalFlash = 1;
                Tell("crystal", "Вы нашли секретный кристалл в пещере! Рыбки слетаются посмотреть", Web(layout.secretCave), 5, rare: true);
                foreach (var f in Shuffled(fish.Where(q => !q.hidden)).Take(6))
                    f.SetState(FishState.INVESTIGATE, new FishCtx { spot = SpotByName("пещера"), ang = Rnd.R(0, 6), side = 1 }, Rnd.R(6, 10));
                Secret("crystal");
            }
        }
    }
}
