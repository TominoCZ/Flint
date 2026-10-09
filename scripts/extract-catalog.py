#!/usr/bin/env python3
"""
Builds assets/catalog.json: every model the Spark app offers, per chain slot, with the images and panel layout it uses.
The app keeps this in code (the Fx* enums, their editor factories and the reverb types), not in data, so it is read from
jadx's decompile. Only facts come out (ids, names, file names), and only into the gitignored assets/.

Fails, naming what is wrong, if any model lacks a layout or any image or layout it names (directly, or through a
layout or XML drawable) is missing from assets/. That check is what keeps this parser honest when the app changes.

Usage: extract-catalog.py <research/decompiled> <assets dir>
"""
import json, re, sys
from pathlib import Path

src_root, assets = Path(sys.argv[1]) / "sources", Path(sys.argv[2])
model_dir = src_root / "com/positivegrid/spark/model"

# Constructor arguments after the enum's own (name, ordinal), per catalog class. Read from the constructors.
FIELDS = {
    "FxNoiseGate": ["dspId", "name", "icon", None],
    "FxComp": ["dspId", "name", "icon", "itemIcon", None],
    "FxDrive": ["dspId", "name", "icon", "itemIcon", None],
    "FxAmp": ["dspId", "name", "icon", "itemIcon", "face", "knobStyle", "cab", None],
    "FxPreamp": ["dspId", "name", "icon", "itemIcon", "face", "knobStyle", "cab", None],
    "FxModulation": ["dspId", "name", "icon", "itemIcon", None],
    "FxDelay": ["dspId", "name", "icon", "itemIcon", None],
    "FxReverb": ["dspId", None],
}
# The chain, left to right, as the strip labels it, with its catalog classes and its ModulePresets folder.
CHAIN = [("NOISEGATE", ["FxNoiseGate"], "NoiseGate"), ("COMP/WAH", ["FxComp"], "Comp"), ("DRIVE", ["FxDrive"], "Drive"),
         ("AMP", ["FxAmp", "FxPreamp"], "Amp"), ("MOD/EQ", ["FxModulation"], "Modulation"), ("DELAY", ["FxDelay"], "Delay"),
         ("REVERB", ["FxReverb"], "Reverb")]

errors = []


def resource_names():
    """Obfuscated R field -> real name, through the resource ids every R class shares. jadx names the renamed fields
    f<digits><letters> (f42079P); the real ones are snake_case. Ids are written in hex in some modules, decimal in others."""
    by_id, alias = {}, {}
    for r in src_root.rglob("R*.java"):
        if not re.fullmatch(r"R(\$\w+)?\.java", r.name):
            continue
        kind = r.name[2:-5] if r.name.startswith("R$") else None
        for line in r.read_text(errors="ignore").splitlines():
            if m := re.search(r"class (\w+) \{", line):
                kind = m.group(1).removeprefix("R$") if m.group(1) != "R" else kind
            elif kind and (m := re.search(r"public static (?:final )?int (\w+) = (0x[0-9a-fA-F]+|\d+);", line)):
                name, rid = m.group(1), int(m.group(2), 0)
                (alias.setdefault(kind, {}).__setitem__(name, rid) if re.fullmatch(r"f\d+\w*", name)
                 else by_id.setdefault((kind, rid), name))
    resolve = lambda kind, field: by_id.get((kind, alias.get(kind, {}).get(field)), field)
    return resolve, {rid: (kind, name) for (kind, rid), name in by_id.items()}


real, by_raw_id = resource_names()


def name_raw_ids():
    """jadx leaves some references in layouts and XML drawables as bare ids ("0x7f080262"). Name them once here, so the
    app reads only @kind/name references."""
    named = 0
    for xml in [*(assets / "res").glob("layout*/*.xml"), *(assets / "res").glob("drawable*/*.xml")]:
        text = xml.read_text(errors="ignore")
        fixed = re.sub(r'"(0x7f[0-9a-f]{6})"', lambda m: (f'"@{by_raw_id[int(m.group(1), 16)][0]}/{by_raw_id[int(m.group(1), 16)][1]}"'
                                                          if int(m.group(1), 16) in by_raw_id else m.group(0)), text)
        if fixed != text:
            xml.write_text(fixed)
            named += 1
    # Styles name some drawables as "?unknown_ref: 7f0804fe".
    styles = assets / "res" / "values" / "styles.xml"
    text = styles.read_text(errors="ignore")
    fixed = re.sub(r"\?unknown_ref: (7f[0-9a-f]{6})", lambda m: (f"@{by_raw_id[int(m.group(1), 16)][0]}/{by_raw_id[int(m.group(1), 16)][1]}"
                                                               if int(m.group(1), 16) in by_raw_id else m.group(0)), text)
    if fixed != text:
        styles.write_text(fixed)
        named += 1
    return named


def ref(token):
    """An argument or expression naming a resource: R.drawable.x, R$drawable.f123, R.layout.f456."""
    m = re.search(r"R[.$](drawable|layout)\.(\w+)", token)
    return real(m.group(1), m.group(2)) if m else None


def split_args(text):
    out, depth, cur = [], 0, ""
    for ch in text:
        if ch == "," and depth == 0:
            out.append(cur.strip()); cur = ""
            continue
        depth += ch in "(" ; depth -= ch in ")"
        cur += ch
    return out + [cur.strip()] if cur.strip() else out


def models(cls):
    """The enum's entries, in order, with their constructor arguments resolved."""
    text = (model_dir / f"{cls}.java").read_text()
    inline = f"new {cls}(" not in text  # FxNoiseGate declares its one entry inline, without name and ordinal
    block = text if inline else re.search(r"\n    static \{(.*?)\n    \}", text, re.S).group(1)
    # Locals are reassigned as the block goes (int i10 = R.drawable.x; Knob.styleRes styleres = ...), so read in order.
    local, entries = {}, []
    pattern = (r"^    (\w+)\((\".*?)\)[,;]$" if inline
               else rf"(?:(?:int|Integer|Knob\.styleRes|a) (\w+) = ([^;]+);)|(?:(\w+) = new {cls}\((.*?)\);)")
    for m in re.finditer(pattern, block, re.M):
        if not inline and m.group(1):
            local[m.group(1)] = m.group(2)
            continue
        if not inline:
            m = re.match(rf"(\w+) = new {cls}\((.*)\);", m.group(0))
        args = split_args(m.group(2))
        args = args if inline else args[2:]
        entry = {"id": m.group(1)}
        for field, arg in zip(FIELDS[cls], args):
            if field is None:
                continue
            arg = local.get(arg, arg)
            if arg.startswith('"'):
                entry[field] = arg.strip('"')
            elif "styleRes." in arg:
                entry[field] = arg.split("styleRes.")[1]
            elif arg != "null":
                entry[field] = ref(arg) or errors.append(f"{cls}.{m.group(1)}: cannot resolve {field} = {arg}")
        entries.append(entry)
    if not entries:
        errors.append(f"{cls}: no entries found")
    return entries


def editor_layouts(cls, entries):
    """Model id -> its editor layout, read from the factory its editor() calls (switch, if-chain, ternary or single)."""
    text = (model_dir / f"{cls}.java").read_text()
    body = re.search(r"Fragment editor\(\) \{(.*?)\n    \}", text, re.S).group(1)
    # Some models skip the factory for a fragment of their own (FxComp: the two wahs), chosen by a switch-map case.
    own_cases = {int(n): name for name, n in re.findall(rf"iArr\[{cls}\.(\w+)\.ordinal\(\)\] = (\d+);", text)}
    special = {}
    for n, frag in (re.findall(r"!= (\d+) \? [^:]+ : new (\w+)\(\)", body)
                    + re.findall(r"if \(\w+ != (\d+)\) \{.*?\}\s*return new (\w+)\(\)", body, re.S)):
        frag_text = next(src_root.rglob(f"{frag}.java")).read_text()
        # They inflate through generated view bindings (one for phones, one for tablets), which hold the layout ids.
        bindings = set(re.findall(r"(\w+)\.c\(inflater, container, false\)", frag_text))
        texts = [frag_text] + [next(src_root.rglob(f"{b}.java")).read_text() for b in bindings]
        special[own_cases[int(n)]] = [ref(t) for x in texts for t in re.findall(r"R\.layout\.\w+", x)]
    factory = re.search(r"(\w+)\.INSTANCE\.", body).group(1)
    imp = re.search(rf"import ([\w.]+\.{factory});", text).group(1)
    code = (src_root / (imp.replace(".", "/") + ".java")).read_text()

    case_to_id = {int(n): name for name, n in re.findall(rf"iArr\[{cls}\.(\w+)\.ordinal\(\)\] = (\d+);", code)}
    by_case, pending = {}, set()
    for line in code.splitlines():
        line = line.strip()
        if m := re.match(r"case (\d+):", line):
            pending.add(int(m.group(1)))
        elif line == "default:" or line.startswith("} else {"):
            pending.add("default")
        elif m := re.search(r"if \(\w+ [=!]= (\d+)\)", line):
            pending = {int(m.group(1))}
        elif m := re.search(r"== (\d+) \? R\.layout\.(\w+) : R\.layout\.(\w+)", line):
            by_case[int(m.group(1))] = real("layout", m.group(2))
            by_case["default"] = real("layout", m.group(3))
        elif (m := re.search(r"\w+ = R\.layout\.(\w+);", line)) and pending:
            for case in pending:
                by_case[case] = real("layout", m.group(1))
            pending = set()
        elif m := re.search(r"\(getLayoutInflater\(\), R\.layout\.(\w+),", line):
            by_case.setdefault("default", real("layout", m.group(1)))
    out = {}
    for e in entries:
        case = next((c for c, name in case_to_id.items() if name == e["id"]), "default")
        layouts = special.get(e["id"]) or [by_case.get(case) or by_case.get("default")]
        if not layouts[0]:
            errors.append(f"{cls}.{e['id']}: no editor layout found in {factory}")
            continue
        out[e["id"]] = sorted(set(layouts))
    return out


def reverb_types():
    """Param 6 of bias.reverb picks the reverb: value -> name, face, strip and picker icons. Each is an
    if-chain over the value (`if (m.b(param, 0.4f)) { return ... }`, the last one sometimes a ternary)."""
    text = (model_dir / "FxReverb.java").read_text()
    types = {}
    hit = r'm\.b\(\w+, ([0-9.]+)f\)\)?\s*(?:\{\s*return|\?)\s*("[^"]*"|R\.drawable\.\w+|Integer\.valueOf\(R\.drawable\.\w+\))'
    for value, token in re.findall(hit, text):
        name = ref(token)
        field = ("name" if token.startswith('"') else "face" if name.startswith("editor_") else
                 "itemIcon" if name.startswith("module_item_icon") else "icon")
        types.setdefault(float(value), {"value": float(value)})[field] = token.strip('"') if field == "name" else name
    for t in types.values():
        if len(t) != 5:
            errors.append(f"reverb type {t['value']}: only {sorted(t)}")
    return [types[v] for v in sorted(types)]


def knob_styles():
    text = next(src_root.rglob("controls/Knob.java")).read_text()
    return {m.group(1): ref(m.group(2)) for m in re.finditer(r"(JamUpKnob_\d+)\(\d+, (R\$drawable\.\w+)\)", text)}


def picker_order(folder, slot, reverbs):
    """The model sheet's order, from ModulePresets/<folder>/order.json. Each entry's folder also holds the knob values
    the app loads when that model is picked (data.json). A reverb entry is a type of bias.reverb, told by its param 6."""
    presets = assets / "ModulePresets" / folder
    order = []
    for m in json.loads((presets / "order.json").read_text())["modules"]:
        data = json.loads((presets / m["path"] / "data.json").read_text())
        entry = {"preset": f"ModulePresets/{folder}/{m['path']}/data.json", "dspId": data["dspId"]}
        if data["dspId"] == "bias.reverb":
            value = next(p["value"] for p in data["params"] if p["index"] == 6)
            entry["reverbType"] = round(value, 1)
            if not any(t["value"] == entry["reverbType"] for t in reverbs):
                errors.append(f"{folder}/{m['path']}: no reverb type {value}")
        elif not any(x["dspId"] == data["dspId"] for x in slot["models"]):
            errors.append(f"{folder}/{m['path']}: no model with dspId {data['dspId']}")
        order.append(entry)
    return order


def panel_controls(layout):
    """
    Which parameter each knob, slider and switch of a panel turns, read from its generated data binding, as the app
    wires it: DataBinderMapperImpl names the binding class for "layout/<name>_0"; its constructor hands the views to the
    base class in field order (objArr[N] is the view tagged binding_N, or the id sViewsWithIds gives N); and each
    two-way listener reads one field (D6.h.m a knob, D6.h.r a slider, isChecked a switch) and writes q.N(params, i, …).
    Returns {"id:<id>" or "tag:binding_N": {"param": i, "kind": ...}}.
    """
    mapper = (src_root / "com/positivegrid/spark/DataBinderMapperImpl.java").read_text()
    m = re.search(rf'"layout/{layout}_0"\.equals\(obj\)\) \{{\s*return new ([\w.]+)\(', mapper)
    if not m:
        return None
    cls = m.group(1).split(".")[-1]
    # One-letter class names repeat across packages: follow the mapper's own import.
    qualified = m.group(1) if "." in m.group(1) else re.search(rf"import ([\w.]+\.{cls});", mapper).group(1)
    path = src_root / (qualified.replace(".", "/") + ".java")
    impl = path.read_text()
    args = re.search(r"super\(\w+, view, \d+, (.*?)\);", impl, re.S).group(1)
    indices = [int(i) for i in re.findall(r"objArr\[(\d+)\]", args)]
    base_name = re.search(rf"class {cls} extends (\w+)", impl).group(1)
    base = (path.parent / f"{base_name}.java").read_text()
    ctor = re.search(rf"protected {base_name}\(Object \w+, View \w+, int \w+, (.*?)\) \{{(.*?)\n    \}}", base, re.S)
    params = [p.split()[-1] for p in ctor.group(1).split(",")]
    field_of = {param: field for field, param in re.findall(r"this\.(\w+) = (\w+);", ctor.group(2))}
    view_of_index = {int(n): f"id:{real('id', alias)}" for alias, n in re.findall(r"put\(R\.id\.(\w+), (\d+)\)", impl)}
    view_of_field = {}
    for position, param in enumerate(params):
        if param in field_of and position < len(indices):
            n = indices[position]
            view_of_field[field_of[param]] = view_of_index.get(n, f"tag:binding_{n}")
    controls = {}
    for block in re.split(r"\n    (?:final |private |public )*class ", impl)[1:]:
        fields = re.findall(rf"{cls}\.this\.(\w+)", block)
        write = re.search(r"q\.N\(\w+, (\d+),", block)
        if not fields or not write:
            continue
        kind = "knob" if "D6.h.m(" in block else "slider" if "D6.h.r(" in block else "switch" if "isChecked()" in block else "other"
        view = view_of_field.get(fields[0])
        if view:
            controls[view] = {"param": int(write.group(1)), "kind": kind}
    return controls


# ---- build ----
renamed_layouts = name_raw_ids()
reverbs, chain = reverb_types(), []
for label, classes, folder in CHAIN:
    slot = {"label": label, "models": []}
    for cls in classes:
        entries = models(cls)
        layouts = editor_layouts(cls, entries)
        for e in entries:
            e["editor"] = layouts.get(e["id"], [])
            slot["models"].append(e)
    slot["order"] = picker_order(folder, slot, reverbs)
    chain.append(slot)
catalog = {"chain": chain, "reverbTypes": reverbs, "knobStyles": knob_styles()}

# Each pedal panel's controls; every knob and slider in it must turn some parameter.
panels = {}
for name in sorted({e for slot in chain for m in slot["models"] for e in m["editor"] if not e.startswith("editor_amp")}):
    controls = panel_controls(name)
    if controls is None:
        # The two wahs use view bindings driven from their fragments' code, not data binding. The Auto
        # Wah's one knob is param 4 (AutoWahFragment's listener: hVar.n().put(4, value)). The J.H. Legendary Wah's
        # phone panel has no controls of its own: they are in a dialog its mode button opens, not drawn here.
        if name.startswith("editor_auto_wah"):
            controls = {"id:sensitivity_knob": {"param": 4, "kind": "knob"}}
        elif "wah" in name:
            continue
        else:
            errors.append(f"{name}: no data binding found")
            continue
    layout_text = (assets / "res" / "layout" / f"{name}.xml").read_text(errors="ignore")
    for tag, body in re.findall(r"<([\w.]+\.(?:Knob|Slider))\b(.*?)/?>", layout_text, re.S):
        vid = re.search(r'android:id="@\+id/(\w+)"', body)
        vtag = re.search(r'android:tag="(binding_\d+)"', body)
        keys = [k for k in ([f"id:{vid.group(1)}"] if vid else []) + ([f"tag:{vtag.group(1)}"] if vtag else [])]
        if not any(k in controls for k in keys):
            errors.append(f"{name}: {tag.split('.')[-1]} {keys} turns no parameter")
    panels[name] = controls
catalog["panels"] = panels

# ---- check every reference resolves to a file in assets/ ----
res = assets / "res"


def drawable_file(name):
    hits = [p for p in res.glob(f"drawable*/{name}.*")]
    return hits[0] if hits else None


seen, todo = set(), []
for slot in chain:
    for e in slot["models"]:
        todo += [("drawable", e[k]) for k in ("icon", "itemIcon", "face", "cab") if e.get(k)]
        todo += [("layout", n) for n in e["editor"]]
todo += [("drawable", t[k]) for t in catalog["reverbTypes"] for k in ("face", "icon", "itemIcon") if t.get(k)]
todo += [("drawable", d) for d in catalog["knobStyles"].values()]
while todo:
    kind, name = todo.pop()
    if (kind, name) in seen or not name:
        continue
    seen.add((kind, name))
    path = drawable_file(name) if kind == "drawable" else next(iter(res.glob(f"layout*/{name}.xml")), None)
    if path is None:
        errors.append(f"missing {kind} {name}")
    elif path.suffix == ".xml":
        todo += [("drawable", d) for d in re.findall(r"@drawable/(\w+)", path.read_text(errors="ignore"))]

if errors:
    sys.exit("catalog: " + "\n  ".join([f"{len(errors)} problem(s):"] + errors))
(assets / "catalog.json").write_text(json.dumps(catalog, indent=2))
count = sum(len(s["models"]) for s in chain)
print(f"catalog: {count} models in {len(chain)} chain slots, {len(catalog['reverbTypes'])} reverb types, "
      f"{len(catalog['knobStyles'])} knob styles, {len(seen)} files checked, bare ids named in {renamed_layouts} files")
