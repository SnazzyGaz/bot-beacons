#!/usr/bin/env python3
"""Build a personal beacons.json from the starter pack: one beacon per bot.

  python3 make_beacons.py -o beacons.json \
      --beacon "id=research; name=Research Bot; look=robot; color=#FFB13B; agent=<agent id>; purpose=Research and reports" \
      --beacon "id=news; name=News Bot; look=cat; agent=<agent id>"

Keys per --beacon (separated by ";"):
  id       short lowercase id (a-z, 0-9, -), used by beaconctl.py   (required)
  name     shown in the bubble and menus                             (required)
  look     a starter beacon to copy the art and animation from: orb, robot or cat (default orb)
  agent    the Grok Bot agent id this beacon carries messages for (used by "Open in app")
  color    #RRGGBB eye/glow colour (default: the look's colour)
  purpose  one line under the name in the bubble
  gist     one-line sample that "Test glow" shows
The first beacon receives messages for unknown ids (fallback) unless another has "fallback=true".
The hidden "echo" test beacon is kept so the reply router can be tested; pass --no-echo to drop it.
"""
import argparse, copy, json, os, re, sys

HERE = os.path.dirname(os.path.abspath(__file__))
STARTER = os.path.join(HERE, "..", "beacons.json")

def parse(spec):
    out = {}
    for part in spec.split(";"):
        if not part.strip(): continue
        if "=" not in part: sys.exit(f"Bad --beacon part '{part.strip()}': use key=value")
        k, v = part.split("=", 1); out[k.strip().lower()] = v.strip()
    return out

def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--beacon", action="append", required=True)
    ap.add_argument("--starter", default=STARTER)
    ap.add_argument("--no-echo", action="store_true")
    ap.add_argument("-o", "--out", default="-")
    a = ap.parse_args()
    st = json.load(open(a.starter))
    looks = {b["id"]: b for b in st["beacons"]}
    out = {k: v for k, v in st.items() if k != "beacons"}; out["beacons"] = []
    ids = set(); any_fallback = False
    for spec in a.beacon:
        o = parse(spec)
        bid, name = o.get("id", ""), o.get("name", "")
        if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,30}", bid): sys.exit(f"Bad id '{bid}': lowercase letters, digits and '-' only.")
        if bid in ids or bid == "echo": sys.exit(f"Duplicate or reserved id '{bid}'.")
        if not name: sys.exit(f"Beacon '{bid}' needs a name.")
        look = o.get("look", "orb")
        if look not in looks or looks[look].get("test"): sys.exit(f"Unknown look '{look}'. Starter looks: orb, robot, cat.")
        b = copy.deepcopy(looks[look])
        b.pop("fallback", None)
        b.update({"id": bid, "name": name, "agentId": o.get("agent", ""), "purpose": o.get("purpose", f"Messages from {name}"),
                  "gist": o.get("gist", f"{name}: something is ready for you.")})
        if o.get("color"):
            if not re.fullmatch(r"#[0-9A-Fa-f]{6}", o["color"]): sys.exit(f"Bad colour '{o['color']}': use #RRGGBB.")
            b["color"] = b["tint"] = o["color"].upper()
        if o.get("fallback", "").lower() == "true": b["fallback"] = True; any_fallback = True
        ids.add(bid); out["beacons"].append(b)
    if not any_fallback: out["beacons"][0]["fallback"] = True
    if not a.no_echo and "echo" in looks: out["beacons"].append(copy.deepcopy(looks["echo"]))
    text = json.dumps(out, indent=2) + "\n"
    if a.out == "-": sys.stdout.write(text)
    else:
        open(a.out, "w").write(text); print(f"Wrote {a.out} with {len(out['beacons'])} beacon(s): " + ", ".join(b["id"] for b in out["beacons"]))

if __name__ == "__main__": main()
