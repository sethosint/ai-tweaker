"""Writes catalog JSON in the house style: one key per line, inline dicts, one action per line."""
import json


def _inline(v):
    """One-line JSON with padded braces: { "ru": "...", "en": "..." } (recursively)."""
    if isinstance(v, dict):
        return "{ " + ", ".join(f"{json.dumps(k, ensure_ascii=False)}: {_inline(x)}" for k, x in v.items()) + " }" if v else "{}"
    if isinstance(v, list):
        return "[" + ", ".join(_inline(x) for x in v) + "]"
    return json.dumps(v, ensure_ascii=False)


def dumps_tweaks(tweaks):
    out = ["["]
    for i, t in enumerate(tweaks):
        out.append("  {")
        keys = list(t.keys())
        for j, k in enumerate(keys):
            last = j == len(keys) - 1
            if k == "actions":
                out.append('    "actions": [')
                for n, a in enumerate(t[k]):
                    out.append("      " + _inline(a) + ("," if n < len(t[k]) - 1 else ""))
                out.append("    ]" + ("" if last else ","))
            else:
                out.append(f'    "{k}": {_inline(t[k])}' + ("" if last else ","))
        out.append("  }" + ("," if i < len(tweaks) - 1 else ""))
    out.append("]")
    return "\n".join(out) + "\n"


def remove_object(text, obj_id):
    """Removes the top-level object with "id": obj_id from a hand-formatted JSON array, keeping the rest verbatim."""
    i = text.index(f'"id": "{obj_id}"')
    start = text.rfind("\n  {", 0, i)
    depth, j, in_str = 0, start + 1, False
    while True:
        c = text[j]
        if in_str:
            if c == "\\":
                j += 1
            elif c == '"':
                in_str = False
        elif c == '"':
            in_str = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                break
        j += 1
    end = j + 1
    if text[end] == ",":
        end += 1  # drop our trailing comma
    else:
        start = text.rfind(",", 0, start)  # last element: drop the previous comma instead
    out = text[:start] + text[end:]
    json.loads(out)
    return out


def _ml(key, d, indent):
    """{ "ru": ..., "en": ... } written over several lines, like guides.json summaries."""
    pad = " " * indent
    lines = [f'{pad}"{key}": {{']
    items = list(d.items())
    for n, (k, v) in enumerate(items):
        lines.append(f'{pad}  "{k}": {json.dumps(v, ensure_ascii=False)}' + ("," if n < len(items) - 1 else ""))
    lines.append(f"{pad}}}")
    return lines


def dumps_guide(g):
    out = ["  {"]
    keys = list(g.keys())
    for j, k in enumerate(keys):
        comma = "," if j < len(keys) - 1 else ""
        if k == "summary":
            block = _ml(k, g[k], 4)
            block[-1] += comma
            out += block
        elif k == "sections":
            out.append('    "sections": [')
            for n, s in enumerate(g[k]):
                out.append("      {")
                skeys = list(s.keys())
                for m, sk in enumerate(skeys):
                    sc = "," if m < len(skeys) - 1 else ""
                    if sk == "settings":
                        if not s[sk]:
                            out.append('        "settings": []' + sc)
                        else:
                            out.append('        "settings": [')
                            for q, st in enumerate(s[sk]):
                                out.append("          " + _inline(st) + ("," if q < len(s[sk]) - 1 else ""))
                            out.append("        ]" + sc)
                    else:
                        out.append(f'        "{sk}": {_inline(s[sk])}' + sc)
                out.append("      }" + ("," if n < len(g[k]) - 1 else ""))
            out.append("    ]" + comma)
        else:
            out.append(f'    "{k}": {_inline(g[k])}' + comma)
    out.append("  }")
    return "\n".join(out)


def append_objects(text, chunks):
    """Appends pre-formatted top-level objects to a hand-formatted JSON array."""
    body = text.rstrip()
    assert body.endswith("]")
    body = body[:-1].rstrip()
    out = body + ",\n" + ",\n".join(chunks) + "\n]\n"
    json.loads(out)
    return out
