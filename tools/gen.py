"""Preflight the sheets and generate src/Generated.cs from them.

usage: python tools/gen.py [--magicraft-dump DIR] [--noita DIR] [--decomp DIR]
The optional folders add lab checks against this PC's installs (never copied into the project).
"""
import argparse, json, os, re, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
S = lambda n: json.load(open(os.path.join(ROOT, "sheets", n), encoding="utf-8"))

ap = argparse.ArgumentParser()
ap.add_argument("--magicraft-dump"); ap.add_argument("--noita"); ap.add_argument("--decomp")
a = ap.parse_args()

spells, mods, scaling, chars, hooks, srcs, tests = S("spells.json"), S("modifiers.json"), S("scaling.json"), S("characters.json"), S("hooks.json"), S("noita_sources.json"), S("tests.json")
problems = []

def cells(sheet, rows, cols):
    for i, r in enumerate(rows):
        for c in cols:
            v = r.get(c, None)
            if v is None or v == "" or (isinstance(v, list) and not v):
                problems.append(f"{sheet} row {i} ({r.get(list(cols)[0])}): '{c}' is empty")
        for k in r:
            if k not in cols:
                problems.append(f"{sheet} row {i}: column '{k}' is not declared")

cells("spells", spells["rows"], spells["columns"])
cells("modifiers", mods["rows"], mods["columns"])
cells("scaling", scaling["rows"], scaling["columns"])
cells("sets", chars["sets"]["rows"], chars["sets"]["columns"])
cells("wands", chars["wands"]["rows"], chars["wands"]["columns"])
cells("hooks", hooks["rows"], hooks["columns"])
cells("noita_sources", srcs["rows"], srcs["columns"])
cells("tests", tests["rows"], tests["columns"])

RARITY = {"Common": 1, "Rare": 2, "Epic": 3}
SPECIAL = {"none", "teleport", "blackhole", "beam", "thrown"}
seen = set()
for sheet, rows, digit in (("spells", spells["rows"], 1), ("modifiers", mods["rows"], 3)):
    for r in rows:
        if r["newBase"] // 1000 != digit: problems.append(f"{sheet} {r['noitaId']}: newBase must start with {digit}")
        if r["rarity"] not in RARITY: problems.append(f"{sheet} {r['noitaId']}: rarity '{r['rarity']}' unknown")
        for k in ("noitaId", "newBase"):
            if (k, r[k]) in seen: problems.append(f"{sheet}: duplicate {k} {r[k]}")
            seen.add((k, r[k]))
for r in spells["rows"]:
    if r["special"] not in SPECIAL: problems.append(f"spells {r['noitaId']}: special '{r['special']}' unknown")
    for c in ("shootSound", "hitSound", "bounceSound"):
        v = r[c]
        if v != "none" and v.split(":")[0] not in ("projectiles", "explosion"): problems.append(f"spells {r['noitaId']}: {c} must start with projectiles: or explosion:")
spell_ids = {r["newBase"] * 10 + 1 for r in spells["rows"]}
wand_ids = {w["wandId"] for w in chars["wands"]["rows"]}
for w in chars["wands"]["rows"]:
    for s in w["slots"]:
        if s and s not in spell_ids: problems.append(f"wands {w['wandId']}: slot spell {s} is not a spells.json level-1 id")
for st in chars["sets"]["rows"]:
    if st["setId"] != 13: problems.append("sets: setId must be 13")
    for wid in st["wandIds"]:
        if wid not in wand_ids: problems.append(f"sets {st['setId']}: wand {wid} not in wands sheet")
    if len(st["upgradeCosts"]) != len(st["wandIds"]) - 1: problems.append(f"sets {st['setId']}: upgradeCosts must be wandIds-1 long")

# lab checks against the installs on this PC
if a.magicraft_dump:
    mc = {s["id"]: s for s in json.load(open(os.path.join(a.magicraft_dump, "SpellConfig.txt"), encoding="utf-8-sig"))}
    mw = {w["id"] for w in json.load(open(os.path.join(a.magicraft_dump, "WandConfig.txt"), encoding="utf-8-sig"))}
    abil = {s["abilityType"] for s in mc.values()}
    for r in spells["rows"] + mods["rows"]:
        if any((r["newBase"] * 10 + l) in mc for l in range(1, 10)): problems.append(f"{r['noitaId']}: newBase {r['newBase']} collides with a Magicraft spell id")
        if r["newBase"] in abil: problems.append(f"{r['noitaId']}: ability type {r['newBase']} is used by Magicraft")
    for w in chars["wands"]["rows"]:
        if w["cloneOf"] not in mw: problems.append(f"wands {w['wandId']}: cloneOf {w['cloneOf']} not a Magicraft wand")
        if w["wandId"] in mw: problems.append(f"wands {w['wandId']}: id collides with Magicraft")
if a.noita:
    sys.path.insert(0, os.path.join(ROOT, "tools"))
    from wak import read_wak
    blob, toc = read_wak(os.path.join(a.noita, "data", "data.wak"))
    o, n = toc["data/scripts/gun/gun_actions.lua"]
    lua = blob[o:o + n].decode("utf-8")
    for r in spells["rows"] + mods["rows"]:
        if not re.search(r'id\s*=\s*"%s"' % r["noitaId"], lua): problems.append(f"{r['noitaId']}: not in Noita gun_actions.lua")
    snd = os.path.join(a.noita, "data", "audio", "Desktop")
    for bank in ("projectiles", "explosion"):
        if not os.path.exists(os.path.join(snd, bank + ".bank")): problems.append(f"Noita sound bank {bank}.bank missing")
    for w in chars["wands"]["rows"]:
        if w["icon"] not in toc: problems.append(f"wands {w['wandId']}: {w['icon']} not in data.wak")
    for st in chars["sets"]["rows"]:
        if st["spriteXml"] not in toc: problems.append(f"sets: {st['spriteXml']} not in data.wak")
    keys = {line.split(",", 1)[0] for line in open(os.path.join(a.noita, "data", "translations", "common.csv"), encoding="utf-8")}
    for w in chars["wands"]["rows"]:
        if w["nameKey"] not in keys: problems.append(f"wands {w['wandId']}: nameKey {w['nameKey']} not in common.csv")
if a.decomp:
    for h in hooks["rows"]:
        if h["kind"] == "api": continue
        if h["kind"] == "transpiler":
            cls, code = h["target"].split("|", 1)
            p = os.path.join(a.decomp, cls + ".cs")
            if not os.path.exists(p) or code not in open(p, encoding="utf-8").read():
                problems.append(f"hooks {h['id']}: '{code}' not found in {cls}")
            continue
        t, m = h["target"].rsplit(".", 1)
        cls = t.split("+")[0]
        p = os.path.join(a.decomp, cls + ".cs")
        if not os.path.exists(p) or not re.search(r"\b%s\s*\(" % re.escape(m), open(p, encoding="utf-8").read()):
            problems.append(f"hooks {h['id']}: {h['target']} not found in decompiled code")

all_ids = {r["newBase"] * 10 + 1 for r in spells["rows"] + mods["rows"]}
for r in tests["testWands"]["rows"]:
    for sid in r["slots"]:
        if sid and sid not in all_ids: problems.append(f"tests.testWands run {r['run']}: {sid} is not a Noita spell or modifier")
cells("combos", tests["combos"]["rows"], tests["combos"]["columns"])
magicraft_ids = set(mc.keys()) if a.magicraft_dump else None
for r in tests["combos"]["rows"]:
    for sid in r["slots"]:
        if not sid or sid in all_ids: continue
        if magicraft_ids is not None and sid not in magicraft_ids: problems.append(f"tests.combos {r['id']}: {sid} is neither a Noita nor a Magicraft spell")
    if len(r["slots"]) > 4: problems.append(f"tests.combos {r['id']}: Mina's wand has 4 slots")
handlers = {h["handler"] for h in hooks["rows"]}
if problems:
    print("PREFLIGHT FAILED"); [print(" -", p) for p in problems]; sys.exit(1)
print(f"preflight clean: {len(spells['rows'])} spells, {len(mods['rows'])} modifiers, {len(chars['wands']['rows'])} wands, {len(chars['sets']['rows'])} set, {len(hooks['rows'])} hooks, {len(tests['rows'])} checks")

cs = lambda s: json.dumps(s, ensure_ascii=False)
f = lambda x: repr(float(x)).replace("inf", "float.PositiveInfinity") + "f"
out = ["// Generated by tools/gen.py from sheets/*.json. Do not edit; change the sheet.", "namespace NoitaCraft", "{",
       "    internal static class Sheets", "    {"]
out.append("        public static readonly SpellRow[] Spells =\n        {")
for r in spells["rows"]:
    out.append(f"            new SpellRow({cs(r['noitaId'])}, {r['newBase']}, {RARITY[r['rarity']]}, {r['copies']}, {cs(r['special'])}, {f(r['throwSpeed'])}, {cs(r['shootSound'])}, {cs(r['hitSound'])}, {cs(r['bounceSound'])}),")
out.append("        };")
out.append("        public static readonly ModifierRow[] Modifiers =\n        {")
bl = lambda v: str(v).lower()
for r in mods["rows"]:
    out.append(f"            new ModifierRow({cs(r['noitaId'])}, {r['newBase']}, {RARITY[r['rarity']]}, {r['addBounces']}, {bl(r['homing'])}, {r['addPierce']}, {f(r['damageAdd'])}, {f(r['damageMul'])}, {f(r['speedMul'])}, {f(r['spreadAdd'])}, {f(r['freezeSec'])}, {bl(r['electric'])}, {r['copies']}, {r['castDelay']}),")
out.append("        };")
out.append("        public static readonly System.Collections.Generic.Dictionary<string, float> Scaling = new System.Collections.Generic.Dictionary<string, float>\n        {")
for r in scaling["rows"]:
    out.append(f"            {{ {cs(r['key'])}, {f(r['value'])} }},")
out.append("        };")
out.append("        public static readonly WandRow[] Wands =\n        {")
for w in chars["wands"]["rows"]:
    out.append(f"            new WandRow({w['wandId']}, {w['cloneOf']}, {cs(w['nameKey'])}, {cs(w['icon'])}, new int[] {{ {', '.join(map(str, w['slots']))} }}),")
out.append("        };")
st = chars["sets"]["rows"][0]
out.append(f"        public static readonly SetRow Mina = new SetRow({st['setId']}, {cs(st['name'])}, {cs(st['description'])}, new int[] {{ {', '.join(map(str, st['wandIds']))} }}, new int[] {{ {', '.join(map(str, st['upgradeCosts']))} }}, {st['relicId']}, {cs(st['spriteXml'])}, {cs(st['animations'][0])}, {cs(st['animations'][1])}, {f(st['worldHeight'])}, {st['uiHeight']});")
out.append("        public static readonly int[][] TestWands = { " + ", ".join("new int[] { " + ", ".join(map(str, r["slots"])) + " }" for r in tests["testWands"]["rows"]) + " };")
out.append("        public static readonly (string Id, int[] Slots, float Seconds)[] Combos =\n        {")
for r in tests["combos"]["rows"]:
    out.append(f"            ({cs(r['id'])}, new int[] {{ {', '.join(map(str, r['slots']))} }}, {f(r['seconds'])}),")
out.append("        };")
out.append("        public static readonly string[] Checks = { " + ", ".join(cs(t["check"]) for t in tests["rows"]) + " };")
out.append("    }")
out.append("}")
open(os.path.join(ROOT, "src", "Generated.cs"), "w", encoding="utf-8").write("\n".join(out) + "\n")
print("wrote src/Generated.cs")
