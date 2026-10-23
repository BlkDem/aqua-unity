"""
Re-pack GLB files whose textures are WebP (EXT_texture_webp, as the web version's Blender
export writes them) with PNG textures, which glTFast imports. Geometry, materials and node
names are untouched.

  python3 Tools/glb_webp_to_png.py <src.glb or dir> <dst dir>
"""
import io
import json
import os
import struct
import sys

from PIL import Image


def read_glb(path):
    b = open(path, 'rb').read()
    magic, version, length = struct.unpack('<III', b[:12])
    assert magic == 0x46546C67, 'not a GLB'
    off, js, binc = 12, None, b''
    while off < length:
        clen, ctype = struct.unpack('<II', b[off:off + 8])
        data = b[off + 8:off + 8 + clen]
        if ctype == 0x4E4F534A:
            js = json.loads(data)
        elif ctype == 0x004E4942:
            binc = data
        off += 8 + clen
    return js, binc


def write_glb(path, js, binc):
    jb = json.dumps(js, separators=(',', ':')).encode()
    jb += b' ' * (-len(jb) % 4)
    binc += b'\0' * (-len(binc) % 4)
    total = 12 + 8 + len(jb) + 8 + len(binc)
    with open(path, 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, total))
        f.write(struct.pack('<II', len(jb), 0x4E4F534A) + jb)
        f.write(struct.pack('<II', len(binc), 0x004E4942) + binc)


def convert(src, dst):
    js, binc = read_glb(src)
    views = js.get('bufferViews', [])
    # rebuild the binary chunk: every buffer view copied as is, images re-encoded to PNG
    new_bin = bytearray()
    new_views = []
    png_of = {}
    for i, img in enumerate(js.get('images', [])):
        if img.get('mimeType') == 'image/webp':
            v = views[img['bufferView']]
            data = binc[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
            im = Image.open(io.BytesIO(data))
            out = io.BytesIO()
            im.save(out, 'PNG', optimize=True)
            png_of[i] = out.getvalue()
    image_views = {img['bufferView']: i for i, img in enumerate(js.get('images', [])) if i in png_of}
    for vi, v in enumerate(views):
        if vi in image_views:
            data = png_of[image_views[vi]]
        else:
            data = binc[v.get('byteOffset', 0):v.get('byteOffset', 0) + v['byteLength']]
        new_bin += b'\0' * (-len(new_bin) % 4)
        nv = dict(v, byteOffset=len(new_bin), byteLength=len(data))
        new_bin += data
        new_views.append(nv)
    js['bufferViews'] = new_views
    js['buffers'] = [{'byteLength': len(new_bin)}]
    for i in png_of:
        js['images'][i]['mimeType'] = 'image/png'
    for t in js.get('textures', []):
        ext = t.get('extensions', {}).pop('EXT_texture_webp', None)
        if ext is not None:
            t['source'] = ext['source']
        if 'extensions' in t and not t['extensions']:
            del t['extensions']
    for k in ('extensionsUsed', 'extensionsRequired'):
        if k in js:
            js[k] = [e for e in js[k] if e != 'EXT_texture_webp']
            if not js[k]:
                del js[k]
    write_glb(dst, js, bytes(new_bin))
    return len(png_of)


if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    os.makedirs(dst, exist_ok=True)
    files = [os.path.join(src, f) for f in sorted(os.listdir(src)) if f.endswith('.glb')] if os.path.isdir(src) else [src]
    for f in files:
        n = convert(f, os.path.join(dst, os.path.basename(f)))
        print(f'{os.path.basename(f)}: {n} textures -> PNG, {os.path.getsize(os.path.join(dst, os.path.basename(f))) // 1024} KB')
