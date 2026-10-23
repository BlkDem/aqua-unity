// Shrimp, snails and the crab (the web version's critters.js): rigid parts with their origins at
// the joints, animated by turning them. Like the fish they live in the web version's coordinates;
// the root and every joint are mirrored into Unity's when set (see Joint).
using System.Collections.Generic;
using System.Linq;
using Aquarium.World;
using UnityEngine;
using Terrain = Aquarium.World.Terrain;

namespace Aquarium.Fish
{
    /// <summary>A joint of a critter model: its rest pose plus turns about axes in the parent's frame (web space).</summary>
    public sealed class Joint
    {
        public readonly Transform t;
        readonly Quaternion restWeb;

        public static Quaternion Mirror(Quaternion q) => new Quaternion(q.x, -q.y, -q.z, q.w);

        public Joint(Transform t)
        {
            this.t = t;
            restWeb = Mirror(t.localRotation);
        }

        /// <summary>Where the joint sits in its parent, in web space.</summary>
        public Vector3 LocalPosWeb => new Vector3(-t.localPosition.x, t.localPosition.y, t.localPosition.z);

        public void Set(params (Vector3 axis, float angle)[] turns)
        {
            var q = restWeb;
            foreach (var (axis, angle) in turns)
                if (angle != 0) q = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, axis) * q;
            t.localRotation = Mirror(q);
        }
    }

    public abstract class Critter
    {
        public static readonly Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
        protected static readonly (float front, float back, float side) Glass = (Terrain.D / 2 - 0.08f, -Terrain.D / 2 + 0.08f, Terrain.W / 2 - 0.08f);

        public readonly Ecosystem eco;
        public string kind, name, title;
        public int id;
        public Vector3 pos;
        public readonly Transform root;
        protected Quaternion rot = Quaternion.identity;   // web space
        protected readonly Dictionary<string, Transform> parts = new Dictionary<string, Transform>();

        protected Critter(Ecosystem eco, GameObject prefab, Transform parent, string name)
        {
            this.eco = eco;
            this.name = name;
            id = eco.nextId++;
            root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var model = Object.Instantiate(prefab, root);
            model.name = prefab.name;
            foreach (var t in model.GetComponentsInChildren<Transform>()) parts[t.name] = t;
            // a model whose only root node is Body is imported with Body as the model object itself
            if (!parts.ContainsKey("Body")) parts["Body"] = model.transform;
        }

        protected Joint J(string part) => new Joint(parts[part]);

        public abstract void Init(bool onGlass);
        public abstract void Update(float dt);
        public abstract void Scare(Vector3 from);

        /// <summary>Turn smoothly to stand on a surface with normal `up`, facing `fwd` (web space).</summary>
        protected void Orient(Vector3 up, Vector3 fwd, float k)
        {
            var a = fwd - up * Vector3.Dot(fwd, up);
            if (a.sqrMagnitude < 1e-6f) a = Vector3.Cross(up, Vector3.right);
            rot = Quaternion.Slerp(rot, Quaternion.LookRotation(a.normalized, up), k);
        }

        protected void Place()
        {
            root.localPosition = new Vector3(-pos.x, pos.y, pos.z);
            root.localRotation = Joint.Mirror(rot);
        }
    }

    // ============================================================ SHRIMP
    public sealed class Shrimp : Critter
    {
        enum S { WANDER, FOOD, FORAGE, HOP }
        S state = S.WANDER;
        float t, dur = 3, walk, clawPhase, clawAmp, walkAmp, tilt;
        Vector3 target, heading = Vector3.right;
        (Vector3 from, Vector3 to, float t, float dur, float h)? hop;
        FoodItem food;
        readonly Transform inner;
        readonly Joint[] segs;
        readonly Joint tail, antL, antR;
        readonly (Joint j, float side, int i)[] legs;
        readonly Joint[] claws;

        public Shrimp(Ecosystem eco, GameObject prefab, Transform parent, Color color, string name) : base(eco, prefab, parent, name)
        {
            kind = "shrimp";
            title = "Креветка";
            inner = root.GetChild(0);
            // the shell, legs and whiskers all take the shrimp's colour (cherry, blue, yellow...)
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                var mats = r.sharedMaterials.Select(m => { var c = new Material(m); c.SetColor("_BaseColor", color); return c; }).ToArray();
                r.sharedMaterials = mats;
            }
            segs = Enumerable.Range(0, 6).Select(i => J($"Seg{i}")).ToArray();
            tail = J("Tail");
            antL = J("AntL"); antR = J("AntR");
            legs = new[] { "L", "R" }.SelectMany(s => Enumerable.Range(0, 4).Select(i => (J($"Leg{s}{i}"), s == "L" ? 1f : -1f, i))).ToArray();
            claws = new[] { J("CheliL"), J("CheliR") };
            root.localScale = Vector3.one * Rnd.R(0.85f, 1.25f);
        }

        public override void Init(bool onGlass)
        {
            pos = eco.RandomSurfacePoint(true);
            PickTarget(null);
        }

        void PickTarget(string mode)
        {
            if (mode == "climb")
            {
                // the highest point within reach
                Vector3 best = pos;
                float bh = -1;
                for (int i = 0; i < 40; i++)
                {
                    var p = eco.RandomSurfacePoint(false, pos, 2.5f);
                    if (p.y > bh) { bh = p.y; best = p; }
                }
                target = best;
            }
            else target = eco.RandomSurfacePoint(false, pos, mode == "far" ? 4 : 1.2f);
        }

        public override void Scare(Vector3 from)
        {
            var a = pos - from; a.y = 0; a.Normalize();
            StartHop(eco.SurfacePoint(pos.x + a.x * Rnd.R(0.6f, 1.4f), pos.z + a.z * Rnd.R(0.4f, 1.0f)), 0.5f);
        }

        public void StartHop(Vector3 to, float h = 0.4f)
        {
            hop = (pos, to, 0, 0.6f + Vector3.Distance(pos, to) * 0.5f, h);
            state = S.HOP;
        }

        public override void Update(float dt)
        {
            t += dt;
            if (state == S.HOP && hop is var (from, to, ht, hdur, hh))
            {
                ht += dt / hdur;
                hop = (from, to, ht, hdur, hh);
                float k = Mathf.Min(1, ht), s = k * k * (3 - 2 * k);
                var a = Vector3.Lerp(from, to, s);
                a.y += Mathf.Sin(k * Mathf.PI) * (hh + Vector3.Distance(from, to) * 0.2f);
                var b = a - pos;
                if (b.sqrMagnitude > 1e-8f) heading = b.normalized;
                pos = a;
                Orient(Y, heading, 1 - Mathf.Exp(-8 * dt));
                // swimmerets flutter, a flick of the tail
                tail.Set((X, Mathf.Sin(t * 30) * 0.4f));
                foreach (var sg in segs) sg.Set((X, -0.1f + Mathf.Sin(t * 20) * 0.1f));
                AnimateLegs(0.3f);
                Place();
                if (k >= 1) { state = S.WANDER; PickTarget(null); }
                return;
            }
            // food on the ground?
            if (state != S.FOOD && eco.food.Count > 0 && Rnd.Chance(dt * 0.6f))
            {
                var f = eco.food.FirstOrDefault(x => !x.eaten && x.grounded && Vector3.Distance(x.pos, pos) < 2.5f);
                if (f != null) { state = S.FOOD; food = f; t = 0; }
            }
            if (state == S.FOOD)
            {
                if (food == null || food.eaten) { state = S.FORAGE; t = 0; dur = Rnd.R(2, 5); }
                else { target = food.pos; if (Vector3.Distance(pos, food.pos) < 0.09f) eco.EatFood(food, this); }
            }
            if (state == S.FORAGE)
            {
                // picking at the ground with the little claws
                clawPhase = t * 9;
                clawAmp = 1;
                tilt = 0.15f + Mathf.Sin(t * 2) * 0.05f;
                if (t > dur) Next();
                Stand(dt, 0);
                return;
            }
            tilt *= 0.9f;
            // walk towards the target over the surface
            var dir = target - pos; dir.y = 0;
            float d = dir.magnitude;
            if (d < 0.05f) { if (state != S.FOOD) Next(); Stand(dt, 0); return; }
            dir.Normalize();
            heading = Vector3.Lerp(heading, dir, 1 - Mathf.Exp(-4 * dt)).normalized;
            float sp = 0.16f * (state == S.FOOD ? 1.6f : 1);
            float nx = pos.x + heading.x * sp * dt, nz = pos.z + heading.z * sp * dt;
            float ny = eco.SurfaceAt(nx, nz);
            if (ny - pos.y > 0.25f) { StartHop(eco.SurfacePoint(target.x, target.z), 0.3f); return; }
            pos = new Vector3(nx, ny, nz);
            Stand(dt, sp);
        }

        void Next()
        {
            float r = Rnd.R();
            t = 0;
            if (r < 0.35f) { state = S.FORAGE; dur = Rnd.R(2, 7); }
            else if (r < 0.5f)
            {
                state = S.WANDER; PickTarget("climb");
                if (Rnd.Chance(0.2f)) eco.Tell("shrimp-climb", $"Креветка {name} карабкается повыше", target, 60, this);
            }
            else if (r < 0.58f) StartHop(eco.RandomSurfacePoint(false, pos, 3), 0.6f);
            else { state = S.WANDER; PickTarget(Rnd.Chance(0.2f) ? "far" : null); }
        }

        void Stand(float dt, float speed)
        {
            walk += speed * dt * 60;
            walkAmp = speed > 0 ? 1 : 0.1f;
            if (state != S.FORAGE) clawAmp *= 0.9f;
            Orient(eco.SurfaceNormal(pos.x, pos.z), heading, 1 - Mathf.Exp(-6 * dt));
            AnimateLegs(walkAmp);
            tail.Set((X, Mathf.Sin(eco.time * 1.5f + id) * 0.1f));
            foreach (var sg in segs) sg.Set((X, Mathf.Sin(eco.time * 1.1f + id) * 0.03f));
            inner.localRotation = WebSpace.Rot(tilt, 0, 0);
            Place();
        }

        void AnimateLegs(float amp)
        {
            // the pairs step in turn, the two sides in opposite phase; each leg swings and lifts
            foreach (var (j, side, i) in legs)
            {
                float sw = Mathf.Sin(walk + i * 1.1f + (side > 0 ? 0 : Mathf.PI)) * amp;
                j.Set((Y, sw * 0.35f * side), (new Vector3(0, 0, side), Mathf.Max(0, sw) * 0.3f));
            }
            for (int k = 0; k < 2; k++)
                claws[k].Set((X, Mathf.Sin(clawPhase + k * Mathf.PI) * clawAmp * 0.45f));
            antL.Set((Y, Mathf.Sin(eco.time * 1.3f + id) * 0.15f), (X, Mathf.Sin(eco.time * 0.9f + id) * 0.1f));
            antR.Set((Y, Mathf.Sin(eco.time * 1.2f + id + 1) * 0.15f), (X, Mathf.Sin(eco.time * 0.8f + id + 2) * 0.1f));
        }
    }

    // ============================================================ SNAIL
    public sealed class Snail : Critter
    {
        static readonly Dictionary<string, (string title, float scale)> Types = new Dictionary<string, (string, float)>
        {
            ["mystery"] = ("Ампулярия", 1.35f), ["nerite"] = ("Улитка-нерита", 1.6f), ["ramshorn"] = ("Катушка", 1.5f),
        };
        // apple snails come in several colours; the tint is multiplied over the shell's texture
        static readonly Color[] ShellTints = { new Color(1, 1, 1), new Color(0.78f, 0.6f, 0.45f), new Color(1.04f, 1.02f, 0.9f), new Color(0.85f, 0.95f, 0.78f) };

        public readonly string type;
        public string mode = "ground";
        string pane;
        float u, v, ang, retract, t, crawl, pause;
        public bool reachedTop, goalTop;
        Vector3 heading = Vector3.right, normal = Vector3.up;
        Vector3? goal;
        FoodItem food;
        readonly Transform inner, shell;
        readonly Vector3 shellRest;
        readonly Joint[] tents;

        public Snail(Ecosystem eco, GameObject prefab, Transform parent, string type, string name) : base(eco, prefab, parent, name)
        {
            kind = "snail";
            this.type = type;
            title = Types[type].title;
            inner = root.GetChild(0);
            shell = parts["Shell"];
            shellRest = shell.localPosition;
            if (type == "mystery")
            {
                var r = shell.GetComponent<MeshRenderer>();
                r.sharedMaterials = r.sharedMaterials.Select(m => { var c = new Material(m); c.SetColor("_BaseColor", ShellTints[id % ShellTints.Length]); return c; }).ToArray();
            }
            tents = new[] { J("TentL"), J("TentR") };
            root.localScale = Vector3.one * Types[type].scale * Rnd.R(0.9f, 1.1f);
            ang = Rnd.R(0, 6.28f);
        }

        public override void Init(bool onGlass)
        {
            if (onGlass)
            {
                mode = "glass";
                pane = Rnd.Pick(new[] { "back", "front", "left", "right" });
                u = Rnd.R(-4, 4); v = Rnd.R(1.5f, 4.5f);
                ang = Rnd.R(0, 6.28f);
            }
            else pos = eco.RandomSurfacePoint(true);
        }

        public override void Scare(Vector3 from) { retract = 1; pause = Rnd.R(3, 6); }

        (System.Func<float, float, Vector3> p, Vector3 n, float umax) PaneFrame() => pane switch
        {
            "back" => ((a, b) => new Vector3(a, b, Glass.back), new Vector3(0, 0, 1), 5.6f),
            "front" => ((a, b) => new Vector3(a, b, Glass.front), new Vector3(0, 0, -1), 5.6f),
            "left" => ((a, b) => new Vector3(-Glass.side, b, a), new Vector3(1, 0, 0), 2.1f),
            _ => ((a, b) => new Vector3(Glass.side, b, a), new Vector3(-1, 0, 0), 2.1f),
        };

        public override void Update(float dt)
        {
            t += dt;
            if (pause > 0) pause -= dt;
            else retract = Mathf.Max(0, retract - dt * 0.6f);
            float speed = pause > 0 ? 0 : 0.035f * (1 - retract);
            Animate(dt, speed);

            // food on the ground nearby
            if (food == null && eco.food.Count > 0 && Rnd.Chance(dt * 0.3f))
            {
                var f = eco.food.FirstOrDefault(x => !x.eaten && x.grounded && Vector3.Distance(x.pos, pos) < 1.2f);
                if (f != null)
                {
                    food = f;
                    if (mode == "ground" && Rnd.Chance(0.5f)) eco.Tell("snail-food", $"Корм упал прямо перед улиткой {name}!", f.pos, 60, this);
                }
            }
            if (food != null && (food.eaten || mode != "ground")) food = null;

            if (mode == "glass")
            {
                var F = PaneFrame();
                ang += (Rnd.R() - 0.5f) * dt * 0.8f;
                if (goalTop) ang += (Mathf.PI / 2 - ang) * dt * 0.5f;
                u += Mathf.Cos(ang) * speed * dt;
                v += Mathf.Sin(ang) * speed * dt;
                if (Mathf.Abs(u) > F.umax) { u = Mathf.Sign(u) * F.umax; ang = Mathf.PI - ang; }
                if (v > 5.5f)
                {
                    v = 5.5f; ang = -Mathf.PI / 2 + Rnd.R(-0.6f, 0.6f);
                    if (!reachedTop) { reachedTop = true; eco.Tell("snail-top", $"Улитка {name} добралась до самого верха стекла!", pos, 120, this, true); }
                    goalTop = false;
                }
                var bottom = F.p(u, 0);
                float floor = eco.HeightAt(bottom.x, bottom.z);
                if (v < floor + 0.02f)
                {
                    if (Rnd.Chance(0.5f))
                    {
                        mode = "ground";
                        pos = F.p(u, floor) + F.n * 0.12f;
                        pos.y = eco.SurfaceAt(pos.x, pos.z);
                        heading = F.n;
                    }
                    else { v = floor + 0.03f; ang = Mathf.PI / 2 + Rnd.R(-0.6f, 0.6f); }
                }
                if (v > 4.5f) reachedTop = reachedTop && v > 4.0f;
                pos = F.p(u, v) + F.n * 0.005f;
                // forward direction on the pane
                var tangU = pane == "back" || pane == "front" ? new Vector3(1, 0, 0) : new Vector3(0, 0, 1);
                var fwd = tangU * Mathf.Cos(ang) + new Vector3(0, Mathf.Sin(ang), 0);
                Orient(F.n, fwd, 1 - Mathf.Exp(-3 * dt));
                Place();
                return;
            }
            // ground mode
            if (!goal.HasValue || Vector3.Distance(pos, goal.Value) < 0.05f || food != null)
                goal = food != null ? food.pos : eco.RandomSurfacePoint(false, pos, 1.5f);
            if (food != null && Vector3.Distance(pos, food.pos) < 0.08f) { eco.EatFood(food, this); food = null; pause = Rnd.R(2, 4); }
            var a = goal.Value - pos; a.y = 0; a.Normalize();
            heading = Vector3.Lerp(heading, a, 1 - Mathf.Exp(-1.5f * dt)).normalized;
            float nx = pos.x + heading.x * speed * dt, nz = pos.z + heading.z * speed * dt;
            pos = new Vector3(nx, eco.SurfaceAt(nx, nz), nz);
            normal = Vector3.Lerp(normal, eco.SurfaceNormal(nx, nz), 1 - Mathf.Exp(-3 * dt)).normalized;
            Orient(normal, heading, 1 - Mathf.Exp(-3 * dt));
            Place();
            // reached a wall -> climb the glass
            string nearWall = Mathf.Abs(nx) > Glass.side - 0.2f ? (nx > 0 ? "right" : "left") : nz > Glass.front - 0.2f ? "front" : nz < Glass.back + 0.2f ? "back" : null;
            if (nearWall != null && Rnd.Chance(dt * 0.5f)) ToGlass(nearWall);
        }

        /// <summary>Tentacles feel about (and pull in when scared); the foot ripples and the shell sways as it glides.</summary>
        void Animate(float dt, float speed)
        {
            crawl += speed * dt * 60;
            for (int i = 0; i < 2; i++)
            {
                float s = i == 1 ? -1 : 1;
                tents[i].Set((X, Mathf.Sin(t * 1.3f + i * 1.7f) * 0.18f - 0.05f), (Z, s * Mathf.Sin(t * 0.9f + i) * 0.18f));
                tents[i].t.localScale = new Vector3(1, 1 - retract * 0.85f, 1);
            }
            float glide = Mathf.Min(1, speed / 0.03f);
            inner.localScale = new Vector3(1, 1, 1 + Mathf.Sin(crawl * 0.9f) * 0.025f * glide);
            shell.localRotation = WebSpace.Rot(Mathf.Sin(t * 0.7f) * 0.02f - retract * 0.12f, 0, Mathf.Sin(crawl * 0.45f + 1) * 0.04f * glide);
            shell.localPosition = shellRest - new Vector3(0, retract * 0.006f, 0);
        }

        void ToGlass(string p)
        {
            mode = "glass"; pane = p;
            u = p == "back" || p == "front" ? pos.x : pos.z;
            v = pos.y + 0.05f;
            ang = Mathf.PI / 2 + Rnd.R(-0.5f, 0.5f);
        }

        /// <summary>A long trip: across the sand, or up the glass to the top.</summary>
        public void Journey()
        {
            if (mode == "ground") goal = eco.RandomSurfacePoint(false, pos, 5);
            else goalTop = true;
        }
    }

    // ============================================================ CRAB
    /// <summary>A walking leg: hip (swing and lift), knee and ankle, all hinged across the leg.</summary>
    sealed class CrabLeg
    {
        readonly Joint[] j;
        readonly Vector3 hinge;
        readonly float s, phase;

        public CrabLeg(Joint[] joints, string side, int i)
        {
            j = joints;
            var outDir = j[1].LocalPosWeb; outDir.y = 0; outDir.Normalize();   // horizontal direction of the leg
            hinge = Vector3.Cross(outDir, Vector3.up).normalized;               // + lifts the leg
            s = side == "L" ? 1 : -1;
            // alternating tetrapod gait: L0 R1 L2 R3 step together, then the other four
            phase = ((i + (side == "L" ? 0 : 1)) % 2) * Mathf.PI + i * 0.25f;
        }

        public void Pose(float walk, float amp, float dir, float rest)
        {
            float ph = walk + phase;
            float lift = Mathf.Max(0, Mathf.Sin(ph)) * amp;
            // reach: the tip moves along the walking direction in the air and back on the ground
            float reach = Mathf.Cos(ph) * amp * dir * s;
            j[0].Set((hinge, 0.42f * lift - 0.1f * reach + rest * 0.05f), (Vector3.up, 0.1f * Mathf.Sin(ph) * amp));
            j[1].Set((hinge, 0.28f * reach - 0.2f * lift - rest * 0.08f));
            j[2].Set((hinge, 0.12f * reach + 0.15f * lift));
        }
    }

    public sealed class Crab : Critter
    {
        float facing, t, wave, walk, amp, dir = 1, scared, pick, fast, idle;
        Vector3? goal;
        FoodItem food;
        readonly Transform body;
        readonly float bodyY;
        readonly List<CrabLeg> legs = new List<CrabLeg>();
        readonly (Joint[] j, float s, Vector3 open)[] claws;
        readonly Joint[] eyes;

        public Crab(Ecosystem eco, GameObject prefab, Transform parent, string name) : base(eco, prefab, parent, name)
        {
            kind = "crab";
            title = "Крабик";
            body = parts["Body"];
            bodyY = body.localPosition.y;
            foreach (var side in new[] { "L", "R" })
                for (int i = 0; i < 4; i++)
                    legs.Add(new CrabLeg(Enumerable.Range(0, 3).Select(k => J($"Leg{side}{i}_{k}")).ToArray(), side, i));
            claws = new[] { "L", "R" }.Select(side =>
            {
                var j = Enumerable.Range(0, 4).Select(k => J($"Claw{side}_{k}")).ToArray();
                // the movable finger opens about an axis across the palm
                var palmDir = j[3].LocalPosWeb.normalized;
                return (j, side == "L" ? 1f : -1f, Vector3.Cross(palmDir, Vector3.up).normalized);
            }).ToArray();
            eyes = new[] { J("EyeL"), J("EyeR") };
            root.localScale = Vector3.one * 0.95f;
            facing = Rnd.R(0, 6.28f);
        }

        public override void Init(bool onGlass) => pos = eco.RandomSurfacePoint(true);

        public override void Scare(Vector3 from)
        {
            var a = pos - from; a.y = 0; a.Normalize();
            goal = eco.SurfacePoint(pos.x + a.x * 1.2f, pos.z + a.z * 1.2f);
            fast = 2; idle = 0; scared = 1.5f; food = null;
        }

        public override void Update(float dt)
        {
            t += dt;
            fast = Mathf.Max(0, fast - dt);
            scared = Mathf.Max(0, scared - dt);
            // a flake on the ground nearby: go and pick at it with the claws
            if (food == null && fast <= 0 && eco.food.Count > 0 && Rnd.Chance(dt * 0.5f))
            {
                var f = eco.food.FirstOrDefault(x => !x.eaten && x.grounded && Vector3.Distance(x.pos, pos) < 1.8f);
                if (f != null) { food = f; goal = f.pos; idle = 0; }
            }
            if (food != null)
            {
                if (food.eaten) { food = null; goal = null; }
                else if (Vector3.Distance(pos, food.pos) < 0.12f)
                {
                    eco.EatFood(food, this);
                    food = null; goal = null; idle = Rnd.R(2, 3.5f); pick = idle;
                }
            }
            if (idle > 0) idle -= dt;
            else if (!goal.HasValue || Vector3.Distance(pos, goal.Value) < 0.05f)
            {
                goal = eco.RandomSurfacePoint(true, pos, 1.5f);
                idle = Rnd.Chance(0.4f) ? Rnd.R(1, 4) : 0;
                if (Rnd.Chance(0.15f)) wave = 2.5f;
            }
            float speed = 0;
            if (!(idle > 0) && goal.HasValue)   // (someone else may have eaten the food it was going for)
            {
                var a = goal.Value - pos; a.y = 0;
                float d = a.magnitude; a.Normalize();
                speed = (fast > 0 ? 0.45f : 0.14f) * Mathf.Min(1, d * 4);
                // crabs walk sideways: the body faces across the way it goes
                float targetAng = Mathf.Atan2(a.x, a.z) + Mathf.PI / 2;
                float dd = targetAng - facing; dd = Mathf.Atan2(Mathf.Sin(dd), Mathf.Cos(dd));
                float side = 1;
                if (Mathf.Abs(dd) > Mathf.PI / 2) { dd -= Mathf.Sign(dd) * Mathf.PI; side = -1; }
                facing += dd * Mathf.Min(1, dt * 2);
                dir = side;   // walking towards the body's left (+1) or right (-1)
                float nx = pos.x + a.x * speed * dt, nz = pos.z + a.z * speed * dt;
                pos = new Vector3(nx, eco.SurfaceAt(nx, nz), nz);
            }
            Animate(dt, speed);
            Orient(eco.SurfaceNormal(pos.x, pos.z), new Vector3(Mathf.Sin(facing), 0, Mathf.Cos(facing)), 1 - Mathf.Exp(-5 * dt));
            Place();
        }

        void Animate(float dt, float speed)
        {
            amp += ((speed > 0.01f ? 1 : 0) - amp) * Mathf.Min(1, dt * 6);
            walk += speed * dt * 95;
            float breathe = Mathf.Sin(t * 1.4f) * 0.5f + 0.5f;
            foreach (var L in legs) L.Pose(walk, amp, dir, breathe * (1 - amp));
            // the body bobs with the steps and crouches when frightened
            body.localPosition = new Vector3(body.localPosition.x, bodyY + Mathf.Abs(Mathf.Sin(walk)) * 0.004f * amp - scared * 0.008f, body.localPosition.z);
            wave = Mathf.Max(0, wave - dt);
            pick = Mathf.Max(0, pick - dt);
            for (int i = 0; i < claws.Length; i++)
            {
                var (j, s, openAxis) = claws[i];
                float raise = Mathf.Sin(t * 1.2f + i) * 0.05f, swing = 0, wrist = 0, open = 0.08f + Mathf.Sin(t * 0.8f + i * 2) * 0.06f;
                if (wave > 0)
                {
                    // a threat display: claws up and open, snapping
                    raise = -0.75f + Mathf.Sin(t * 6 + i * 3) * 0.25f;
                    open = 0.45f + Mathf.Sin(t * 9 + i) * 0.3f;
                }
                else if (pick > 0)
                {
                    // picking at the ground and bringing the morsel to the mouth, claws taking turns
                    float k = Mathf.Sin(t * 5 + i * Mathf.PI);
                    raise = k > 0 ? -0.35f * k : 0.15f * -k;
                    swing = s * 0.35f * Mathf.Max(0, k);
                    wrist = s * 0.3f * Mathf.Max(0, k);
                    open = k < 0 ? 0.4f * -k : 0.05f;
                }
                else if (scared > 0) { raise = -0.3f; open = 0.5f; }   // claws up in defence
                j[0].Set((X, raise), (Y, swing - amp * s * 0.05f));
                j[1].Set((Y, wrist));
                j[2].Set((X, Mathf.Sin(t * 0.7f + i) * 0.05f));
                j[3].Set((openAxis, open));
            }
            // eyes on stalks: look about, fold down when frightened
            for (int i = 0; i < 2; i++)
                eyes[i].Set((X, scared > 0 ? 0.9f : Mathf.Sin(t * 0.6f + i * 2) * 0.12f), (Y, Mathf.Sin(t * 0.43f + i) * 0.2f));
        }
    }

    public static class CritterFactory
    {
        public static List<Critter> Build(Ecosystem eco, CritterModelSet set, Transform parent)
        {
            var list = new List<Critter>();
            if (!set) return list;
            var shrimpCols = new[] { "#ff2a2a", "#ff3b30", "#e8101e", "#ff5a36", "#2f7dff", "#ffd400", "#ff2a6a", "#c9d6d9" };
            var shrimpNames = new[] { "Креветыч", "Вишенка", "Клешня", "Усик", "Малинка", "Голубика", "Лимончик", "Призрачка" };
            if (set.shrimp)
                for (int i = 0; i < shrimpCols.Length; i++)
                {
                    ColorUtility.TryParseHtmlString(shrimpCols[i], out var c);
                    list.Add(new Shrimp(eco, set.shrimp, parent, c, shrimpNames[i]));
                }
            foreach (var (type, name, glass) in new[] { ("mystery", "Галя", true), ("nerite", "Зебра", true), ("ramshorn", "Завиток", false), ("nerite", "Тихоход", false), ("mystery", "Турбо", true), ("ramshorn", "Колесо", false) })
            {
                if (!set.Snail(type)) continue;
                var s = new Snail(eco, set.Snail(type), parent, type, name);
                s.Init(glass);
                list.Add(s);
            }
            if (set.crab) list.Add(new Crab(eco, set.crab, parent, "Краб Валера"));
            foreach (var c in list) if (c.kind != "snail") c.Init(false);
            return list;
        }
    }
}
