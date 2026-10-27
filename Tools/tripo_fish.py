"""
Sculpted fish from Tripo (one mesh, ~2M triangles, 4K textures) -> the game's fish GLBs in
Assets/Aquarium/Models/Fish, laid out like the web version's make_fish.py: glTF +Z nose, +Y up,
body length FishSpecies len; Body = everything but the pectorals (its own eyes, solid fins),
PectL/PectR with the origin at the fin root. No Fins node, so FishModelBuilder puts the
pectorals into the body and draws no fin mesh.

  blender -b -P Tools/tripo_fish.py -- <preset> <tripo.glb> <out dir> <texture dir>

Then Aquarium -> Build Fish Models. The numbers in PRESETS are measured on each model (Tripo
units: x nose, y side, z up, after `flip` for a model sculpted nose to -x). A preset with colour variants writes one GLB per variant, the
texture's hue turned so the reference colour becomes the variant's.
"""
import bpy, bmesh, sys, os, math, colorsys, numpy as np
from mathutils import Matrix, Vector

PRESETS = {
    # "clownfish 3d model.glb"
    "clown": dict(len=0.46, nose=0.49, peduncle=-0.25, spine=0.30, cut=0.125, pect_x=(-0.02, 0.27), body_tris=14000, pect_tris=900),
    # "colorful fish 3d model.glb": its tail is bent to one side in the sculpt, straightened here.
    # Variants: the tail colours of the web version's guppies (species.json), the sculpt's own orange first.
    "guppy": dict(len=0.36, nose=0.49, peduncle=-0.11, spine=0.40, cut=0.095, pect_x=(0.12, 0.29), body_tris=9000, pect_tris=500,
                  straighten=True, ref="#ff6a3d",
                  variants=["#ff6a3d", "#ffb21f", "#3f8cff", "#ff3fa4", "#8a5bff", "#28d6a8", "#ff2f2f"]),
    # "colorful fish 3d model (1).glb": sculpted nose to -x
    "neon": dict(len=0.34, flip=True, nose=0.49, peduncle=-0.22, spine=0.33, cut=0.10, pect_x=(0.09, 0.25), body_tris=7000, pect_tris=400),
}
P0 = 0.45   # typical pectoral angle the shader applies

argv = sys.argv[sys.argv.index("--") + 1:]
name, src, out_dir, tex = argv[0], argv[1], argv[2], argv[3]
cfg = PRESETS[name]
L = cfg["len"]
S = (L / 2 + 0.47 * L) / (cfg["nose"] - cfg["peduncle"])   # nose at +L/2, peduncle at the shader's pivot -0.47 L

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src, merge_vertices=True)
ob = [o for o in bpy.context.scene.objects if o.type == 'MESH'][0]
ob.name = "Body"
me = ob.data
nv = len(me.vertices)
co = np.empty(nv * 3); me.vertices.foreach_get("co", co); co = co.reshape(-1, 3)
if cfg.get("flip"):   # half a turn about z: nose to +x
    co[:, :2] *= -1
    me.vertices.foreach_set("co", co.ravel())

if cfg.get("straighten"):
    # the middle of each slice along the body back onto y = 0
    edges = np.arange(co[:, 0].min(), co[:, 0].max() + 0.02, 0.02)
    mids, centers = [], []
    for a, b in zip(edges[:-1], edges[1:]):
        s = co[(co[:, 0] >= a) & (co[:, 0] < b)]
        if len(s): mids.append((a + b) / 2); centers.append(s[:, 1].mean())
    centers = np.convolve(np.pad(centers, 2, mode="edge"), np.ones(5) / 5, mode="valid")
    co[:, 1] -= np.interp(co[:, 0], mids, centers)
    me.vertices.foreach_set("co", co.ravel())
    print("STRAIGHTENED, tail shift", round(centers[0], 4), "nose shift", round(centers[-1], 4))

# pectorals: the part beyond |y| > cut connected to each fin tip
ed = np.empty(len(me.edges) * 2, int); me.edges.foreach_get("vertices", ed); ed = ed.reshape(-1, 2)
x0, x1 = cfg["pect_x"]
mask = (np.abs(co[:, 1]) > cfg["cut"]) & (co[:, 0] > x0) & (co[:, 0] < x1)
e = ed[mask[ed[:, 0]] & mask[ed[:, 1]]]
adj = [[] for _ in range(nv)]
for a, b in e: adj[a].append(b); adj[b].append(a)
fin = np.zeros(nv, bool)
ys = np.where(mask, co[:, 1], 0)
for tip in (int(np.argmax(ys)), int(np.argmin(ys))):
    st = [tip]; fin[tip] = True
    while st:
        v = st.pop()
        for w in adj[v]:
            if not fin[w]: fin[w] = True; st.append(w)
print("FIN VERTS", fin.sum())

pv = np.empty(len(me.loops), int); me.loops.foreach_get("vertex_index", pv)
ls = np.empty(len(me.polygons), int); me.polygons.foreach_get("loop_start", ls)
fsel = fin[pv[ls]] & fin[pv[ls + 1]] & fin[pv[ls + 2]]   # Tripo meshes are triangles
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
bm = bmesh.from_edit_mesh(me)
for seq in (bm.verts, bm.edges, bm.faces):
    for x in seq: x.select = False
bm.faces.ensure_lookup_table()
for i in np.flatnonzero(fsel): bm.faces[i].select = True
bmesh.update_edit_mesh(me)
bpy.ops.mesh.separate(type='SELECTED')
bpy.ops.object.mode_set(mode='OBJECT')
fins = [o for o in bpy.context.scene.objects if o.type == 'MESH' and o is not ob][0]
bpy.ops.object.select_all(action='DESELECT'); fins.select_set(True); bpy.context.view_layer.objects.active = fins
bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT'); bpy.ops.mesh.separate(type='LOOSE'); bpy.ops.object.mode_set(mode='OBJECT')
parts = sorted([o for o in bpy.context.scene.objects if o.type == 'MESH' and o is not ob], key=lambda o: len(o.data.vertices), reverse=True)
print("FIN PARTS", [len(o.data.vertices) for o in parts])
for o in parts[2:]: bpy.data.objects.remove(o)
pects = parts[:2]


def boundary_center(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    vs = {v for e in bm.edges if e.is_boundary for v in e.verts}
    c = sum((v.co for v in vs), Vector()) / max(1, len(vs)); bm.free()
    return c


def fill_holes(o):
    bm = bmesh.new(); bm.from_mesh(o.data)
    uvl = bm.loops.layers.uv.active
    old = set(bm.faces)
    bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
    new = [f for f in bm.faces if f not in old]
    bmesh.ops.triangulate(bm, faces=new)
    for f in bm.faces:
        if f in old: continue
        for l in f.loops:   # a cap takes the texture of the surface around it
            other = [m for m in l.vert.link_loops if m.face in old]
            if other: l[uvl].uv = other[0][uvl].uv
    bm.to_mesh(o.data); bm.free()


def decimate(o, tris):
    bpy.ops.object.select_all(action='DESELECT'); o.select_set(True); bpy.context.view_layer.objects.active = o
    m = o.modifiers.new("dec", 'DECIMATE'); m.ratio = min(1, tris / len(o.data.polygons)); m.use_collapse_triangulate = True
    bpy.ops.object.modifier_apply(modifier="dec")
    o.data.shade_smooth()
    print("DECIMATED", o.name, len(o.data.polygons))


roots = [boundary_center(o) for o in pects]
for o in [ob] + pects: fill_holes(o)
decimate(ob, cfg["body_tris"])
for o in pects: decimate(o, cfg["pect_tris"])

# Tripo (x nose, y side, z up) -> Blender export space (-y nose, x side, z up), game scale
M = Matrix.Translation((0, -(L / 2 - cfg["nose"] * S), -cfg["spine"] * S)) @ Matrix.Scale(S, 4) @ Matrix.Rotation(-math.pi / 2, 4, 'Z')
for o in [ob] + pects: o.data.transform(M)
roots = [M @ r for r in roots]

# Blender <-> Unity object space (glTF export y-up, glTFast mirrors x)
to_u = lambda b: Vector((-b.x, b.z, -b.y))
to_b = lambda u: Vector((-u.x, -u.z, u.y))
cx, sx = math.cos(0.3), math.sin(0.3)
for o, r in zip(pects, roots):
    o.name = "PectL" if r.x < 0 else "PectR"   # as make_fish.py: PectL at glTF x < 0
    side = 1 if o.name == "PectL" else -1       # FishModelBuilder's paddle direction
    a = side * P0; ca, sa = math.cos(a), math.sin(a)
    pu = to_u(r)
    for v in o.data.vertices:
        # undo the shader's paddle at the typical angle (rotate about Y, then tilt about X),
        # so the fin looks as sculpted when the game paddles it
        q = to_u(v.co) - pu
        y, z = cx * q.y + sx * q.z, -sx * q.y + cx * q.z
        x, z = ca * q.x - sa * z, sa * q.x + ca * z
        v.co = to_b(Vector((x, y, z)) + pu) - r
    o.location = r
    print("PECT", o.name, "root", tuple(round(c, 4) for c in r))


def find(sub): return [i for i in bpy.data.images if sub in i.name][0]


def pixels(img):
    a = np.empty(img.size[0] * img.size[1] * 4, np.float32); img.pixels.foreach_get(a)
    return a.reshape(img.size[1], img.size[0], 4)


def hue_turned(rgb, turn):
    """rgb (h, w, 3) with every hue turned by `turn` (0..1); greys stay grey."""
    mx, mn = rgb.max(-1), rgb.min(-1)
    d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-6), 0)
    r, g, b = rgb[..., 0], rgb[..., 1], rgb[..., 2]
    dd = np.maximum(d, 1e-6)
    h = np.where(mx == r, (g - b) / dd % 6, np.where(mx == g, (b - r) / dd + 2, (r - g) / dd + 4)) / 6
    h = (h + turn) % 1.0
    i = np.floor(h * 6).astype(int) % 6
    f = h * 6 - np.floor(h * 6)
    p, q, t = mx * (1 - s), mx * (1 - s * f), mx * (1 - s * (1 - f))
    sel = [i == k for k in range(6)]
    out = np.stack([np.select(sel, c) for c in ((mx, q, p, p, t, mx), (t, mx, mx, q, p, p), (p, p, t, mx, mx, q))], -1)
    return out


base, nrm = find("basecolor"), find("normal")
for img in (base, nrm): img.scale(1024, 1024)
nrm.filepath_raw = f"{tex}/{name}_body_n.png"; nrm.file_format = 'PNG'; nrm.save()
base_px = pixels(base)
os.makedirs(tex, exist_ok=True)

variants = cfg.get("variants") or [None]
for k, col in enumerate(variants):
    key = f"{name}-{k}" if col else name
    img = bpy.data.images.new(f"{key}_body", 1024, 1024)
    px = base_px.copy()
    if col and k > 0:
        hue = lambda hexs: colorsys.rgb_to_hsv(*(int(hexs[i:i + 2], 16) / 255 for i in (1, 3, 5)))[0]
        px[..., :3] = hue_turned(px[..., :3], hue(col) - hue(cfg["ref"]))
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = f"{tex}/{key}_body.jpg"; img.file_format = 'JPEG'; img.save()
    color = bpy.data.images.load(f"{tex}/{key}_body.jpg"); color.name = f"{key}_body"
    normal = bpy.data.images.load(f"{tex}/{name}_body_n.png"); normal.name = f"{key}_body_n"; normal.colorspace_settings.name = "Non-Color"

    mat = bpy.data.materials.new(f"{key}_body"); mat.use_nodes = True
    nt = mat.node_tree; bsdf = nt.nodes["Principled BSDF"]
    tb = nt.nodes.new("ShaderNodeTexImage"); tb.image = color
    tn = nt.nodes.new("ShaderNodeTexImage"); tn.image = normal
    nm = nt.nodes.new("ShaderNodeNormalMap")
    nt.links.new(tb.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tn.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], bsdf.inputs["Normal"])
    bsdf.inputs["Metallic"].default_value = 0; bsdf.inputs["Roughness"].default_value = 0.35
    for o in [ob] + pects:
        o.data.materials.clear(); o.data.materials.append(mat)

    bpy.ops.object.select_all(action='SELECT')
    path = os.path.join(out_dir, f"{key}.glb")
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", export_yup=True, export_apply=True,
                              export_texcoords=True, export_normals=True, export_materials="EXPORT",
                              export_image_format="AUTO", use_selection=False)
    print("WROTE", path, os.path.getsize(path))
