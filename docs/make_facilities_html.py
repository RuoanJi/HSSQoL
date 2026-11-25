"""Build docs/facilities.html (human-readable, sortable table) from docs/data/facilities.json.

Run:  python make_facilities_html.py   (from docs/)
"""
import json
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE / "data" / "facilities.json"
OUT = HERE / "facilities.html"

KNOW_ZH = ["近战", "远程", "海员", "探究心", "工作心", "勇敢战士", "秘传"]
PARAM_ZH = ["舒适", "锻炼", "解析", "收获"]
# ShipInChipModel.EX_EFF_TYPE_* 0-12
EX_ZH = PARAM_ZH + [k + "知识" for k in KNOW_ZH] + ["购物", "资源"]
# ShipInChipModel.TYPE_*
TYPE_EN = {
    0: "floor", 1: "stairway", 2: "facility", 3: "plant", 4: "bedroom", 5: "restaurant",
    6: "training room", 7: "lab", 8: "farm", 9: "tree farm", 10: "ranch",
    11: "monster house", 12: "warehouse", 13: "huge warehouse", 14: "prison",
}
COST_TYPE = {0: "资源", 1: "金币"}


def row(m, cats):
    gk = m.get("getKnow") or []
    return {
        "id": m["id"],
        "name": m["name"],
        "explain": m.get("explain") or "",
        "kind": TYPE_EN.get(m["type"], str(m["type"])),
        "cat": cats[m["category"]] if 0 <= m["category"] < len(cats) else str(m["category"]),
        "w": m["widthNum"], "h": m["heightNum"],
        "know": KNOW_ZH[gk[0]] if len(gk) >= 2 else "",
        "knowBase": gk[1] if len(gk) >= 2 else None,
        "knowTime": m.get("getKnowTime") or None,
        "param": PARAM_ZH[m["paramType"]],
        "paramValue": m["paramValue"],
        "area": [[PARAM_ZH[t], v] for t, v in (m.get("areaEffect") or [])],
        "ex": [EX_ZH[m["exEffect"][0]], m["exEffect"][1]] if len(m.get("exEffect") or []) >= 2 else None,
        "work": m["workPow"],
        "refund": m["workPow"] > 0 and m["healWorkPowFlag"] == 0,
        "useCost": m["useCost"] or None,
        "useCostType": COST_TYPE.get(m["costType"], "") if m["useCost"] else "",
        "buildCost": m["buildCost"], "buildTime": m["buildTime"],
        "harvest": [m["harvestTime"], m["harvestNum"][0], m["harvestNum"][1]] if m.get("isHarvest") else None,
        "canLevel": bool(m.get("canLevel")),
    }


def main():
    data = json.loads(SRC.read_text(encoding="utf-8"))
    rows = [row(m, data["categoryNames"]) for m in data["models"]]
    payload = json.dumps(rows, ensure_ascii=False, separators=(",", ":")).replace("</", "<\\/")
    OUT.write_text(TEMPLATE.replace("__DATA__", payload), encoding="utf-8")
    print(f"wrote {OUT} ({len(rows)} facilities)")


TEMPLATE = r"""<!doctype html>
<html lang="zh">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Facility Table</title>
<style>
:root {
  --bg: #f7f6f3; --surface: #ffffff; --text: #1d1d1b; --muted: #6b6a66; --line: #e4e2dc;
  --head: #efede8; --accent: #2f6f8f; --accent-bg: #e3eff5; --good: #2e7d4f; --good-bg: #e2f2e8;
  --chip: #f0eee9; --row-hover: #faf9f6;
}
@media (prefers-color-scheme: dark) {
  :root:not([data-theme="light"]) {
    --bg: #161615; --surface: #1f1f1d; --text: #ecebe7; --muted: #9c9a94; --line: #34332f;
    --head: #282825; --accent: #7fb6d3; --accent-bg: #1f3440; --good: #7cc79a; --good-bg: #1d3326;
    --chip: #2b2a27; --row-hover: #252523;
  }
}
:root[data-theme="dark"] {
  --bg: #161615; --surface: #1f1f1d; --text: #ecebe7; --muted: #9c9a94; --line: #34332f;
  --head: #282825; --accent: #7fb6d3; --accent-bg: #1f3440; --good: #7cc79a; --good-bg: #1d3326;
  --chip: #2b2a27; --row-hover: #252523;
}
* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); color: var(--text);
  font: 14px/1.45 system-ui, -apple-system, "Segoe UI", "Microsoft YaHei", "PingFang SC", sans-serif; }
main { max-width: 1600px; margin: 0 auto; padding: 24px 16px 48px; }
h1 { font-size: 22px; margin: 0 0 4px; }
.sub { color: var(--muted); margin: 0 0 16px; max-width: 900px; }
.controls { display: flex; flex-wrap: wrap; gap: 8px 16px; align-items: center; margin-bottom: 12px; }
input[type=search] { font: inherit; padding: 6px 10px; border: 1px solid var(--line); border-radius: 6px;
  background: var(--surface); color: var(--text); min-width: 220px; }
.seg { display: inline-flex; border: 1px solid var(--line); border-radius: 6px; overflow: hidden; }
.seg button { font: inherit; border: 0; background: var(--surface); color: var(--text); padding: 6px 10px; cursor: pointer; }
.seg button + button { border-left: 1px solid var(--line); }
.seg button.on { background: var(--accent-bg); color: var(--accent); font-weight: 600; }
label.tog { display: inline-flex; gap: 6px; align-items: center; color: var(--muted); cursor: pointer; }
.count { color: var(--muted); margin-left: auto; }
.wrap { overflow-x: auto; background: var(--surface); border: 1px solid var(--line); border-radius: 8px; }
table { border-collapse: collapse; width: 100%; }
th, td { padding: 6px 10px; border-bottom: 1px solid var(--line); text-align: left; vertical-align: top; white-space: nowrap; }
th { background: var(--head); position: sticky; top: 0; font-weight: 600; font-size: 12px; color: var(--muted);
  cursor: pointer; user-select: none; }
th.sorted { color: var(--text); }
th .arrow { font-size: 10px; margin-left: 3px; }
tbody tr:hover { background: var(--row-hover); }
td.num { text-align: right; font-variant-numeric: tabular-nums; }
th.num { text-align: right; }
td.name { white-space: normal; min-width: 140px; }
td.name .ex { display: block; color: var(--muted); font-size: 12px; max-width: 260px; }
.chip { display: inline-block; background: var(--chip); border-radius: 4px; padding: 0 6px; margin: 1px 2px 1px 0; font-size: 12px; }
.refund { display: inline-block; background: var(--good-bg); color: var(--good); border-radius: 4px; padding: 0 6px; font-size: 12px; margin-left: 4px; }
.dim { color: var(--muted); }
.notes { color: var(--muted); font-size: 13px; margin-top: 16px; max-width: 900px; }
.notes li { margin: 3px 0; }
</style>
</head>
<body>
<main>
<h1>设施数据 · Facility table</h1>
<p class="sub">All 80 facility types from <code>data/facilities.json</code>. Click a column header to sort.</p>
<div class="controls">
  <input type="search" id="q" placeholder="Search name / description…">
  <div class="seg" id="cat"></div>
  <label class="tog"><input type="checkbox" id="hideRooms" checked> Hide floors, stairways & bedrooms</label>
  <span class="count" id="count"></span>
</div>
<div class="wrap"><table>
  <thead><tr id="head"></tr></thead>
  <tbody id="body"></tbody>
</table></div>
<ul class="notes">
  <li><b>Knowledge</b>: type and base gain per visit (before facility level and crew bonuses). "Know time" is the game's raw <code>getKnowTime</code> value.</li>
  <li><b>Param</b>: base value of the facility's own param. Actual value = base + lv/2 + matching area effects from neighbours (max 500).</li>
  <li><b>Area effect</b>: added to neighbours (Manhattan distance ≤ 3) whose param type matches, + lv/5 of this facility.</li>
  <li><b>Extra effect</b>: flat bonus to a neighbour's slot (no level scaling).</li>
  <li><b>Work cost</b>: work power spent per visit. <span class="refund">退还</span> means the visit refunds the facility's current 舒适 value ÷ 10 afterwards.</li>
  <li><b>Harvest</b>: time per harvest and yield range.</li>
</ul>
</main>
<script>
const DATA = __DATA__;
const ROOM_KINDS = new Set(["floor", "stairway", "bedroom"]);
const fmt = n => n == null ? "" : n.toLocaleString("en-US");
const esc = s => String(s).replace(/[&<>"]/g, c => ({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;"}[c]));
const COLS = [
  {k:"id", t:"ID", num:true},
  {k:"name", t:"名称 Name", cls:"name", html: r => esc(r.name) + (r.explain ? `<span class="ex">${esc(r.explain)}</span>` : "")},
  {k:"kind", t:"Kind"},
  {k:"cat", t:"Category"},
  {k:"size", t:"Size", sort: r => r.w * r.h, html: r => `${r.w}×${r.h}`},
  {k:"know", t:"Knowledge", sort: r => r.know ? r.know : "~", html: r => r.know ? `${r.know} <b>${r.knowBase}</b>` : `<span class="dim">—</span>`},
  {k:"knowTime", t:"Know time", num:true},
  {k:"param", t:"Param", sort: r => r.param + String(r.paramValue).padStart(4, "0"), html: r => `${r.param} <b>${r.paramValue}</b>`},
  {k:"area", t:"Area effect", sort: r => r.area.length ? r.area[0][1] : -1,
     html: r => r.area.map(([p, v]) => `<span class="chip">${p} +${v}</span>`).join("") || `<span class="dim">—</span>`},
  {k:"ex", t:"Extra effect", sort: r => r.ex ? r.ex[0] : "~",
     html: r => r.ex ? `<span class="chip">${r.ex[0]} +${r.ex[1]}</span>` : `<span class="dim">—</span>`},
  {k:"work", t:"Work cost", num:true, html: r => (r.work ? fmt(r.work) : `<span class="dim">0</span>`) + (r.refund ? `<span class="refund">退还</span>` : "")},
  {k:"useCost", t:"Use cost", num:true, html: r => r.useCost ? `${fmt(r.useCost)} <span class="dim">${r.useCostType}</span>` : ""},
  {k:"harvest", t:"Harvest", sort: r => r.harvest ? r.harvest[0] : -1,
     html: r => r.harvest ? `${fmt(r.harvest[0])} <span class="dim">→</span> ${r.harvest[1]}–${r.harvest[2]}` : ""},
  {k:"buildCost", t:"Build cost", num:true},
  {k:"buildTime", t:"Build time", num:true},
  {k:"canLevel", t:"Levels", html: r => r.canLevel ? "yes" : `<span class="dim">no</span>`},
];
let sortKey = "id", sortDir = 1, cat = "All";
const cats = ["All", ...new Set(DATA.map(r => r.cat))];
const catEl = document.getElementById("cat");
cats.forEach(c => {
  const b = document.createElement("button"); b.textContent = c; b.onclick = () => { cat = c; render(); };
  catEl.appendChild(b);
});
const head = document.getElementById("head");
COLS.forEach(c => {
  const th = document.createElement("th"); th.dataset.k = c.k; if (c.num) th.className = "num";
  th.innerHTML = `${c.t}<span class="arrow"></span>`;
  th.onclick = () => { if (sortKey === c.k) sortDir = -sortDir; else { sortKey = c.k; sortDir = c.num ? -1 : 1; } render(); };
  head.appendChild(th);
});
["q", "hideRooms"].forEach(id => document.getElementById(id).addEventListener("input", render));
function val(c, r) { const v = c.sort ? c.sort(r) : r[c.k]; return v == null || v === "" ? null : v; }
function render() {
  const q = document.getElementById("q").value.trim().toLowerCase();
  const hide = document.getElementById("hideRooms").checked;
  const col = COLS.find(c => c.k === sortKey);
  const rows = DATA.filter(r =>
    (cat === "All" || r.cat === cat) && (!hide || !ROOM_KINDS.has(r.kind)) &&
    (!q || (r.name + " " + r.explain + " " + r.kind + " " + r.know + " " + r.param).toLowerCase().includes(q))
  ).sort((a, b) => {
    const x = val(col, a), y = val(col, b);
    if (x === y) return a.id - b.id;
    if (x === null) return 1; if (y === null) return -1;
    return (typeof x === "number" && typeof y === "number" ? x - y : String(x).localeCompare(String(y), "zh")) * sortDir;
  });
  document.getElementById("body").innerHTML = rows.map(r => "<tr>" + COLS.map(c =>
    `<td class="${c.num ? "num " : ""}${c.cls || ""}">${c.html ? c.html(r) : esc(fmt(r[c.k]))}</td>`).join("") + "</tr>").join("");
  head.querySelectorAll("th").forEach(th => {
    const on = th.dataset.k === sortKey; th.classList.toggle("sorted", on);
    th.querySelector(".arrow").textContent = on ? (sortDir > 0 ? "▲" : "▼") : "";
  });
  catEl.querySelectorAll("button").forEach(b => b.classList.toggle("on", b.textContent === cat));
  document.getElementById("count").textContent = `${rows.length} / ${DATA.length}`;
}
render();
</script>
</body>
</html>
"""

if __name__ == "__main__":
    main()
