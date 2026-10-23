// The living aquarium on screen (the web version's ecosystem.js): the fish of the saved world,
// their schools, food, the places they know, stories about what happens. Everything here works
// in the web version's coordinates, so the behaviour code carries over as it is; positions are
// converted to Unity only for drawing (WebSpace.P).
using System;
using System.Collections.Generic;
using System.Linq;
using Aquarium.Life;
using Aquarium.World;
using UnityEngine;
using Terrain = Aquarium.World.Terrain;

namespace Aquarium.Fish
{
    public sealed class Place
    {
        public string name, kind;
        public Vector3 pos;
        public float radius;
    }

    public sealed class Story
    {
        public string kind, text;
        public Vector3? pos;
        public Fish focus;
        public bool rare;
        public float t;
    }

    public sealed class SchoolState
    {
        public FishSpecies sp;
        public Vector3 target, goal;
        public float next, speed = 0.8f, raceT;
        public string mode = "roam";
    }

    public static class Rnd
    {
        public static float R() => UnityEngine.Random.value;
        public static float R(float a, float b) => a + UnityEngine.Random.value * (b - a);
        public static T Pick<T>(IList<T> list) => list.Count == 0 ? default : list[Mathf.Min(list.Count - 1, (int)(UnityEngine.Random.value * list.Count))];
        public static bool Chance(float p) => UnityEngine.Random.value < p;
    }

    public sealed class Ecosystem
    {
        public readonly Aquarium.Life.World world;
        public readonly List<Fish> fish = new List<Fish>();
        public readonly List<FoodItem> food = new List<FoodItem>();
        public readonly List<Place> spots = new List<Place>();
        public readonly List<Place> clumps = new List<Place>();
        public readonly List<Obstacle> colliders = new List<Obstacle>();
        public readonly Dictionary<string, SchoolState> schools = new Dictionary<string, SchoolState>();
        public readonly Dictionary<int, Fish> byId = new Dictionary<int, Fish>();
        public readonly Dictionary<int, Fish> byUid = new Dictionary<int, Fish>();
        public readonly List<Story> storyLog = new List<Story>();
        public readonly List<BubbleSource> bubbleSources = new List<BubbleSource>();
        public readonly Bubbles bubbles = new Bubbles();
        public BubbleSource aerator, filterSrc, diverSrc, chestSrc, sandSrc;
        float nextDiver = 8;
        public event Action<Story> OnStory;
        public event Action<Fish> OnFish;

        public float time, night;
        public int nextId = 1;
        public Vector3 filterOutlet, cameraPos = new Vector3(0, 3, 14);
        public (bool active, Vector3 point) cursor;
        public (float x, float z, float t)? lastFeed;
        public Fish moonfish;

        readonly Dictionary<string, float> cool = new Dictionary<string, float>();
        float lastStoryT = -99;
        readonly List<(float t, Action fn)> timers = new List<(float, Action)>();

        /// <summary>Unity position -> the web version's coordinates.</summary>
        public static Vector3 Web(Vector3 u) => new Vector3(-u.x, u.y, u.z);

        readonly AquariumLayout layout;

        public Ecosystem(Aquarium.Life.World world, AquariumLayout layout)
        {
            this.world = world;
            this.layout = layout;
            foreach (var s in layout.spots) spots.Add(new Place { name = s.name, kind = s.kind, pos = Web(s.position), radius = s.radius });
            foreach (var c in layout.clumps) clumps.Add(new Place { name = c.name, kind = "plants", pos = Web(c.position), radius = c.radius });
            spots.AddRange(clumps);
            foreach (var o in layout.obstacles) colliders.Add(new Obstacle { center = Web(o.center), radius = o.radius });
            filterOutlet = Web(layout.filterOutlet);
            aerator = bubbles.AddSource("аэратора", Web(layout.aerator), 16, 0.008f, 0.03f, 0.05f);
            filterSrc = bubbles.AddSource("фильтра", filterOutlet + new Vector3(-0.1f, -0.05f, 0), 5, 0.005f, 0.014f, 0.04f);
            filterSrc.vel = new Vector3(-0.8f, -0.1f, 0.2f);
            diverSrc = bubbles.AddSource("водолаза", Web(layout.diverHelmet), 0, 0.01f, 0.035f, 0.02f);
            chestSrc = bubbles.AddSource("сундука", Web(layout.chestPosition), 0, 0.01f, 0.05f, 0.15f);
            sandSrc = bubbles.AddSource("песка", Vector3.zero, 0, 0.01f, 0.03f, 0.02f);
            bubbleSources.AddRange(new[] { aerator, diverSrc, filterSrc });

            foreach (var rec in world.Fish) Spawn(rec, null, false);
            // friendships are saved by uid
            foreach (var f in fish)
                foreach (var kv in f.rec.Friends)
                    if (int.TryParse(kv.Key, out int uid) && byUid.TryGetValue(uid, out var o)) f.mem.friends[o.id] = (float)kv.Value;
            foreach (var f in fish) f.Init(null);
            // the rare moonfish only comes out at night
            foreach (var f in fish) if (f.sp.Rare) { f.hidden = true; moonfish = f; }

            foreach (var sp in FishSpecies.All.Values)
                if (sp.school > 0.6f)
                    schools[sp.id] = new SchoolState { sp = sp, target = new Vector3(Rnd.R(-3, 3), (sp.zoneMin + sp.zoneMax) / 2, Rnd.R(-1, 1)) };
        }

        public Fish Spawn(FishRecord rec, Vector3? at, bool init = true)
        {
            var f = new Fish(this, rec, fish.Count);
            fish.Add(f);
            byId[f.id] = f;
            byUid[rec.Uid] = f;
            if (init) f.Init(at);
            OnFish?.Invoke(f);
            return f;
        }

        public float HeightAt(float x, float z) => Terrain.SandHeight(x, z);
        /// <summary>Whatever is underfoot at web (x, z): sand or the top of the decor (food settles there).</summary>
        public float SurfaceAt(float x, float z) => layout ? layout.SurfaceAt(-x, z) : HeightAt(x, z);
        public Place SpotByName(string n) => spots.Find(s => s.name == n);

        public bool Tell(string kind, string text, Vector3? pos = null, float cooldown = 15, Fish focus = null, bool rare = false)
        {
            if (cool.TryGetValue(kind, out var until) && time < until) return false;
            if (!rare && time - lastStoryT < 4.5f) return false;
            cool[kind] = time + cooldown;
            lastStoryT = time;
            var s = new Story { kind = kind, text = text, pos = pos, focus = focus, rare = rare, t = time };
            storyLog.Insert(0, s);
            if (storyLog.Count > 40) storyLog.RemoveAt(storyLog.Count - 1);
            OnStory?.Invoke(s);
            return true;
        }

        public void After(float sec, Action fn) => timers.Add((time + sec, fn));

        // ----------------------------------------------------------- food
        public void Feed(float? x = null, bool auto = false)
        {
            float px = x ?? Rnd.R(-4, 4), pz = Rnd.R(-0.8f, 1.2f);
            string type = world.State.Feeder.Food;
            FoodItem.Drop(food, px, pz, 30, type);
            After(Rnd.R(0.6f, 1.4f), () => FoodItem.Drop(food, px + Rnd.R(-1.2f, 1.2f), pz + Rnd.R(-0.3f, 0.3f), 18, type));
            if (type == "worms") foreach (var f in fish) f.hunger = Mathf.Min(1, f.hunger + 0.3f);   // a treat!
            lastFeed = (px, pz, time);
            foreach (var f in fish)
            {
                if (f.hidden) continue;
                // fish that remember the feeding place react faster
                if (f.mem.foodSpot is Vector3 fs && Vector3.Distance(fs, new Vector3(px, fs.y, pz)) < 2 && Rnd.Chance(0.6f)) f.thinkT = 0;
            }
            Tell("feed", auto
                ? Rnd.Pick(new[] { "Автокормушка: время обеда", "Автокормушка насыпала корм", "По расписанию — обед!" })
                : Rnd.Pick(new[] { "Время обеда! Корм опускается в воду", "Кормление! Все к поверхности", "Сверху посыпался корм…" }),
                new Vector3(px, 5, pz), 2, rare: true);
        }

        public void EatFood(FoodItem f, Fish eater)
        {
            if (f.eaten) return;
            f.eaten = true;
            eater.hunger = Mathf.Max(0, eater.hunger - 0.12f);
            eater.gulp = 1;
            eater.mem.meals++;
            if (lastFeed is var (lx, lz, _))
            {
                var spot = new Vector3(lx, 4.8f, lz);
                eater.mem.foodSpot = eater.mem.foodSpot is Vector3 fs ? Vector3.Lerp(fs, spot, 0.3f) : spot;
            }
            var rivals = f.claim.Where(o => o != eater && o.state == FishState.EAT && o.ctx.food == f && Vector3.Distance(o.pos, f.pos) < 0.7f).ToList();
            if (rivals.Count >= 3) Tell("many", $"Сразу {rivals.Count + 1} рыбки гнались за одним кусочком — повезло {eater.name}", f.pos, 25, eater);
            else if (rivals.Count > 0)
            {
                var r = rivals[0];
                if (eater.size < r.size * 0.6f) Tell("small-steal", $"Маленький {eater.name} утащил корм прямо у {r.name}!", f.pos, 25, eater);
                else Tell("steal", Rnd.Pick(new[] { $"{eater.name} стащил корм прямо из-под носа у {r.name}", $"{eater.name} оказался шустрее, чем {r.name}" }), f.pos, 25, eater);
                eater.Remember(r, "соперничали за корм", -0.03f);
                r.Remember(eater, "украл корм", -0.06f);
            }
            if (eater.mem.meals == 1 && eater.p("gluttony") > 0.8f && Rnd.Chance(0.4f)) Tell("first", $"{eater.name}, как всегда, первым у корма", f.pos, 40, eater);
        }

        // ----------------------------------------------------------- frame
        public void Update(float dt)
        {
            time += dt;
            for (int i = timers.Count - 1; i >= 0; i--)
                if (time >= timers[i].t) { var fn = timers[i].fn; timers.RemoveAt(i); fn(); }
            UpdateSchools(dt);
            foreach (var f in fish) f.Update(dt);
            FoodItem.Update(food, dt, this);
            bubbles.Update(dt, time);
            if (time > nextDiver) { nextDiver = time + Rnd.R(10, 30); diverSrc.burst = Rnd.R(0.15f, 0.4f); }

            // the moonfish comes out at night and goes back into the cave at dawn
            if (moonfish != null)
            {
                if (moonfish.hidden && night > 0.75f && Rnd.Chance(dt * 0.02f))
                {
                    var cave = SpotByName("пещера");
                    moonfish.hidden = false;
                    moonfish.pos = cave.pos;
                    moonfish.SetState(FishState.IDLE);
                    Tell("moonfish", "Из пещеры выплыла светящаяся Лунная рыбка!", cave.pos, 120, moonfish, true);
                }
                else if (!moonfish.hidden && night < 0.3f && moonfish.state != FishState.SCRIPT)
                {
                    var cave = SpotByName("пещера");
                    moonfish.SetState(FishState.SCRIPT, new FishCtx { point = cave.pos, speed = 1.2f, label = "Возвращается в пещеру" }, 20);
                    var m = moonfish;
                    After(8, () => m.hidden = true);
                }
            }
        }

        void UpdateSchools(float dt)
        {
            foreach (var s in schools.Values)
            {
                var sp = s.sp;
                if (s.mode == "race")
                {
                    s.raceT += dt;
                    float a = s.raceT * 0.9f;
                    s.goal = new Vector3(Mathf.Cos(a) * 4.3f, (sp.zoneMin + sp.zoneMax) / 2 + Mathf.Sin(a * 2) * 0.5f, Mathf.Sin(a) * 1.4f);
                    s.target = Vector3.Lerp(s.target, s.goal, Mathf.Min(1, dt * 3));
                    s.speed = 1.9f;
                    continue;
                }
                s.speed += (0.85f - s.speed) * dt;
                if (time > s.next)
                {
                    s.next = time + Rnd.R(4, 11);
                    for (int k = 0; k < 10; k++)
                    {
                        s.goal = new Vector3(Rnd.R(-4.8f, 4.8f), Rnd.R(sp.zoneMin + 0.4f, sp.zoneMax), Rnd.R(-1.6f, 1.7f));
                        if (!colliders.Any(c => Vector3.Distance(c.center, s.goal) < c.radius + 0.5f)) break;
                    }
                    if (night > 0.6f) s.goal.y = Mathf.Min(s.goal.y, 2.2f);
                }
                s.target = Vector3.Lerp(s.target, s.goal, Mathf.Min(1, dt * 0.35f));
            }
        }

        /// <summary>The biggest swimming fish push the plants aside (shader global _AqFish, Unity space).</summary>
        public void PushPlants(Vector4[] into)
        {
            var big = fish.Where(f => !f.hidden).OrderByDescending(f => f.size).Take(into.Length).ToList();
            for (int i = 0; i < into.Length; i++)
            {
                if (i < big.Count)
                {
                    var f = big[i];
                    into[i] = new Vector4(-f.pos.x, f.pos.y, f.pos.z, f.size * 0.6f * Mathf.Min(1.5f, 0.3f + f.vel.magnitude));
                }
                else into[i] = new Vector4(0, -100, 0, 0);
            }
        }

        public void Save()
        {
            foreach (var f in fish)
            {
                f.rec.Fav = f.fav?.name;
                f.rec.Friends = f.mem.friends.Where(kv => kv.Value > 0.1f).OrderByDescending(kv => kv.Value).Take(5)
                    .Where(kv => byId.ContainsKey(kv.Key))
                    .ToDictionary(kv => byId[kv.Key].uid.ToString(), kv => (double)Mathf.Round(kv.Value * 100) / 100);
            }
            world.Save();
        }
    }
}
