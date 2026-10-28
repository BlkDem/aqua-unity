// How the fish get on with the decor: a fresh aquarium swims for a while (in memory), and every
// frame each fish's body and nose are measured against the decor's real shapes (the water field).
//   Unity.exe -batchmode -projectPath <project> -executeMethod Aquarium.EditorTools.Dev.SwimCheck -quit
using System;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Aquarium.EditorTools
{
    public static partial class Dev
    {
        public static void SwimCheck()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var app = UnityEngine.Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(11);
            var eco = app.Eco;
            var field = eco.field;
            if (field == null) { Debug.LogError("SWIM no water field in the scene"); return; }
            Vector3 Spot(string name) => eco.spots.First(s => s.name == name).pos;
            var hold = Spot("трюм корабля");
            var arch = Spot("каменная арка");
            const float dt = 1 / 30f;
            int frames = 0, bodyIn = 0, noseIn = 0, inHold = 0, inArch = 0;
            float worst = 1;
            int early = 0, stuck = 0, axisIn = 0, halfIn = 0;
            var halfWho = new System.Collections.Generic.Dictionary<string, int>();
            var stuckT = new System.Collections.Generic.Dictionary<Aquarium.Fish.Fish, float>();
            var stuckWho = new System.Collections.Generic.List<string>();
            var where = new System.Collections.Generic.Dictionary<string, int>();
            var who = new System.Collections.Generic.Dictionary<string, int>();
            string worstWho = "";
            for (int i = 0; i < 30 * 600; i++)
            {
                app.Simulate(dt);
                foreach (var f in eco.fish)
                {
                    if (f.hidden) continue;
                    frames++;
                    float body = field.Distance(f.pos), nose = field.Distance(f.pos + f.heading * (f.size * 0.4f));
                    if (body < 0) { bodyIn++; var key = $"{f.sp.id} {f.state}"; who[key] = who.TryGetValue(key, out var c) ? c + 1 : 1; if (i < 30) early++; var at = $"({Mathf.Round(f.pos.x * 2) / 2}, {Mathf.Round(f.pos.y * 2) / 2}, {Mathf.Round(f.pos.z * 2) / 2})"; where[at] = where.TryGetValue(at, out var w) ? w + 1 : 1; }
                    if (nose < 0) noseIn++;
                    if (body - f.Girth < worst) { worst = body - f.Girth; worstWho = $"{f.sp.id} at {f.pos} ({f.state}, t={i * dt:0}s)"; }
                    if ((f.pos - hold).magnitude < 0.45f) inHold++;
                    // along the whole body, nose to tail: is its axis in the decor, or half its thickness?
                    bool axis = false, half = false;
                    foreach (var (at, k) in Aquarium.Fish.Fish.Profile)
                    {
                        float dd = field.Distance(f.pos + f.heading * (f.size * at));
                        if (dd < 0) axis = true;
                        if (dd < f.Girth * k * 0.5f) half = true;
                    }
                    if (axis) axisIn++;
                    if (half) { halfIn++; var hk = $"{f.sp.id}"; halfWho[hk] = halfWho.TryGetValue(hk, out var hc) ? hc + 1 : 1; }
                    // stuck: barely moving against the decor while meaning to swim
                    bool resting = f.state == Aquarium.Fish.FishState.REST || f.state == Aquarium.Fish.FishState.HIDE || f.state == Aquarium.Fish.FishState.IDLE;
                    stuckT[f] = !resting && f.vel.magnitude < 0.05f && body < f.Girth + 0.1f ? (stuckT.TryGetValue(f, out var st) ? st : 0) + dt : 0;
                    if (stuckT[f] > 3 && stuckT[f] - dt <= 3) { stuck++; stuckWho.Add($"{f.sp.id} {f.state} at {f.pos} d={body:0.00} girth={f.Girth:0.00} goal {f.goal} d={field.Distance(f.goal):0.00} floor={eco.HeightAt(f.pos.x, f.pos.z):0.00} vel={f.vel} escaping={f.Escaping:0.0} exit={field.ExitAt(f.pos)} way={field.WayOut(f.pos)}"); }
                    if ((f.pos - arch).magnitude < 0.35f) inArch++;
                }
            }
            Debug.Log($"SWIM {eco.fish.Count} fish, {frames} fish-frames: body inside the decor {bodyIn}, nose inside {noseIn}; in the ship's hold {inHold}, under the arch {inArch}");
            Debug.Log($"SWIM along the body: its axis in the decor {axisIn} fish-frames, half its thickness in {halfIn} (" + string.Join(", ", halfWho.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")) + ")");
            Debug.Log($"SWIM inside in the first second: {early}; by fish and state: " + string.Join(", ", who.OrderByDescending(kv => kv.Value).Take(12).Select(kv => $"{kv.Key} {kv.Value}")));
            Debug.Log("SWIM inside where: " + string.Join(", ", where.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key} {kv.Value}")));
            Debug.Log($"SWIM stuck against the decor for 3 s or more: {stuck} times " + string.Join("; ", stuckWho.Take(8)));
            Debug.Log($"SWIM closest: {worst:0.000} m past the body's girth — {worstWho}");
        }

        /// <summary>How the walkers stand on the decor: under each one's head, middle and tail the real
        /// surface (a ray down onto the colliders) against where the body is.</summary>
        public static void WalkCheck()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var app = UnityEngine.Object.FindAnyObjectByType<Aquarium.Fish.AquariumApp>();
            app.Boot(new Aquarium.Life.MemoryWorldStore());
            UnityEngine.Random.InitState(11);
            var eco = app.Eco;
            var layout = UnityEngine.Object.FindAnyObjectByType<Aquarium.World.AquariumLayout>();
            float Under(Vector3 p) => layout.SurfaceAt(-p.x, p.z) is var s && Physics.Raycast(new Vector3(-p.x, p.y + 0.12f, p.z), Vector3.down, out var hit, 2, layout.walkable, QueryTriggerInteraction.Ignore) ? Mathf.Max(hit.point.y, Aquarium.World.Terrain.SandHeight(p.x, p.z)) : Aquarium.World.Terrain.SandHeight(p.x, p.z);
            const float dt = 1 / 30f;
            var frames = new System.Collections.Generic.Dictionary<string, int>();
            var sunk = new System.Collections.Generic.Dictionary<string, int>();
            var why = new System.Collections.Generic.Dictionary<string, int>();
            var lastY = new System.Collections.Generic.Dictionary<Aquarium.Fish.Critter, float>();
            var pops = new System.Collections.Generic.Dictionary<string, int>();
            float maxPop = 0; string popAt = "";
            var floats = new System.Collections.Generic.Dictionary<string, int>();
            float maxGap = 0; string gapAt = "";
            var worst = new System.Collections.Generic.Dictionary<string, (float d, string at)>();
            for (int i = 0; i < 30 * 300; i++)
            {
                app.Simulate(dt);
                foreach (var c in eco.critters)
                {
                    float rise = lastY.TryGetValue(c, out var ly) ? c.pos.y - ly : 0;
                    lastY[c] = c.pos.y;
                    if (c is Aquarium.Fish.Snail sn && sn.mode != "ground") continue;
                    if (rise > 0.1f && c.Status != "Прыгает") { pops[c.kind] = pops.TryGetValue(c.kind, out var pn) ? pn + 1 : 1; if (rise > maxPop) { maxPop = rise; popAt = $"{c.kind} at {c.pos}"; } }
                    var fwd = c.Forward; fwd.y = 0; fwd.Normalize();
                    var side = new Vector3(fwd.z, 0, -fwd.x);
                    // how far the body's lowest line (its middle, head, tail and sides on the slope it stands on) is below the real surface
                    float half = c.Half;
                    var up = c.root.up; up.x = -up.x;   // web space
                    float Sunk(Vector3 o) { var p = c.pos + o; float body = c.pos.y - Vector3.Dot(o, up) / Mathf.Max(up.y, 0.3f); return Under(new Vector3(p.x, body, p.z)) - body; }
                    float d = Mathf.Max(Mathf.Max(Sunk(Vector3.zero), Sunk(fwd * half)), Mathf.Max(Sunk(-fwd * half), Mathf.Max(Sunk(side * half * 0.6f), Sunk(-side * half * 0.6f))));
                    frames[c.kind] = frames.TryGetValue(c.kind, out var n) ? n + 1 : 1;
                    // floating: no point of the body within 3 cm of the ground under it
                    float gap = Mathf.Min(Mathf.Min(-Sunk(Vector3.zero), -Sunk(fwd * half)), Mathf.Min(-Sunk(-fwd * half), Mathf.Min(-Sunk(side * half * 0.6f), -Sunk(-side * half * 0.6f))));
                    if (gap > 0.03f && c.Status != "Прыгает") { floats[c.kind] = floats.TryGetValue(c.kind, out var fl) ? fl + 1 : 1; if (gap > maxGap) { maxGap = gap; gapAt = $"{c.kind} at {c.pos}"; } }
                    if (d > 0.02f) { sunk[c.kind] = sunk.TryGetValue(c.kind, out var m) ? m + 1 : 1; var key = $"{c.kind} {c.Status} near ({Mathf.Round(c.pos.x * 2) / 2}, {Mathf.Round(c.pos.y * 2) / 2}, {Mathf.Round(c.pos.z * 2) / 2})"; why[key] = why.TryGetValue(key, out var wn) ? wn + 1 : 1; }
                    if (!worst.TryGetValue(c.kind, out var w) || d > w.d) worst[c.kind] = (d, $"{c.pos}");
                }
            }
            Debug.Log($"WALK jumps up by more than 10 cm in a frame, not hopping: " + string.Join(", ", pops.Select(kv => $"{kv.Key} {kv.Value}")) + $"; the biggest {maxPop:0.00} m, {popAt}");
            Debug.Log($"WALK floating more than 3 cm above the ground, not hopping: " + string.Join(", ", floats.Select(kv => $"{kv.Key} {kv.Value}")) + $"; the most {maxGap:0.00} m, {gapAt}");
            Debug.Log("WALK sunk while: " + string.Join(", ", why.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}")));
            foreach (var k in frames.Keys)
                Debug.Log($"WALK {k}: {frames[k]} frames, sunk more than 2 cm in {(sunk.TryGetValue(k, out var m) ? m : 0)}, worst {worst[k].d:0.000} m at {worst[k].at}");
        }

        /// <summary>Where each piece of decor is (bounds in the web version's coordinates).</summary>
        public static void DecorBounds()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            foreach (var t in GameObject.Find("Decor").transform.Cast<Transform>())
                foreach (var c in t.GetComponentsInChildren<Collider>())
                {
                    var b = c.bounds;
                    Debug.Log($"BOUNDS {t.name}/{c.name}: x {-b.max.x:0.00}..{-b.min.x:0.00}, y {b.min.y:0.00}..{b.max.y:0.00}, z {b.min.z:0.00}..{b.max.z:0.00}");
                }
        }

        /// <summary>Which pieces of decor run into each other: the vertices of each piece's colliders inside another's
        /// (AQ_PIECE: only this piece, e.g. driftwood).</summary>
        public static void OverlapProbe()
        {
            EditorSceneManager.OpenScene(SceneBuilder.ScenePath, OpenSceneMode.Single);
            var decor = GameObject.Find("Decor").transform;
            Physics.SyncTransforms();
            string only = System.Environment.GetEnvironmentVariable("AQ_PIECE");
            foreach (var piece in decor.Cast<Transform>().Where(t => only == null || t.name == only))
            {
                var own = new System.Collections.Generic.HashSet<Collider>(piece.GetComponentsInChildren<Collider>());
                var hits = new System.Collections.Generic.Dictionary<string, int>();
                var box = new System.Collections.Generic.Dictionary<string, (Vector3 lo, Vector3 hi)>();
                foreach (var c in own.OfType<MeshCollider>())
                    foreach (var v in c.sharedMesh.vertices)
                        foreach (var o in Physics.OverlapSphere(c.transform.TransformPoint(v), 0.02f))
                            if (!own.Contains(o))
                            {
                                var key = o.transform.GetComponentsInParent<Transform>().First(t => t.parent == decor).name;
                                hits[key] = hits.TryGetValue(key, out var n) ? n + 1 : 1;
                                var w = c.transform.TransformPoint(v); w.x = -w.x;
                                box[key] = box.TryGetValue(key, out var bb) ? (Vector3.Min(bb.lo, w), Vector3.Max(bb.hi, w)) : (w, w);
                            }
                if (hits.Count > 0) Debug.Log($"OVERLAP {piece.name} runs into " + string.Join(", ", hits.Select(kv => $"{kv.Key} ({kv.Value} vertices, {box[kv.Key].lo:F2}..{box[kv.Key].hi:F2})")));
            }
            Debug.Log("OVERLAP done");
        }
    }
}
