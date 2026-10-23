// A fish: mind (states chosen by character, needs and company), steering, swimming physics and
// the body animation. A port of the web version's fish.js, in its coordinates (see Ecosystem).
using System.Collections.Generic;
using System.Linq;
using Aquarium.Life;
using UnityEngine;

namespace Aquarium.Fish
{
    public enum FishState
    {
        IDLE, SCHOOL, CRUISE, EXPLORE, RETURN, REST, HIDE, SOCIALIZE, FOLLOW, PLAY, BUBBLES, WAIT_FOOD, PATROL, DEFEND,
        STANDOFF, EAT, FLEE, CURIOUS, WATCH, INVESTIGATE, CIRCLE, CAROUSEL, AIR, SCRIPT, NURSERY, COURT, NEST, GUARD,
    }

    /// <summary>What the current state is about (the web version keeps this in a loose object).</summary>
    public sealed class FishCtx
    {
        public FoodItem food;
        public Vector3? point, face;
        public Place spot;
        public Fish other, who, rival, target;
        public float side, ang, peek, r, angSpeed, rx, rz, dy, x, z, speed = 1, ox;
        public float? pause;
        public int tags, phase;
        public string role, label;
        public bool done, peeked, follow;
        public Vector3 dir, center;
        public FishState? prev;
        public BubbleSource src;
    }

    public sealed class FishMemory
    {
        public readonly Dictionary<int, float> friends = new Dictionary<int, float>();
        public (string who, string kind, float t)? last;
        public Vector3? foodSpot;
        public readonly Dictionary<string, float> visits = new Dictionary<string, float>();
        public int meals;
        public readonly HashSet<string> discoveries = new HashSet<string>();
    }

    public sealed class Fish
    {
        public const float BoundsX = 5.55f, BoundsZMin = -2.05f, BoundsZMax = 2.05f, BoundsYMax = 5.35f;
        static readonly Vector3 Up = Vector3.up;
        static readonly HashSet<FishState> Active = new HashSet<FishState>
        {
            FishState.IDLE, FishState.EXPLORE, FishState.SCHOOL, FishState.RETURN, FishState.INVESTIGATE, FishState.PLAY,
            FishState.FOLLOW, FishState.SOCIALIZE, FishState.PATROL, FishState.NURSERY, FishState.COURT,
        };

        public readonly Ecosystem eco;
        public readonly FishSpecies sp;
        public readonly FishRecord rec;
        public readonly int idx, uid, id;
        public string name => rec.Name;
        public string sex => rec.Sex;
        public readonly string modelKey;

        // life
        public Stage stage;
        public float growth, targetScale, speedK, independence, scale, size;
        public double guardUntil;

        // motion
        public Vector3 pos, vel, heading, wantDir, goal;
        public float yawRate, pitch, tail = 0.5f, pectF, pectBrake, pectS, pectB, idleScull, phase, finPhase, burstT, burst, gulp, flare, speedMul = 1;
        public Vector3? face;

        // needs & mind
        public float hunger, fear, stateT, stateDur, thinkT;
        public FishState state = FishState.IDLE;
        public FishCtx ctx = new FishCtx();
        public readonly FishMemory mem = new FishMemory();
        public bool hidden;
        public Place fav, home, hideSpot;

        // what the renderer draws (web space): rotation, scale, and the shader's body wave
        public Quaternion rotation = Quaternion.identity;
        public Vector3 scaleXYZ = Vector3.one;
        public Vector4 wag;
        public float pect = 0.55f;

        public float p(string trait) => rec.Traits.TryGetValue(trait, out var v) ? (float)v : 0.5f;

        public Fish(Ecosystem eco, FishRecord rec, int idx)
        {
            this.eco = eco;
            this.rec = rec;
            this.idx = idx;
            sp = FishSpecies.All[rec.Sp];
            uid = rec.Uid;
            id = eco.nextId++;
            var R = new Mulberry32((uint)(rec.Uid * 7919 + 17));
            modelKey = sp.ModelKey(rec.Gene.Variant);
            wag = new Vector4(0, sp.swim.amp * sp.len, 0, sp.len);
            UpdateLife(eco.world.Now());
            scale = targetScale;
            size = sp.len * scale;
            vel = new Vector3(Rnd.R(-1, 1), 0, Rnd.R(-1, 1)).normalized * sp.swim.cruise * 0.5f;
            heading = vel.normalized;
            wantDir = heading;
            // slow cruisers (under ~1.5 body lengths/s) keep fanning their pectorals all the time
            idleScull = 0.35f * Mathf.Clamp01(1 - sp.swim.cruise / sp.len / 1.5f);
            phase = (float)R.Next() * 10;
            finPhase = (float)R.Next() * 10;
            burstT = Rnd.R(0, 2);
            hunger = Rnd.R(0.2f, 0.6f);
            stateDur = Rnd.R(3, 8);
            thinkT = Rnd.R(0, 0.5f);
        }

        /// <summary>Age-dependent state: life stage, body size and swimming speed.</summary>
        public void UpdateLife(double t)
        {
            stage = Lifecycle.StageOf(rec, t);
            growth = (float)Lifecycle.GrowthOf(rec, t);
            targetScale = growth * (float)rec.Gene.Size;
            speedK = (0.45f + 0.55f * growth) * (stage == Stage.Elder ? 0.85f : 1);
            // how far a youngster dares to stray from where it hatched
            independence = Mathf.Clamp01((growth - 0.3f) / 0.45f);
        }

        public Fish mate => rec.Mate.HasValue && eco.byUid.TryGetValue(rec.Mate.Value, out var m) ? m : null;

        /// <summary>Where a youngster grows up: the spot it hatched at, or its favourite place.</summary>
        public Vector3 NurseryPos() => (rec.Nursery != null ? eco.SpotByName(rec.Nursery) : null)?.pos ?? fav.pos;

        public string Character
        {
            get
            {
                var pairs = new (float, string)[]
                {
                    (p("curiosity") * 1.1f, "Любопытный исследователь"),
                    (p("shyness") * 1.15f, "Пугливый тихоня"),
                    (p("gluttony") * (sp.Life.Traits.First(t => t.key == "gluttony").value > 0.9 ? 1.2f : 0.9f), "Большой обжора"),
                    (p("aggression") * 1.2f, "Задира"),
                    (p("playfulness") * 1.05f, "Неутомимый игрун"),
                    (p("territoriality") * 1.25f, "Хозяин территории"),
                    (p("bravery"), "Смельчак"),
                    ((1 - p("activity")) * 1.1f, "Неторопливый философ"),
                };
                return pairs.OrderByDescending(x => x.Item1).First().Item2;
            }
        }

        static readonly string[] TerritoryKinds = { "castle", "cave", "hide", "ship", "arch" };
        static readonly string[] CuriousKinds = { "landmark", "ship", "castle", "filter", "arch" };

        public void Init(Vector3? at)
        {
            var spots = eco.spots;
            // favourite place: remembered from earlier visits, or chosen by character + species
            fav = rec.Fav != null ? eco.SpotByName(rec.Fav) : null;
            if (fav == null)
            {
                if (sp.homeKind == "anemone") fav = eco.SpotByName("актиния");
                else if (p("territoriality") > 0.6f) fav = Rnd.Pick(spots.Where(s => TerritoryKinds.Contains(s.kind)).ToList());
                else if (sp.bottom) fav = Rnd.Pick(spots.Where(s => s.pos.y < 1.6f).ToList());
                else if (p("shyness") > 0.55f) fav = Rnd.Pick(spots.Where(s => s.kind == "hide" || s.kind == "plants" || s.kind == "cave").ToList());
                else if (p("curiosity") > 0.65f) fav = Rnd.Pick(spots.Where(s => CuriousKinds.Contains(s.kind)).ToList());
                else fav = Rnd.Pick(spots.Where(s => s.kind == "plants" || s.kind == "open" || s.kind == "landmark").ToList());
            }
            fav ??= Rnd.Pick(spots);
            home = fav;
            var R = new Mulberry32((uint)(rec.Uid * 7919 + 31));
            Place best = null;
            float bd = 1e9f;
            foreach (var s in spots)
                if (s.kind == "hide" || s.kind == "cave" || s.kind == "plants")
                {
                    float d = Vector3.Distance(s.pos, fav.pos) + (float)R.Next() * 2;
                    if (d < bd) { bd = d; best = s; }
                }
            hideSpot = best;
            if (at.HasValue) pos = at.Value;
            else if (stage == Stage.Fry) pos = RandomPointNear(NurseryPos(), 0.6f);
            else pos = new Vector3(Rnd.R(-BoundsX, BoundsX) * 0.9f, Rnd.R(sp.zoneMin + 0.5f, sp.zoneMax), Rnd.R(-1.5f, 1.5f));
            if (sp.bottom) pos.y = eco.HeightAt(pos.x, pos.z) + 0.12f;
            rotation = Quaternion.LookRotation(heading, Up);
            SetState(stage == Stage.Fry ? FishState.NURSERY : sp.school > 0.6f ? FishState.SCHOOL : FishState.IDLE);
        }

        public float Dist(Fish o) => Vector3.Distance(pos, o.pos);

        public void SetState(FishState s, FishCtx c = null, float? dur = null)
        {
            state = s;
            ctx = c ?? new FishCtx();
            stateT = 0;
            stateDur = dur ?? Rnd.R(4, 12) * (0.6f + (1 - p("activity")) * 0.8f);
            face = null;
        }

        public void Remember(Fish other, string kind, float delta = 0)
        {
            if (other == null) return;
            mem.friends.TryGetValue(other.id, out float v);
            mem.friends[other.id] = Mathf.Clamp(v + delta, -1, 1);
            mem.last = (other.name, kind, eco.time);
        }

        public Fish BestFriend()
        {
            Fish best = null;
            float bv = 0.15f;
            foreach (var kv in mem.friends)
                if (kv.Value > bv && eco.byId.TryGetValue(kv.Key, out var o) && !o.hidden) { best = o; bv = kv.Value; }
            return best;
        }

        // ------------------------------------------------------------------ think
        void Think()
        {
            if (hidden) return;
            var s = state;
            bool busy = s == FishState.FLEE || s == FishState.STANDOFF || s == FishState.CAROUSEL || s == FishState.SCRIPT;

            // 1. threats: much bigger fish rushing close
            if (!busy && s != FishState.EAT && s != FishState.DEFEND)
            {
                foreach (var o in eco.fish)
                {
                    if (o == this || o.hidden || (o.sp == sp && stage == Stage.Fry)) continue;
                    bool attacked = o.state == FishState.DEFEND && o.ctx.target == this;
                    if (o.size > size * 2.0f || attacked)
                    {
                        float d = Dist(o), r = o.size * 1.2f + 0.25f;
                        // startle only when the big fish is actually closing in; a big fish just passing by
                        // is calmly given way to by the steering
                        float closing = d > 1e-4f ? Vector3.Dot(pos - o.pos, o.vel) / d : 0;
                        if (d < r && (attacked || closing > o.sp.swim.cruise * (0.9f - p("shyness") * 0.4f)))
                        {
                            Flee(o.pos, 0.6f + p("shyness") * 0.8f, o);
                            if (sp.school > 0.6f && Rnd.Chance(0.06f)) eco.Tell("scatter", $"Стайка «{sp.Title}» расступилась перед {o.name}", pos, 40);
                            return;
                        }
                    }
                }
            }

            // 2. food
            if (eco.food.Count > 0 && s != FishState.EAT && !busy)
            {
                float sleepy = sp.nocturnal ? 0 : eco.night;
                float pr = (0.25f + p("gluttony") * 0.75f) * (0.4f + hunger) * (1 - sleepy * 0.6f);
                if (Rnd.Chance(pr))
                {
                    var f = PickFood();
                    if (f != null) { SetState(FishState.EAT, new FishCtx { food = f }, Rnd.R(6, 14)); f.claim.Add(this); return; }
                }
            }

            // 3. territory defence
            if (p("territoriality") > 0.5f && !busy && (s == FishState.PATROL || s == FishState.IDLE || s == FishState.REST || s == FishState.RETURN) && Vector3.Distance(pos, home.pos) < 1.6f)
            {
                foreach (var o in eco.fish)
                {
                    if (o == this || o.hidden || o.state == FishState.FLEE) continue;
                    if (Vector3.Distance(o.pos, home.pos) < 0.9f + size && Rnd.Chance(p("aggression") * 0.5f))
                    {
                        if (o.p("territoriality") > 0.5f && o.size > size * 0.6f && o.state != FishState.STANDOFF) StartStandoff(o);
                        else if (o.size < size * 1.6f)
                        {
                            SetState(FishState.DEFEND, new FishCtx { target = o }, Rnd.R(2, 4));
                            Remember(o, "прогнал", -0.1f);
                            o.Remember(this, "был прогнан", -0.15f);
                            eco.Tell("defend", $"{name} прогоняет {o.name} со своей территории", pos, 25, this);
                        }
                        return;
                    }
                }
            }

            // 4. curious about the pointer (shy ones keep away, see Steer)
            if (eco.cursor.active && !busy && s != FishState.EAT && s != FishState.CURIOUS && Vector3.Distance(pos, eco.cursor.point) < 2.6f)
            {
                if (p("curiosity") > 0.55f && p("bravery") > 0.35f && Rnd.Chance(0.06f * p("curiosity")))
                {
                    SetState(FishState.CURIOUS, new FishCtx { point = eco.cursor.point, follow = true }, Rnd.R(5, 12));
                    return;
                }
            }

            // 5. next state when time is up
            if (stateT > stateDur) ChooseNext();
        }

        FoodItem PickFood()
        {
            FoodItem best = null;
            float bs = -1e9f;
            foreach (var f in eco.food)
            {
                if (f.eaten) continue;
                if (sp.bottom && f.pos.y > eco.HeightAt(f.pos.x, f.pos.z) + 1.4f) continue;
                float d = Vector3.Distance(pos, f.pos);
                if (d > 6.5f) continue;
                float score = -d - f.claim.Count * 0.4f * (1 - p("aggression")) + Rnd.R() * 0.8f;
                if (score > bs) { bs = score; best = f; }
            }
            return best;
        }

        public void Flee(Vector3 from, float dur = 1.2f, Fish who = null)
        {
            var a = pos - from;
            if (a.sqrMagnitude < 1e-4f) a = new Vector3(Rnd.R(-1, 1), Rnd.R(-0.3f, 0.3f), Rnd.R(-1, 1));
            a.y *= 0.4f;
            a.Normalize();
            var prev = state == FishState.FLEE ? ctx.prev : state;
            SetState(FishState.FLEE, new FishCtx { dir = a, prev = prev, who = who }, dur);
            fear = Mathf.Min(1, fear + 0.6f);
            burst = 0.3f;
            if (who != null) Remember(who, "испугался", -0.05f);
        }

        public void StartStandoff(Fish o)
        {
            float dur = Rnd.R(3, 5.5f);
            SetState(FishState.STANDOFF, new FishCtx { rival = o }, dur);
            o.SetState(FishState.STANDOFF, new FishCtx { rival = this }, dur);
            Remember(o, "мерялись характером", -0.1f);
            o.Remember(this, "мерялись характером", -0.1f);
            eco.Tell("standoff", Rnd.Pick(new[]
            {
                $"{name} и {o.name} меряются характером",
                $"{name} и {o.name} спорят, кто тут главный",
                $"Напряжённый момент: {name} против {o.name}",
            }), pos, 30, this);
        }

        static FishState Weighted(List<(FishState s, float w)> w)
        {
            float sum = w.Sum(x => x.w), r = Rnd.R() * sum;
            foreach (var (s, x) in w) { r -= x; if (r <= 0 && x > 0) return s; }
            return w.LastOrDefault(x => x.w > 0).s;
        }

        void ChooseNext()
        {
            float night = eco.night;
            float sleepy = sp.nocturnal ? 0.2f * (1 - night) : night;
            if (stage == Stage.Fry)
            {
                // youngsters stay close to where they hatched, hide a lot and play with each other
                Enter(Weighted(new List<(FishState, float)>
                {
                    (FishState.NURSERY, 3), (FishState.HIDE, 0.4f + p("shyness") * 0.8f + sleepy),
                    (FishState.PLAY, p("playfulness") * 0.5f * (1 - sleepy)), (FishState.EXPLORE, independence * 0.6f * (1 - sleepy)),
                }));
                return;
            }
            bool school = sp.school > 0.6f;
            bool farFromFav = Vector3.Distance(pos, fav.pos) > 2.5f;
            var friend = BestFriend();
            var m = mate;
            var w = new List<(FishState s, float w)>
            {
                (FishState.IDLE, 0.8f),
                (FishState.SCHOOL, school ? 4.5f * (1 - sleepy * 0.6f) : 0),
                (FishState.EXPLORE, 1.1f * p("curiosity") * p("activity") * (1 - sleepy) * (sp.bottom ? 0.3f : 1)),
                (FishState.RETURN, 0.4f + (farFromFav ? 1.2f : 0)),
                (FishState.REST, (0.3f * (1 - p("activity")) + sleepy * 3.5f) * (stage == Stage.Elder ? 2.5f : 1)),
                (FishState.HIDE, p("shyness") * 0.8f + sleepy * 1.2f * p("shyness") + fear),
                (FishState.SOCIALIZE, friend != null ? sp.social * 1.2f : 0),
                (FishState.FOLLOW, 0.35f * p("curiosity") * (1 - sleepy)),
                (FishState.PLAY, p("playfulness") * 0.8f * (1 - sleepy) * (school ? 0.35f : 1) * (stage == Stage.Elder ? 0.3f : 1)),
                (FishState.INVESTIGATE, p("curiosity") * 0.7f * (1 - sleepy)),
                (FishState.BUBBLES, sp.bottom ? 0 : p("playfulness") * 0.25f * (1 - sleepy)),
                (FishState.WAIT_FOOD, hunger > 0.55f && mem.foodSpot.HasValue ? 0.7f * p("gluttony") : 0),
                (FishState.PATROL, p("territoriality") > 0.5f ? 1.6f * p("territoriality") : 0),
                (FishState.CIRCLE, sp.bottom ? 0 : 0.12f * p("playfulness")),
                (FishState.AIR, sp.id == "cory" ? 0.08f : 0),
                (FishState.CRUISE, sp.bottom ? 2.5f : 0),
                (FishState.COURT, m != null && !m.hidden && m.stage != Stage.Fry ? 1.4f * (1 - sleepy * 0.7f) : 0),
                (FishState.GUARD, guardUntil > eco.world.Now() ? 3.5f : 0),
            };
            if (stage == Stage.Elder)
                for (int i = 0; i < w.Count; i++) if (w[i].s == FishState.EXPLORE) w[i] = (w[i].s, w[i].w * 0.5f);
            Enter(Weighted(w), friend);
        }

        public void Enter(FishState next, Fish friend = null)
        {
            switch (next)
            {
                case FishState.EXPLORE:
                {
                    // prefer spots visited least
                    var s = eco.spots.OrderBy(q => (mem.visits.TryGetValue(q.name, out var v) ? v : 0) + Rnd.R(-3, 3)).First();
                    bool first = !mem.discoveries.Contains(s.name);
                    SetState(FishState.EXPLORE, new FishCtx { spot = s, point = RandomPointNear(s.pos, 1.2f) }, Rnd.R(8, 16));
                    if (first && Rnd.Chance(0.25f)) eco.Tell("explore", $"{name} отправился изучать {s.name}", pos, 25, this);
                    break;
                }
                case FishState.INVESTIGATE:
                {
                    var s = Rnd.Pick(eco.spots.Where(q => q.kind != "open").ToList());
                    SetState(FishState.INVESTIGATE, new FishCtx { spot = s, ang = Rnd.R(0, 6.28f), side = Rnd.Chance(0.5f) ? -1 : 1 }, Rnd.R(8, 15));
                    if (Rnd.Chance(0.3f))
                        eco.Tell("investigate", sp.id == "cory" && s.kind == "cave" ? $"{sp.Title} {name} исследует пещеру" : $"{name} внимательно изучает: {s.name}", s.pos, 20, this);
                    break;
                }
                case FishState.SOCIALIZE:
                    if (friend == null) { Enter(FishState.IDLE); return; }
                    SetState(FishState.SOCIALIZE, new FishCtx { other = friend, side = Rnd.Chance(0.5f) ? -1 : 1 }, Rnd.R(8, 18));
                    if (Rnd.Chance(0.25f)) eco.Tell("friends", $"{name} и {friend.name} плавают вместе", pos, 30, this);
                    break;
                case FishState.FOLLOW:
                {
                    var o = NearestOther(x => !x.hidden && x.sp != sp && x.size > size * 0.5f, 3.5f);
                    if (o == null) { Enter(FishState.IDLE); return; }
                    SetState(FishState.FOLLOW, new FishCtx { other = o }, Rnd.R(6, 12));
                    if (Rnd.Chance(0.25f)) eco.Tell("follow", $"{name} увязался за {o.name}", pos, 25, this);
                    break;
                }
                case FishState.PLAY:
                {
                    var o = NearestOther(x => !x.hidden && x.p("playfulness") > 0.35f && Active.Contains(x.state) && x.size < size * 2.2f && x.size > size * 0.45f, 3.5f);
                    if (o == null) { Enter(FishState.EXPLORE); return; }
                    float dur = Rnd.R(6, 12);
                    SetState(FishState.PLAY, new FishCtx { other = o, role = "chase" }, dur);
                    o.SetState(FishState.PLAY, new FishCtx { other = this, role = "run" }, dur);
                    eco.Tell("play", Rnd.Pick(new[] { $"{name} и {o.name} играют в догонялки", $"{name} гоняется за {o.name}", $"Игра! {name} и {o.name} носятся по аквариуму" }), pos, 18, this);
                    break;
                }
                case FishState.BUBBLES:
                {
                    var src = Rnd.Pick(eco.bubbleSources);
                    SetState(FishState.BUBBLES, new FishCtx { src = src }, Rnd.R(6, 12));
                    if (Rnd.Chance(0.35f)) eco.Tell("bubbles", Rnd.Pick(new[] { $"{name} играет с пузырьками", $"{name} ловит пузыри у {src.name}" }), pos, 30, this);
                    break;
                }
                case FishState.WAIT_FOOD:
                    SetState(FishState.WAIT_FOOD, null, Rnd.R(6, 12));
                    if (Rnd.Chance(0.3f)) eco.Tell("wait", $"{name} ждёт корм там, где его обычно кормят", pos, 60, this);
                    break;
                case FishState.HIDE:
                    SetState(FishState.HIDE, new FishCtx { spot = hideSpot ?? fav, peek = Rnd.R(3, 8) }, Rnd.R(8, 20));
                    break;
                case FishState.CIRCLE:
                {
                    var c = Rnd.Pick(eco.clumps);
                    if (c == null) { Enter(FishState.IDLE); return; }
                    float r = c.radius + Rnd.R(0.4f, 0.8f);
                    SetState(FishState.CIRCLE, new FishCtx { center = c.pos + new Vector3(0, Rnd.R(0.3f, 1.2f), 0), r = r, ang = Rnd.R(0, 6.28f), side = Rnd.Chance(0.5f) ? -1 : 1 }, Rnd.R(6, 12));
                    if (Rnd.Chance(0.3f)) eco.Tell("circle", $"{name} кружит вокруг растения", c.pos, 30, this);
                    break;
                }
                case FishState.AIR:
                    SetState(FishState.AIR, new FishCtx { phase = 0, x = pos.x, z = pos.z }, 6);
                    eco.Tell("air", Rnd.Pick(new[] { $"Сомик {name} метнулся к поверхности за глотком воздуха", $"{name} сделал рывок за воздухом — так умеют только сомики" }), pos, 50, this);
                    break;
                case FishState.CRUISE:
                    SetState(FishState.CRUISE, new FishCtx { point = RandomBottomPoint() }, Rnd.R(5, 12));
                    break;
                case FishState.NURSERY:
                    SetState(FishState.NURSERY, null, Rnd.R(8, 16));
                    break;
                case FishState.COURT:
                {
                    var m = mate;
                    SetState(FishState.COURT, new FishCtx { other = m, side = Rnd.Chance(0.5f) ? -1 : 1 }, Rnd.R(10, 20));
                    if (Rnd.Chance(0.2f)) eco.Tell("court", Rnd.Pick(new[] { $"{name} и {m.name} не расстаются", $"{name} кружит рядом со своей парой — {m.name}" }), pos, 60, this);
                    break;
                }
                case FishState.GUARD:
                    SetState(FishState.GUARD, new FishCtx { ang = Rnd.R(0, 6.28f) }, Rnd.R(10, 18));
                    if (Rnd.Chance(0.2f)) eco.Tell("guard", $"{name} присматривает за мальками", pos, 90, this);
                    break;
                default:
                    SetState(next);
                    break;
            }
        }

        Fish NearestOther(System.Func<Fish, bool> filter, float maxD = 99)
        {
            Fish best = null;
            float bd = maxD;
            foreach (var o in eco.fish)
            {
                if (o == this || !filter(o)) continue;
                float d = Dist(o);
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        public Vector3 RandomZonePoint() => new Vector3(Rnd.R(-BoundsX, BoundsX) * 0.9f, Rnd.R(sp.zoneMin + 0.3f, sp.zoneMax), Rnd.R(BoundsZMin, BoundsZMax) * 0.9f);

        public Vector3 RandomPointNear(Vector3 c, float r) => new Vector3(
            Mathf.Clamp(c.x + Rnd.R(-r, r), -BoundsX, BoundsX),
            Mathf.Clamp(c.y + Rnd.R(-r, r) * 0.5f, sp.zoneMin + 0.3f, Mathf.Min(BoundsYMax, sp.zoneMax + 0.6f)),
            Mathf.Clamp(c.z + Rnd.R(-r, r) * 0.6f, BoundsZMin, BoundsZMax));

        Vector3 RandomBottomPoint()
        {
            var near = Rnd.Chance(0.6f) ? fav.pos : new Vector3(Rnd.R(-5, 5), 0, Rnd.R(-1.8f, 2));
            float x = Mathf.Clamp(near.x + Rnd.R(-1.8f, 1.8f), -5.4f, 5.4f), z = Mathf.Clamp(near.z + Rnd.R(-1, 1), -2.0f, 2.0f);
            return new Vector3(x, eco.HeightAt(x, z) + 0.1f, z);
        }

        // ------------------------------------------------------------------ behave
        void Behave(float dt)
        {
            var c = ctx;
            float spd = 0.7f;
            face = null;
            switch (state)
            {
                case FishState.IDLE:
                    // leisurely wandering: the next waypoint lies roughly ahead and is picked before the
                    // current one is reached, so the path flows instead of zig-zagging point to point
                    if (!c.point.HasValue || Vector3.Distance(pos, c.point.Value) < Mathf.Max(0.35f, size * 2.5f))
                    {
                        var ahead = new Vector3(heading.x, 0, heading.z).normalized * Rnd.R(0.8f, 1.6f) + pos;
                        c.point = RandomPointNear(ahead, 0.7f);
                    }
                    goal = c.point.Value; spd = 0.45f;
                    break;
                case FishState.SCHOOL:
                {
                    if (!eco.schools.TryGetValue(sp.id, out var st)) { SetState(FishState.IDLE); break; }
                    goal = st.target + new Vector3(Mathf.Sin(idx * 1.7f) * 0.4f, Mathf.Cos(idx * 2.3f) * 0.2f, Mathf.Sin(idx * 0.9f) * 0.3f);
                    spd = st.speed;
                    break;
                }
                case FishState.CRUISE:
                    if (Vector3.Distance(pos, c.point.Value) < 0.15f || c.pause > 0)
                    {
                        c.pause = (c.pause ?? Rnd.R(0.6f, 2.5f)) - dt;
                        if (c.pause <= 0) { c.point = RandomBottomPoint(); c.pause = null; }
                        spd = 0.05f;
                        goal = pos + heading;
                    }
                    else { goal = c.point.Value; spd = 0.8f; }
                    break;
                case FishState.EXPLORE:
                    if (Vector3.Distance(pos, c.point.Value) < 0.4f)
                    {
                        c.point = RandomPointNear(c.spot.pos, 1.0f);
                        mem.visits[c.spot.name] = (mem.visits.TryGetValue(c.spot.name, out var v) ? v : 0) + 1;
                        mem.discoveries.Add(c.spot.name);
                    }
                    goal = c.point.Value; spd = 0.9f;
                    break;
                case FishState.RETURN:
                    goal = fav.pos; spd = 0.8f;
                    if (Vector3.Distance(pos, goal) < fav.radius + 0.2f)
                    {
                        mem.visits[fav.name] = (mem.visits.TryGetValue(fav.name, out var v) ? v : 0) + 1;
                        SetState(Rnd.Chance(0.5f) ? FishState.REST : FishState.IDLE);
                    }
                    break;
                case FishState.REST:
                {
                    float t = eco.time * 0.3f + idx;
                    goal = fav.pos + new Vector3(Mathf.Sin(t) * 0.3f, Mathf.Sin(t * 0.7f) * 0.1f, Mathf.Cos(t * 0.8f) * 0.2f);
                    if (sp.bottom) goal.y = eco.HeightAt(goal.x, goal.z) + 0.1f;
                    spd = 0.22f;
                    if (eco.night > 0.6f && !sp.nocturnal) spd = 0.1f;
                    break;
                }
                case FishState.HIDE:
                {
                    var s = c.spot;
                    c.peek -= dt;
                    if (c.peek < 0 && c.peek > -2.2f)
                    {
                        goal = s.pos + new Vector3(0, 0.2f, 0.9f); spd = 1.4f;
                        if (!c.peeked) { c.peeked = true; if (Rnd.Chance(0.3f)) eco.Tell("peek", $"{name} выглянул из укрытия ({s.name})", s.pos, 40, this); }
                    }
                    else
                    {
                        if (c.peek <= -2.2f) { c.peek = Rnd.R(5, 12); c.peeked = false; }
                        float t = eco.time * 0.4f + idx;
                        goal = s.pos + new Vector3(Mathf.Sin(t) * s.radius * 0.6f, 0, Mathf.Cos(t * 1.3f) * s.radius * 0.4f);
                        spd = 0.3f;
                    }
                    break;
                }
                case FishState.SOCIALIZE:
                {
                    var o = c.other;
                    if (o == null || o.hidden) { SetState(FishState.IDLE); break; }
                    var side = Vector3.Cross(o.heading, Up).normalized * (c.side * (o.size + size) * 0.7f);
                    goal = o.pos + side + o.heading * 0.1f;
                    spd = Mathf.Clamp(o.vel.magnitude / sp.swim.cruise + Vector3.Distance(pos, goal) * 0.8f, 0.2f, 1.6f);
                    Remember(o, "плавали вместе", dt * 0.02f);
                    o.Remember(this, "плавали вместе", dt * 0.01f);
                    break;
                }
                case FishState.FOLLOW:
                {
                    var o = c.other;
                    if (o == null || o.hidden) { SetState(FishState.IDLE); break; }
                    goal = o.pos - o.heading * ((o.size + size) * 0.9f);
                    spd = Mathf.Clamp(Vector3.Distance(pos, goal) * 1.2f, 0.3f, 1.5f);
                    Remember(o, "следовал", dt * 0.01f);
                    break;
                }
                case FishState.PLAY:
                {
                    var o = c.other;
                    if (o == null || o.state != FishState.PLAY || o.ctx.other != this) { SetState(FishState.IDLE); break; }
                    if (c.role == "chase")
                    {
                        goal = o.pos + o.vel * 0.3f; spd = 1.7f;
                        if (Dist(o) < (size + o.size) * 0.45f && stateT > 0.8f)
                        {
                            c.role = "run"; o.ctx.role = "chase"; c.tags++; o.ctx.tags++;
                            stateT = Mathf.Min(stateT, stateDur - 2);
                            o.burst = 0.4f;
                            Remember(o, "играли", 0.08f); o.Remember(this, "играли", 0.08f);
                        }
                    }
                    else
                    {
                        if (!c.point.HasValue || Vector3.Distance(pos, c.point.Value) < 0.5f || (Vector3.Distance(pos, o.pos) < 0.6f && Rnd.Chance(0.05f)))
                        {
                            var away = (pos - o.pos).normalized * 2.2f + pos + new Vector3(Rnd.R(-1.5f, 1.5f), Rnd.R(-0.6f, 0.6f), Rnd.R(-1, 1));
                            c.point = RandomPointNear(away, 0.2f);
                        }
                        goal = c.point.Value; spd = 1.55f;
                    }
                    break;
                }
                case FishState.BUBBLES:
                {
                    float t = eco.time * 2 + idx;
                    goal = c.src.pos + new Vector3(Mathf.Sin(t) * 0.25f, 0.4f + (Mathf.Sin(t * 0.37f) * 0.5f + 0.5f) * 2.0f, Mathf.Cos(t) * 0.25f);
                    spd = 1.0f;
                    var bub = eco.bubbles.Nearest(pos, size * 0.8f + 0.05f);
                    if (bub != null) eco.bubbles.Poke(bub, heading);
                    break;
                }
                case FishState.WAIT_FOOD:
                {
                    var fs = mem.foodSpot ?? fav.pos;
                    goal = new Vector3(fs.x + Mathf.Sin(eco.time + idx) * 0.4f, Mathf.Min(fs.y, 5.0f), fs.z);
                    spd = 0.4f;
                    break;
                }
                case FishState.PATROL:
                {
                    var h = home.pos;
                    c.ang += dt * 0.5f;
                    goal = new Vector3(h.x + Mathf.Cos(c.ang) * 0.8f, h.y + Mathf.Sin(c.ang * 0.7f) * 0.2f, h.z + Mathf.Sin(c.ang) * 0.4f);
                    spd = 0.55f;
                    break;
                }
                case FishState.DEFEND:
                {
                    var o = c.target;
                    if (o == null) { SetState(FishState.RETURN); break; }
                    goal = o.pos; spd = 1.8f;
                    flare = Mathf.Min(1, flare + dt * 3);
                    if (Dist(o) < (size + o.size) * 0.5f) { o.Flee(pos, 1.6f, this); if (Rnd.Chance(0.5f)) SetState(FishState.RETURN); }
                    if (Vector3.Distance(o.pos, home.pos) > 2.2f) SetState(FishState.RETURN);
                    break;
                }
                case FishState.STANDOFF:
                {
                    var o = c.rival;
                    if (o == null || o.state != FishState.STANDOFF) { SetState(FishState.RETURN); break; }
                    var a = pos - o.pos; a.y = 0;
                    if (a.sqrMagnitude < 1e-4f) a = Vector3.right;
                    a = a.normalized * ((size + o.size) * 0.55f);
                    goal = o.pos + a + new Vector3(0, Mathf.Sin(eco.time * 4 + idx) * 0.03f, 0);
                    face = o.pos;
                    spd = 0.6f;
                    flare = Mathf.Min(1, flare + dt * 2);
                    if (stateT > stateDur - 0.1f && !c.done)
                    {
                        c.done = true; o.ctx.done = true;
                        float mine = p("bravery") + size + Rnd.R() * 0.6f, theirs = o.p("bravery") + o.size + Rnd.R() * 0.6f;
                        var (win, lose) = mine > theirs ? (this, o) : (o, this);
                        lose.Flee(win.pos, 2.0f, win);
                        win.SetState(FishState.DEFEND, new FishCtx { target = lose }, 2);
                        if (Rnd.Chance(0.6f)) eco.Tell("standoff-end", Rnd.Pick(new[] { $"{win.name} победил в споре, {lose.name} отступает", $"{lose.name} решил не связываться с {win.name}" }), win.pos, 20);
                    }
                    break;
                }
                case FishState.EAT:
                {
                    var f = c.food;
                    if (f == null || f.eaten)
                    {
                        var n = PickFood();
                        if (n != null && hunger > 0.05f) { c.food = n; n.claim.Add(this); }
                        else SetState(sp.school > 0.6f ? FishState.SCHOOL : FishState.IDLE);
                        break;
                    }
                    goal = f.pos; spd = 1.5f + p("gluttony") * 0.6f;
                    if (Vector3.Distance(pos, f.pos) < size * 0.45f + 0.07f) eco.EatFood(f, this);
                    break;
                }
                case FishState.FLEE:
                    goal = pos + c.dir * 2.0f; spd = 2.2f;
                    if (stateT > stateDur)
                    {
                        if (c.prev == FishState.SCHOOL) SetState(FishState.SCHOOL);
                        else if (p("shyness") > 0.6f && Rnd.Chance(0.6f)) Enter(FishState.HIDE);
                        else SetState(FishState.RETURN);
                    }
                    break;
                case FishState.CURIOUS:
                {
                    var pt = c.follow && eco.cursor.active ? eco.cursor.point : c.point ?? pos;
                    goal = pt; goal.z = Mathf.Min(goal.z, BoundsZMax - 0.05f);
                    float d = Vector3.Distance(pos, goal);
                    spd = d > 0.5f ? 0.9f : 0.12f;
                    if (d < 0.6f) face = pt;
                    if (c.follow && !eco.cursor.active && stateT > 2) stateDur = Mathf.Min(stateDur, stateT + 1);
                    break;
                }
                case FishState.WATCH:
                {
                    var cam = eco.cameraPos;
                    goal = new Vector3(Mathf.Clamp(cam.x * 0.25f + c.ox, -4.5f, 4.5f), Mathf.Clamp(cam.y * 0.4f + 1.2f, 1.2f, 4.5f), BoundsZMax - 0.15f);
                    face = cam;
                    spd = Vector3.Distance(pos, goal) > 0.4f ? 0.8f : 0.05f;
                    break;
                }
                case FishState.INVESTIGATE:
                {
                    var s = c.spot;
                    float d = Vector3.Distance(pos, s.pos);
                    if (d > s.radius + 0.8f) { goal = s.pos; spd = 0.8f; }
                    else
                    {
                        c.ang += c.side * dt * 0.5f;
                        goal = s.pos + new Vector3(Mathf.Cos(c.ang) * (s.radius + 0.35f), Mathf.Sin(c.ang * 0.5f) * 0.15f, Mathf.Sin(c.ang) * (s.radius + 0.35f) * 0.6f);
                        spd = 0.35f;
                        if (Rnd.Chance(dt * 0.4f)) face = s.pos;
                        mem.visits[s.name] = (mem.visits.TryGetValue(s.name, out var v) ? v : 0) + dt * 0.2f;
                    }
                    break;
                }
                case FishState.CIRCLE:
                case FishState.CAROUSEL:
                {
                    float r = c.r;
                    c.ang += c.side * dt * (c.angSpeed > 0 ? c.angSpeed : 1.0f / r);
                    float ex = c.rx > 0 ? c.rx : r, ez = c.rz > 0 ? c.rz : r * 0.6f;
                    goal = c.center + new Vector3(Mathf.Cos(c.ang) * ex, Mathf.Sin(c.ang * 2) * 0.15f + c.dy, Mathf.Sin(c.ang) * ez);
                    spd = state == FishState.CAROUSEL ? 1.25f : 0.9f;
                    if (state == FishState.CAROUSEL && stateT > stateDur) SetState(sp.school > 0.6f ? FishState.SCHOOL : FishState.RETURN);
                    break;
                }
                case FishState.AIR:
                    if (c.phase == 0) { goal = new Vector3(c.x, 5.5f, c.z); spd = 2.2f; if (pos.y > 5.3f) c.phase = 1; }
                    else
                    {
                        goal = new Vector3(c.x + 0.5f, eco.HeightAt(c.x, c.z) + 0.15f, c.z); spd = 1.2f;
                        if (pos.y < eco.HeightAt(pos.x, pos.z) + 0.3f) SetState(FishState.CRUISE, new FishCtx { point = RandomBottomPoint() });
                    }
                    break;
                case FishState.SCRIPT:
                    goal = c.point ?? pos; spd = c.speed;
                    if (c.face.HasValue) face = c.face;
                    break;
                case FishState.NURSERY:
                {
                    // little loops around the nursery, wider as the youngster gains confidence
                    var nurse = NurseryPos();
                    if (!c.point.HasValue || Vector3.Distance(pos, c.point.Value) < 0.15f) c.point = RandomPointNear(nurse, 0.3f + independence * 1.6f);
                    goal = c.point.Value; spd = 0.5f;
                    break;
                }
                case FishState.COURT:
                {
                    // side by side with the partner, now and then a little display
                    var o = c.other;
                    if (o == null || o.hidden) { SetState(FishState.IDLE); break; }
                    var side = Vector3.Cross(o.heading, Up).normalized * (c.side * (o.size + size) * 0.45f);
                    goal = o.pos + side + o.heading * 0.05f;
                    spd = Mathf.Clamp(o.vel.magnitude / sp.swim.cruise + Vector3.Distance(pos, goal) * 0.8f, 0.2f, 1.3f);
                    if (Mathf.Sin(eco.time * 0.7f + idx) > 0.97f) flare = Mathf.Min(1, flare + dt * 2);
                    break;
                }
                case FishState.NEST:
                {
                    // the pair circles closely over the chosen spot before the brood arrives
                    var s = c.spot;
                    c.ang += c.side * dt * 0.9f;
                    goal = s.pos + new Vector3(Mathf.Cos(c.ang) * 0.3f, 0.15f + Mathf.Sin(c.ang * 2) * 0.05f, Mathf.Sin(c.ang) * 0.2f);
                    if (sp.bottom) goal.y = eco.HeightAt(goal.x, goal.z) + 0.1f;
                    spd = 0.4f;
                    if (Rnd.Chance(dt * 0.5f)) face = s.pos;
                    break;
                }
                case FishState.GUARD:
                {
                    // parents patrol around their youngsters and shoo away strangers
                    var nurse = NurseryPos();
                    c.ang += dt * 0.6f;
                    goal = new Vector3(nurse.x + Mathf.Cos(c.ang) * 0.6f, nurse.y + 0.1f + Mathf.Sin(c.ang * 0.7f) * 0.15f, nurse.z + Mathf.Sin(c.ang) * 0.35f);
                    spd = 0.5f;
                    var intruder = NearestOther(o => !o.hidden && o.sp != sp && Vector3.Distance(o.pos, nurse) < 0.9f && o.size < size * 1.6f, 1.4f);
                    if (intruder != null && Rnd.Chance(dt * 0.8f))
                    {
                        SetState(FishState.DEFEND, new FishCtx { target = intruder }, Rnd.R(1.5f, 3));
                        eco.Tell("guard-chase", $"{name} отгоняет {intruder.name} от своих мальков", pos, 40, this);
                    }
                    break;
                }
                default:
                    goal = pos + heading; spd = 0.4f;
                    break;
            }
            // sleep at night for diurnal fish (soft)
            if (!sp.nocturnal && eco.night > 0.7f && (state == FishState.REST || state == FishState.HIDE)) spd *= 0.6f;
            if (sp.nocturnal && eco.night > 0.5f && Active.Contains(state)) spd *= 1.25f;
            speedMul = spd;
        }

        // ------------------------------------------------------------------ steer
        void Steer(float dt)
        {
            var S = sp.swim;
            var acc = Vector3.zero;
            // burst & glide rhythm
            float mul = speedMul;
            if (S.burst > 0 && speedMul > 0.3f)
            {
                burstT -= dt;
                if (burstT < 0) { burst = Rnd.R(0.15f, 0.3f); burstT = Rnd.R(0.5f, 2.2f) / (0.4f + S.burst); }
            }
            if (burst > 0) { burst -= dt; mul *= 1 + 0.8f * S.burst; }
            float desiredSpeed = S.cruise * mul * speedK;
            // seek
            var a = goal - pos;
            float dist = a.magnitude;
            if (dist > 1e-4f)
            {
                float arrive = Mathf.Min(1, dist / (0.25f + size));
                a *= desiredSpeed * Mathf.Max(arrive, 0.08f) / dist;
                a.y *= sp.bottom ? 1 : 0.7f;
                acc += (a - vel) * 2.2f;
            }
            // flocking & separation
            float schoolW = state == FishState.SCHOOL ? sp.school : (sp.school > 0.3f && Active.Contains(state) ? sp.school * 0.25f : 0);
            int n = 0;
            Vector3 coh = Vector3.zero, ali = Vector3.zero;
            foreach (var o in eco.fish)
            {
                if (o == this || o.hidden) continue;
                float dx = o.pos.x - pos.x, dy = o.pos.y - pos.y, dz = o.pos.z - pos.z;
                float d2 = dx * dx + dy * dy + dz * dz;
                float sepR = (size + o.size) * 0.55f + 0.04f;
                if (d2 < sepR * sepR && d2 > 1e-6f)
                {
                    float d = Mathf.Sqrt(d2), f = (sepR - d) / sepR * 6;
                    acc.x -= dx / d * f; acc.y -= dy / d * f * 0.6f; acc.z -= dz / d * f;
                }
                if (schoolW > 0 && o.sp == sp && d2 < 1.2f && (o.state == FishState.SCHOOL || o.state == state))
                {
                    coh += o.pos;
                    ali += o.vel;
                    n++;
                }
                // small fish avoid large fish gently even when not fleeing
                if (o.size > size * 2.2f && d2 < (o.size * 1.4f) * (o.size * 1.4f))
                {
                    float d = Mathf.Sqrt(d2) + 1e-4f, f = (o.size * 1.4f - d) * 3 * (0.5f + p("shyness"));
                    acc.x -= dx / d * f; acc.y -= dy / d * f * 0.5f; acc.z -= dz / d * f;
                }
            }
            if (n > 0)
            {
                acc += (coh / n - pos) * (1.2f * schoolW);
                acc += (ali / n - vel) * (1.6f * schoolW);
            }
            // obstacles
            foreach (var col in eco.colliders)
            {
                float dx = pos.x - col.center.x, dy = pos.y - col.center.y, dz = pos.z - col.center.z;
                float r = col.radius + size * 0.5f + 0.12f, d2 = dx * dx + dy * dy + dz * dz;
                if (d2 < r * r)
                {
                    float d = Mathf.Sqrt(d2) + 1e-4f, f = (r - d) / r * 9;
                    acc.x += dx / d * f; acc.y += dy / d * f * 0.8f; acc.z += dz / d * f;
                }
            }
            // walls, floor and surface
            const float m = 0.35f;
            float floor = eco.HeightAt(pos.x, pos.z) + (sp.bottom ? 0.07f : 0.25f + sp.height * 0.4f);
            if (pos.x > BoundsX - m) acc.x -= (pos.x - (BoundsX - m)) * 14;
            if (pos.x < -BoundsX + m) acc.x += (-BoundsX + m - pos.x) * 14;
            if (pos.z > BoundsZMax - m) acc.z -= (pos.z - (BoundsZMax - m)) * 14;
            if (pos.z < BoundsZMin + m) acc.z += (BoundsZMin + m - pos.z) * 14;
            if (pos.y < floor + 0.15f) acc.y += (floor + 0.15f - pos.y) * 16;
            float ceil = state == FishState.EAT || state == FishState.AIR ? 5.55f : BoundsYMax - 0.1f;
            if (pos.y > ceil) acc.y -= (pos.y - ceil) * 16;
            // shy fish keep away from the pointer
            if (eco.cursor.active && p("shyness") > 0.55f)
            {
                var away = pos - eco.cursor.point;
                float d = away.magnitude;
                if (d < 1.0f) acc += away.normalized * ((1.0f - d) * 4 * p("shyness"));
            }
            // the filter's current
            float fd = Vector3.Distance(eco.filterOutlet, pos);
            if (fd < 1.5f) acc += new Vector3(-0.6f, -0.05f, 0.1f) * ((1.5f - fd) * 0.6f);

            float maxA = S.accel * (state == FishState.FLEE ? 2.2f : 1) * (burst > 0 ? 1.6f : 1);
            if (acc.magnitude > maxA) acc = acc.normalized * maxA;

            // --- locomotion physics. Only fins move the fish:
            //  * the tail pushes along the body axis, thrust ~ (beat intensity)^2, so steady speed ~ intensity;
            //  * pectorals give a weak force in any direction (hovering, fine positioning, braking, backing up);
            //  * water drag is low along the body and strong sideways/vertically, so a fish cannot slide
            //    off its axis or keep moving without beating.
            float flee = state == FishState.FLEE ? 2 : 1;
            float maxV = S.max * speedK * (state == FishState.FLEE || state == FishState.DEFEND || state == FishState.EAT ? 1 : 0.75f);
            var h = heading;
            float fwd = Vector3.Dot(vel, h);
            float speed = Mathf.Max(0, fwd);
            // where the fish wants to go: the velocity it asks for, low-passed against twitching
            var want = vel + acc * 0.6f;
            float wantSpeed = Mathf.Min(want.magnitude, maxV) * (mul < 0.2f ? 0.5f : 1);
            if (face.HasValue) want = face.Value - pos;
            if (want.sqrMagnitude > 1e-6f)
            {
                want.Normalize();
                want.y = Mathf.Clamp(want.y * Mathf.Clamp(speed / (S.cruise * 0.5f), 0.15f, 1), -0.5f, 0.5f);   // hovering fish level out
                wantDir = Vector3.Lerp(wantDir, want, 1 - Mathf.Exp(-dt * 3 * flee)).normalized;
            }
            var w = wantDir;
            // yaw: critically damped turn, rate limited (tight turns need some speed)
            float yaw = Mathf.Atan2(h.x, h.z);
            float err = Mathf.Atan2(w.x, w.z) - yaw;
            if (err > Mathf.PI) err -= Mathf.PI * 2;
            if (err < -Mathf.PI) err += Mathf.PI * 2;
            float om = S.turn * flee;
            float maxRate = om * 0.6f * (0.35f + 0.65f * Mathf.Clamp01(speed / S.cruise));
            yawRate = Mathf.Clamp(yawRate + (om * om * err - 2 * om * yawRate) * dt, -maxRate, maxRate);
            float ny = yaw + yawRate * dt;
            pitch += (Mathf.Asin(Mathf.Clamp(w.y, -1, 1)) - pitch) * (1 - Mathf.Exp(-dt * om * 0.5f));
            h = heading = new Vector3(Mathf.Sin(ny) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(ny) * Mathf.Cos(pitch));
            // wanted forward speed: slower in sharp turns; when the goal lies inside the turning circle
            // the fish almost stops and pivots instead of orbiting it
            float facing = Mathf.Max(0, Vector3.Dot(h, w));
            float turnR = speed / Mathf.Max(maxRate, 0.05f);
            float tight = Mathf.Clamp01(2.2f * turnR / Mathf.Max(dist, 1e-3f));
            float target = wantSpeed * (0.25f + 0.75f * facing * facing) * (1 - tight * (1 - facing) * 0.9f);
            // tail: beat intensity (1 = cruise) chosen to reach the target speed; muscles ramp up quickly
            // (faster for bursts and escapes) and relax a little slower -> coasting between beats
            fwd = Vector3.Dot(vel, h);
            float aWant = Mathf.Clamp(target / S.cruise + 1.2f * (target - fwd) / S.cruise, 0, maxV / S.cruise * 1.1f);
            float ramp = aWant > tail ? (burst > 0 || flee > 1 ? 14 : 5) : 4;
            tail += (aWant - tail) * (1 - Mathf.Exp(-dt * ramp));
            float T0 = S.accel * 0.6f;   // thrust (per unit mass) at cruise intensity
            float thrust = T0 * tail * tail;
            // pectorals: what the tail can't do, weakly. Sideways/vertical sculling only near hover (on
            // the move the fish turns with its body), braking only once the tail has stopped pushing.
            var b = (w * target - vel) * 2;
            float along = Vector3.Dot(b, h);
            float slow = Mathf.Clamp01(1 - fwd / (S.cruise * 0.4f));
            b = (b - h * along) * (0.1f + 0.9f * slow);
            if (along < 0) b += h * (along * Mathf.Clamp01((0.5f - tail) / 0.3f));
            float pMax = T0 * 0.22f;
            if (b.magnitude > pMax) b = b.normalized * pMax;
            pectF = b.magnitude / pMax;
            pectBrake = Mathf.Clamp01(-Vector3.Dot(b, h) / pMax);
            vel += h * (thrust * dt) + b * dt;
            // drag: quadratic + a little linear along the body; sideways/vertical much stronger
            fwd = Vector3.Dot(vel, h);
            var side2 = vel - h * fwd;
            float cAx = T0 / (S.cruise * S.cruise);
            fwd *= Mathf.Exp(-dt * (cAx * Mathf.Abs(fwd) + 0.25f));
            side2 *= Mathf.Exp(-dt * (2.5f + 4 * Mathf.Clamp01(Mathf.Abs(fwd) / S.cruise)));
            vel = h * fwd + side2;
            if (vel.magnitude > maxV * 1.15f) vel = vel.normalized * (maxV * 1.15f);
            pos += vel * dt;
            // hard limits
            pos.x = Mathf.Clamp(pos.x, -BoundsX - 0.1f, BoundsX + 0.1f);
            pos.z = Mathf.Clamp(pos.z, BoundsZMin - 0.1f, BoundsZMax + 0.1f);
            float hf = eco.HeightAt(pos.x, pos.z) + (sp.bottom ? 0.05f : sp.height * 0.35f);
            pos.y = Mathf.Clamp(pos.y, hf, ceil + 0.05f);
        }

        // ------------------------------------------------------------------ animate
        void Animate(float dt)
        {
            var S = sp.swim;
            float speed = vel.magnitude;
            float roll = Mathf.Clamp(-yawRate * 0.12f, -0.5f, 0.5f);
            bool sleeping = state == FishState.REST && eco.night > 0.6f && !sp.nocturnal;
            if (sleeping) roll += 0.12f;
            var q = Quaternion.LookRotation(heading, Up) * Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.forward);
            rotation = Quaternion.Slerp(rotation, q, 1 - Mathf.Exp(-10 * dt));

            // tail undulation: the same intensity that produces thrust. Frequency grows with intensity
            // (and so with speed); amplitude saturates. Turning on the spot takes a few tail sweeps too.
            float rel = speed / S.cruise;
            float spin = Mathf.Min(1, Mathf.Abs(yawRate) / (S.turn * 0.6f)) * 0.5f * Mathf.Clamp01(1 - rel);
            float act = Mathf.Max(tail, spin);
            // Bainbridge: tail-beat frequency f ~ 1.33 (1 + U/L) Hz, it never drops much below ~1.3 Hz;
            // at low speed the stroke gets smaller instead
            float freq = Mathf.PI * 2 * 1.33f * (1 + act * S.cruise / size);
            float amp = S.amp * sp.len * (0.15f + 0.85f * Mathf.Min(act / 0.6f, 1));
            if (S.wiggle) amp *= 1.2f;
            if (flare > 0) { freq *= 1.6f; amp *= 1 + flare * 0.8f; flare = Mathf.Max(0, flare - dt * 0.7f); }
            if (sleeping) { freq *= 0.4f; amp *= 0.5f; }
            phase += dt * freq;
            wag.x = phase;
            wag.y += (amp - wag.y) * Mathf.Min(1, dt * 4);
            // turning bends the body into a gentle C: tail tip offset up to ~15% of the length
            wag.z += (0.6f * Mathf.Clamp(-yawRate / 3, -1, 1) / sp.len - wag.z) * Mathf.Min(1, dt * 5);
            wag.w = sp.len;
            // pectorals: they scull while hovering and whenever they produce force, and flare to brake
            float hover = Mathf.Clamp01(1 - rel * 2.5f);
            pectS += (Mathf.Max(Mathf.Max(hover * 0.6f, pectF), idleScull) - pectS) * Mathf.Min(1, dt * 3);
            pectB += (pectBrake - pectB) * Mathf.Min(1, dt * 3);
            finPhase += dt * (4 + pectS * 6);
            pect = 0.35f + hover * 0.25f + pectB * 0.6f + Mathf.Sin(finPhase) * (0.12f + pectS * 0.45f) + flare * 0.5f;
            // growing up: the body follows the age-dependent size smoothly
            if (Mathf.Abs(targetScale - scale) > 1e-4f)
            {
                scale += (targetScale - scale) * Mathf.Min(1, dt * 0.5f);
                size = sp.len * scale;
            }
            scaleXYZ = Vector3.one * scale;
            if (gulp > 0)
            {
                gulp = Mathf.Max(0, gulp - dt * 3);
                float s = 1 + Mathf.Sin(gulp * Mathf.PI) * 0.08f;
                scaleXYZ = new Vector3(scale * s, scale * s, scale * (2 - s));
            }
        }

        public void Update(float dt)
        {
            if (hidden) return;
            stateT += dt;
            hunger = Mathf.Min(1, hunger + dt * 0.003f * (0.6f + p("gluttony")));
            fear = Mathf.Max(0, fear - dt * 0.2f);
            thinkT -= dt;
            if (thinkT <= 0) { thinkT = Rnd.R(0.25f, 0.6f); Think(); }
            Behave(dt);
            Steer(dt);
            Animate(dt);
        }

        public string StatusText()
        {
            var c = ctx;
            string Nm(Fish o) => o != null ? o.name : "кем-то";
            return state switch
            {
                FishState.IDLE => "Неспешно плавает",
                FishState.SCHOOL => "Плывёт со стаей",
                FishState.CRUISE => "Копается в грунте",
                FishState.EXPLORE => $"Исследует: {c.spot?.name ?? "аквариум"}",
                FishState.RETURN => $"Возвращается: {fav.name}",
                FishState.REST => eco.night > 0.6f && !sp.nocturnal ? "Спит" : "Отдыхает",
                FishState.HIDE => $"Прячется: {c.spot?.name}",
                FishState.SOCIALIZE => $"Проводит время с {Nm(c.other)}",
                FishState.FOLLOW => $"Следует за {Nm(c.other)}",
                FishState.PLAY => c.role == "chase" ? $"Догоняет {Nm(c.other)}" : $"Удирает от {Nm(c.other)}",
                FishState.BUBBLES => "Играет с пузырьками",
                FishState.WAIT_FOOD => "Ждёт корм",
                FishState.PATROL => "Охраняет территорию",
                FishState.DEFEND => $"Прогоняет {Nm(c.target)}",
                FishState.STANDOFF => $"Меряется характером с {Nm(c.rival)}",
                FishState.EAT => "Ловит корм!",
                FishState.FLEE => "Испугался!",
                FishState.CURIOUS => "С любопытством разглядывает",
                FishState.WATCH => "Наблюдает за вами",
                FishState.INVESTIGATE => $"Изучает: {c.spot?.name}",
                FishState.CIRCLE => "Кружит вокруг растения",
                FishState.CAROUSEL => "Карусель!",
                FishState.AIR => "Плывёт за глотком воздуха",
                FishState.SCRIPT => c.label ?? "Занят делом",
                FishState.NURSERY => independence > 0.5f ? "Осваивает окрестности" : "Держится рядом с братьями и сёстрами",
                FishState.COURT => $"Плавает со своей парой — {Nm(c.other)}",
                FishState.NEST => "Готовится к появлению мальков",
                FishState.GUARD => "Присматривает за мальками",
                _ => "Плавает",
            };
        }
    }
}
