#!/usr/bin/env python3
"""1.2.7: writes the zip fixtures SkinHarness unpacks (good, nested, and every kind of bad zip).
Usage: python3 tools/make_skin_fixtures.py OUTDIR   (standard library only)"""
import json, os, struct, sys, zipfile, zlib

def png(w, h, rgba=(120, 130, 255, 255)):
    raw = b"".join(b"\0" + bytes(rgba) * w for _ in range(h))
    def chunk(t, d): return struct.pack(">I", len(d)) + t + d + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")

SKIN = {"name": "Fixture", "author": "harness", "description": "two looks", "credits": "made by a script",
        "anim": {"idleEvery": [18, 45], "twitchEvery": [14, 40]},
        "beacons": [{"id": "robot", "name": "Skin Robot", "gist": "skin gist", "art": "art/r.png", "color": "#102030"}],
        "looks": [{"look": "a", "name": "Look A", "art": "art/a.png", "color": "#AA0000"},
                  {"look": "b", "name": "Look B", "art": "art/b.png", "color": "#00BB00", "anim": {"idleEvery": [20, 50]}}]}
GOOD = {"skin.json": json.dumps(SKIN).encode(), "art/a.png": png(60, 70), "art/b.png": png(80, 90, (0, 200, 0, 255)),
        "art/r.png": png(64, 64, (16, 32, 48, 255)), "preview.png": png(32, 16)}

def write(path, files, method=zipfile.ZIP_DEFLATED):
    with zipfile.ZipFile(path, "w", method) as z:
        for n, d in files.items(): z.writestr(n, d)

def patch_cd(path, name, off, fmt, value):
    """change one field of a central directory entry (off = offset from the entry start)"""
    b = bytearray(open(path, "rb").read()); i = 0
    while True:
        i = b.find(b"PK\x01\x02", i)
        if i < 0: raise SystemExit("entry not found: " + name)
        nl = struct.unpack_from("<H", b, i + 28)[0]
        if b[i + 46:i + 46 + nl].decode() == name: struct.pack_into(fmt, b, i + off, value); break
        i += 4
    open(path, "wb").write(bytes(b))

def main(out):
    os.makedirs(out, exist_ok=True)
    j = lambda n: os.path.join(out, n)
    write(j("good.zip"), dict(GOOD, **{"readme.txt": b"skipped", "__MACOSX/._a.png": b"junk", ".DS_Store": b"junk", "tools/run.exe": b"MZ"}))
    write(j("nested.zip"), {"My Skin/" + k: v for k, v in GOOD.items()})
    write(j("stored.zip"), GOOD, zipfile.ZIP_STORED)
    for n, bad in [("trav", "../evil.png"), ("trav2", "art/../../evil.png"), ("abs", "/evil.png"), ("drive", "C:/evil.png"),
                   ("ads", "art/a.png:evil.png"), ("back", "..\\evil.png"), ("dots", "art/x./e.png"), ("deep", "a/b/c/d/e.png")]:
        write(j(n + ".zip"), dict(GOOD, **{bad: png(4, 4)}))
    write(j("big.zip"), dict(GOOD, **{"art/big.png": b"\0" * (9 * 1024 * 1024)}))
    write(j("lie.zip"), dict(GOOD, **{"art/lie.png": b"\0" * (9 * 1024 * 1024)})); patch_cd(j("lie.zip"), "art/lie.png", 24, "<I", 100)
    write(j("total.zip"), dict(GOOD, **{"sounds/f%d.wav" % i: b"\0" * (7 * 1024 * 1024 + i) for i in range(10)}))
    write(j("crc.zip"), GOOD); patch_cd(j("crc.zip"), "art/a.png", 16, "<I", 12345)
    write(j("enc.zip"), GOOD); patch_cd(j("enc.zip"), "art/b.png", 8, "<H", 1)
    write(j("method.zip"), GOOD); patch_cd(j("method.zip"), "art/b.png", 10, "<H", 12)
    write(j("noskin.zip"), {k: v for k, v in GOOD.items() if k != "skin.json"})
    write(j("badskin.zip"), dict(GOOD, **{"skin.json": json.dumps(dict(SKIN, looks=[{"look": "a", "art": "art/missing.png"}])).encode()}))
    write(j("traversal-art.zip"), dict(GOOD, **{"skin.json": json.dumps(dict(SKIN, looks=[{"look": "a", "art": "../../beacon.png"}])).encode()}))
    write(j("huge-art.zip"), dict(GOOD, **{"art/a.png": png(1100, 20)}))
    open(j("notzip.zip"), "wb").write(b"hello, not a zip")
    import warnings
    with warnings.catch_warnings():
        warnings.simplefilter("ignore"); write(j("dup.zip"), dict(GOOD)); 
        with zipfile.ZipFile(j("dup.zip"), "a") as z: z.writestr("art/a.png", png(5, 5))
    print("fixtures in", out)

if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "/tmp/skinfx")
