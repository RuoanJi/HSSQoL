"""Builds analyze.md and loot.md from BepInEx/hssqol-data.json (written by the HSSQoL DataDump, F10 / auto at boot).

Game rules used (from ISIL of the game code, see the notes at the top of each output file):
  * ItemElement.GetChoice(lv, num): table = items_[clamp(lv)]; for each of num picks, scan rows 0,1,2,... in a
    loop, row k accepted when Random(0..100) < weight_k - 30*timesPickedThisAnalysis (min 1).
  * FighterBase.GetDropItemID: scan dropItem_ rows [chance, elementId]; accepted when Random(0..100) <= chance
    (+30 when a 渔师 lands the killing blow: ex-skill #31, current job only). First hit drops; else 50% a small deck item (DropItem table by enemy tier).
  * Drop level: rank = CourseData.GetRank(enemyLv) = 10*t+u; lv = (4t+u)//2 + rand(-1..1), clamped 0..13.
  * CourseData.GetCasketInfo: pick a uniform random row, accept if Random(0..100) <= w  => P ~ (w+1).
  * Player.GetChoiceEnemyGroup: Player.GetEnemyGroup(listID) = every group with upperID_ == listID, whatever its
    upperType_ (sea areas pass LandArea.enemyListID_, dungeons CourseData.battleGroupID_; both share one ID space).
    A group with a campaign_ tag is only included at stamina rank (GetStaminaRank) >= 40.
    Uniform pick, accept if Random(0..100) <= upperRatio_  => P ~ (upperRatio_+1).
  * SearchBattle._init (dungeon fights): picked group + more groups drawn from the same pool (GetEnemyGroup, same
    weighting) up to max(lv//50+1, 2*rankTier, hard?4) groups, max 25; rank tier 0 in normal mode = picked group only.
    Enemy lv = course lv * Player.GetEnemyLvRatio (1, hard 2, nightmare 5). FieldBase.AddEnemyGroup spawns every row
    of a group at once; a member [kind, id, lo, hi] spawns GetRand(lo, hi) copies.
  * Bosses: one fixed group (LandArea.bossGroup_ / a fixEvent script [4, groupId]); every member of it is fought.
    SearchCourse.FirstStart adds a fixed event only if exeNum_ == 0 or oneLimit_ == 1, so oneLimit_ 0 = first clear only.
"""
import json, os, sys
from functools import lru_cache

HERE = os.path.dirname(os.path.abspath(__file__))
# Game folder: HSS_GAME_DIR, else the one this repo sits in (<game>/workspace/src/HSSQoL/docs).
GAME = os.environ.get('HSS_GAME_DIR') or os.path.join(HERE, '..', '..', '..', '..')
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(GAME, 'BepInEx', 'hssqol-data.json')
d = json.load(open(SRC, encoding='utf-8'))
fac = json.load(open(os.path.join(HERE, 'data', 'facilities.json'), encoding='utf-8'))
FAC = {m['id']: m['name'] for m in fac['models']}

ITEM = {i['id_']: i['name_'] for i in d['itemList_']}
EQUIP = {i['id_']: i['name_'] for i in d['equipList_']}
ELEM = d['itemElementList_']
ENEMY = {e['id_']: e for e in d['enemyModelList_']}
CHAR = {c['id_']: c for c in d['charList_']}
JOB = {j['id_']: j for j in d['jobList_']}
GROUP = {g['id_']: g for g in d['enemyGroupList_']}
X = d['extra']
RANK_BY_LV = X['rankByLv']
RANK_LETTER = 'FEDCBAS'
POINT = {0: '资源', 1: '奖牌'}


def rank_str(r):
    t, u = divmod(r, 10)
    return RANK_LETTER[min(t, 6)] + ('+' * u)


def get_rank(lv):
    """CourseData.GetRank(lv): first row t / column u with lv <= RANK_LIST[t][u] -> 10t+u; past the end -> 63."""
    rl = X['RANK_LIST']
    for t, row in enumerate(rl):
        for u, thr in enumerate(row):
            if lv <= thr: return 10 * t + u
    return 10 * len(rl) + len(rl[-1]) - 11


assert all(get_rank(lv) == r for lv, r in enumerate(RANK_BY_LV)), 'get_rank mismatch'


def drop_lv_base(enemy_lv):
    r = get_rank(enemy_lv)
    t, u = divmod(r, 10)
    return (4 * t + u) // 2


def drop_lv_range(enemy_lv):
    b = drop_lv_base(enemy_lv)
    return max(b - 1, 0), min(b + 1, 13)


def rng(a, b):
    return f'{a}' if a == b else f'{a}–{b}'


def thing(t, i, lo=None, hi=None, casket=False):
    """Name of a loot row. Element types: 0 point,1 item,2 equip,3 money. Casket: 1 equip,2 item,3 facility,4 money."""
    if casket:
        t = {1: 'equip', 2: 'item', 3: 'fac', 4: 'money', 0: 'point'}[t]
    else:
        t = {0: 'point', 1: 'item', 2: 'equip', 3: 'money'}[t]
    n = '' if lo is None or (lo == 1 and hi == 1) else f' ×{rng(lo, hi)}'
    if t == 'item': return f'{ITEM.get(i, f"item#{i}")}{n}'
    if t == 'equip': return f'{EQUIP.get(i, f"equip#{i}")} (装备){n}'
    if t == 'fac': return f'{FAC.get(i, f"facility#{i}")} (设施)'
    if t == 'point': return f'{POINT.get(i, f"point#{i}")}{n}'
    return f'金钱 {rng(i, lo)}'


def element_rows(rows):
    out = []
    for r in rows:
        if r[1] == 3: out.append((r[0], thing(3, r[2], r[3])))
        else: out.append((r[0], thing(r[1], r[2], r[3], r[4])))
    return out


def analyze_probs(weights, picks):
    """Returns (P(row on a single first pick), E[count of row over `picks` picks]) exactly, with the -30 penalty."""
    n = len(weights)

    def single(pen):
        p = [max(w - 30 * k, 1) / 101 for w, k in zip(weights, pen)]
        # sequential scan, cycles until something is accepted (1000-step cap ignored: negligible)
        q, fail = [], 1.0
        for pk in p:
            q.append(fail * pk); fail *= 1 - pk
        tot = 1 - fail
        return [x / tot for x in q]

    @lru_cache(None)
    def exp(pen, left):
        if left == 0: return (0.0,) * n
        res = [0.0] * n
        for k, pk in enumerate(single(pen)):
            if pk == 0: continue
            np_ = list(pen); np_[k] += 1
            sub = exp(tuple(np_), left - 1)
            for j in range(n): res[j] += pk * (sub[j] + (1 if j == k else 0))
        return tuple(res)

    return single((0,) * n), exp((0,) * n, picks)


def pct(x):
    return f'{x * 100:.1f}%' if x >= 0.001 else '<0.1%'


# ---------------------------------------------------------------- analyze.md
A = []
A.append('# 解析 outcomes (analyze items)\n')
A.append('Generated by `make_loot.py` from the game\'s own data tables (`AppData.itemElementList_`). '
         'Item/equipment names are the Chinese UI names from the game.\n')
A.append('''## How analysis works

- There are **11 analyze item types**, each with **14 levels (Lv.0–Lv.13)**. The level picks which outcome table is used; higher levels have better tables.
- A crew member at a 解析 facility works on the item until progress ≥ **(Lv+1) × 难度** (the per-type 难度 value below).
  Progress per work tick = **5 + 賢さ/20** (monsters: 5) **+ the facility's 解析 value / 10**.
- When finished it gives **N picks** from the level's table: crew **3–4** picks (+2 if the crew member's current job is 药剂师 (ex-skill #20), +1 while the weekly bonus 解析UP is active); monsters 1–2.
- Each pick scans the rows **top to bottom, looping**: a row is taken when a roll 0–100 is **below its weight**. So early rows are favoured, and the "chance" columns below are the real resulting odds (not weight / total).
  After a row is picked, its weight drops by 30 (min 1) for the rest of that analysis, so one analysis rarely gives the same row twice.
- "1 pick" = chance of that row for a single pick; "avg in 4 picks" = average number received from a typical 4-pick analysis.
''')
for e in ELEM:
    A.append(f'\n## {e["id_"]}. {e["name_"]} (难度 {e["breakPoint_"]})\n')
    for lv, rows in enumerate(e['items_']):
        w = [r[0] for r in rows]
        p1, e4 = analyze_probs(w, 4)
        A.append(f'\n**Lv.{lv}** (work needed {(lv + 1) * e["breakPoint_"]})\n')
        A.append('| outcome | weight | 1 pick | avg in 4 picks |\n|---|---:|---:|---:|')
        for (wt, name), a, b in zip(element_rows(rows), p1, e4):
            A.append(f'| {name} | {wt} | {pct(a)} | {b:.2f} |')
open(os.path.join(HERE, 'analyze.md'), 'w', encoding='utf-8').write('\n'.join(A) + '\n')

# ---------------------------------------------------------------- loot.md
def members(gid):
    """Distinct (kind, id) in a group (all rows)."""
    g = GROUP.get(gid)
    if not g: return []
    return [(m[0], m[1]) for row in g['data_'] for m in row]


def member_counts(gid):
    """{(kind, id): expected copies} for one group. FieldBase.AddEnemyGroup spawns every row at once; an entry
    [kind, id, lo, hi] spawns Lib.GetRand(lo, hi) copies (lo..hi), a plain [kind, id] one."""
    out = {}
    for row in GROUP[gid]['data_']:
        for m in row:
            c = (m[2] + m[3]) / 2 if len(m) >= 4 else 1
            out[(m[0], m[1])] = out.get((m[0], m[1]), 0) + c
    return out


def enemy_drops(kind, mid):
    """[(chance, elementId)] and display name."""
    if kind == 0:
        e = ENEMY.get(mid)
        return (e['name_'] if e else f'monster#{mid}'), (e['dropItem_'] if e else [])
    c = CHAR.get(mid)
    if not c: return f'char#{mid}', []
    j = JOB.get(c['jobID_'])
    jn = j["name_"] if j else "?"
    return (f'{c["name_"]} ({jn})' if c["name_"] else f'{jn} (人类敌人)'), (j['dropItem_'] if j else [])


def drop_chances(rows, bonus=0):
    """Sequential first-hit: P(row k) = p_k * prod(1-p_j, j<k), p = (chance+1)/101."""
    out, fail = [], 1.0
    for ch, el in rows:
        p = min((ch + 1 + bonus) / 101, 1)
        out.append((el, fail * p)); fail *= 1 - p
    merged = {}
    for el, p in out: merged[el] = merged.get(el, 0) + p
    return list(merged.items()), fail


def group_pool(list_id):
    """Player.GetEnemyGroup: every group with upperID_ == list_id (any upperType_). Returns [(gid, weight, campaign)]."""
    return [(g['id_'], min(g['upperRatio_'] + 1, 101), g['campaign_'] >= 0)
            for g in d['enemyGroupList_'] if g['upperID_'] == list_id]


def shares(pool):
    """{(kind, id): (chance a drawn group contains it, expected copies per drawn group)}."""
    seen = {}
    tot = sum(w for _, w, _ in pool) or 1
    for gid, w, _ in pool:
        for km, c in member_counts(gid).items():
            p, e = seen.get(km, (0.0, 0.0))
            seen[km] = (p + w / tot, e + c * w / tot)
    return seen


def enemy_rows(list_id):
    """[(kind, mid, p, e, rank40_only)] per group drawn. Shares are for stamina rank < 40; enemies that only come
    from campaign-tagged groups (rank >= 40) are added with their share in the rank >= 40 pool."""
    pool = group_pool(list_id)
    base = shares([p for p in pool if not p[2]])
    full = shares(pool)
    out = [(k, m, p, e, False) for (k, m), (p, e) in base.items()]
    out += [(k, m, p, e, True) for (k, m), (p, e) in full.items() if (k, m) not in base]
    return sorted(out, key=lambda r: (r[4], -r[3]))


# Game modes: Player.GetEnemyLvRatio = 1, ×2 with FLAG_HARD_MODE (2), ×5 with FLAG_NIGHTMARE_MODE (13).
MODES = [('普通', 1, False), ('困难', 2, True), ('噩梦', 5, False)]


def groups_per_fight(lv, hard):
    """SearchBattle._init: the picked group, then more groups drawn from the same pool until there are
    max(lv // 50 + 1, 2 × rank tier, 4 if hard mode) groups (max 25). Below rank E (tier 0) in normal mode the
    fight is the picked group alone (also needs MyConfig.ENEMY_LV_LIMIT_BREAK > lv, assumed)."""
    t = get_rank(lv) // 10
    if not hard and 2 * t <= 1: return 1
    n = lv // 50 + 1
    if hard: n = max(n, 4)
    return min(max(n, 2 * t), 25)


def mode_info(base_lv, dungeon):
    out = []
    for name, mul, hard in MODES:
        lv = base_lv * mul
        out.append({'m': name, 'lv': lv, 'dl': list(drop_lv_range(lv)), 'n': groups_per_fight(lv, hard) if dungeon else None})
    return out


def mode_line(base_lv, dungeon):
    parts = []
    for mi in mode_info(base_lv, dungeon):
        s = f'{mi["m"]} enemy Lv {mi["lv"]} → analyze items Lv.{mi["dl"][0]}–{mi["dl"][1]}'
        if dungeon: s += f', {mi["n"]} group{"s" if mi["n"] > 1 else ""} per fight'
        parts.append(s)
    return 'By game mode: ' + ' · '.join(parts) + '.'


def rank40(flag):
    return ' (体力等级 ≥ 40)' if flag else ''


def enemy_table(list_id, base_lv, dungeon):
    """Markdown lines listing each random-fight enemy's drops."""
    n = mode_info(base_lv, dungeon)[0]['n']
    L = [mode_line(base_lv, dungeon) + '\n']
    if dungeon:
        L += [f'| enemy | in a drawn group | avg per fight (普通, {n} group{"s" if n > 1 else ""}) | drops (chance per kill) | if a 渔师 lands the kill |', '|---|---:|---:|---|---|']
    else:
        L += ['| enemy | in a drawn group | drops (chance per kill) | if a 渔师 lands the kill |', '|---|---:|---|---|']
    for kind, mid, p, e, r40 in enemy_rows(list_id):
        name, rows = enemy_drops(kind, mid)
        ch, _ = drop_chances(rows)
        dl = ', '.join(f'{ELEM[el]["name_"]} {pct(x)}' for el, x in ch) or '—'
        chf, _ = drop_chances(rows, 30)
        dlf = ', '.join(f'{ELEM[el]["name_"]} {pct(x)}' for el, x in chf) or '—'
        mid_col = f' {e * n:.2f} |' if dungeon else ''
        L.append(f'| {name}{rank40(r40)} | {pct(p)} |{mid_col} {dl} | {dlf} |')
    return L


def row_names(gid):
    out = []
    for row in GROUP[gid]['data_']:
        if not row: continue
        out.append([enemy_drops(m[0], m[1])[0] + (f' ×{m[2]}–{m[3]}' if len(m) >= 4 else '') for m in row])
    return out


def boss_lines(bosses, dungeon):
    """Markdown for the boss fight(s): each group is one fixed fight against all its members, all rows at once."""
    L = []
    for gid, once in bosses:
        rows = row_names(gid)
        when = ''
        if dungeon: when = ' (**first clear only**; later runs have no boss)' if once else ' (every run)'
        L.append(f'\nBoss fight{when}: ' + ' / '.join(', '.join(r) for r in rows) + '. One fixed group, every enemy in it at once.')
        for k, m in sorted(set(members(gid))):
            name, drops = enemy_drops(k, m)
            ch, _ = drop_chances(drops)
            L.append(f'- {name}: ' + (', '.join(f'{ELEM[el]["name_"]} {pct(p)}' for el, p in ch) or 'no analyze drop'))
    return L


def boss_groups(c):
    """[(groupId, first_clear_only)] from the dungeon's fixed SCR_SPECIFY_BATTLE events."""
    out = []
    for pos, evid in (c['fixEvent_'] or []):
        ev = SE.get(evid, {})
        for s in ev.get('script_', []):
            if s and s[0] == 4 and len(s) > 1 and s[1] in GROUP: out.append((s[1], ev.get('oneLimit_') == 0))
    return out


B = []
B.append('# Loot by sea area and dungeon\n')
B.append('Generated by `make_loot.py` from the game\'s data tables. See `analyze.md` for what each analyze item turns into.\n')
B.append('''## Rules

- **Enemy drops.** Each defeated enemy checks its drop list top to bottom; the first row that hits drops that analyze item (if the killing blow comes from a crew member whose current job is 渔师, every row's chance gets +30 points, e.g. 7% → 37%). Monsters use their own list; human enemies use their job's list.
  If nothing drops, there is a 50% chance of a small deck pickup instead (money / 资源 / items, table by enemy tier, see the end).
- **Enemy level and game mode.** Enemies fight at the place's level × the game mode: 普通 ×1, 困难 (hard mode) ×2, 噩梦 (nightmare mode) ×5. Each place lists all three.
- **Drop level.** The analyze item's level comes from the enemy's level: rank (F, F+, … S+++) → base level, then ±1 at random, clamped to 0–13.
- **Enemy groups.** Every sea area and dungeon has an enemy-list ID. Drawing a group means picking one of the groups with that ID, weighted by the group's ratio. Sea areas and dungeons share the same ID numbers, so a dungeon also draws from the sea groups with its ID and vice versa. A group's members all spawn together (its two rows are front and back, not waves); some members come as 1–3 copies ("×1–3"). Groups tied to a campaign only join once your stamina rank (体力等级) is 40 or higher; their enemies are marked "(体力等级 ≥ 40)".
- **"in a drawn group"** is the chance that one drawn group contains that enemy.
- **Dungeon fights are several groups.** A random dungeon fight draws one group, then keeps drawing more from the same pool until it has max(enemy Lv ÷ 50 + 1, 2 × rank tier) groups (rank tier: F = 0, E = 1, D = 2, …; at least 4 in hard mode; at most 25). At rank F in normal mode a fight is a single group. "avg per fight" is the expected number of that enemy in one normal-mode fight; for other modes, scale it by their group count. (Nightmare is counted without the hard-mode minimum of 4; that only matters below dungeon Lv 30.)
- **Sea fights** also draw several groups, but how many was not decoded, so sea areas only show the per-group numbers.
- **Bosses.** The boss is not a roll: it is one fixed group, and you fight every enemy in it at once. In dungeons, many bosses are one-time events: they only appear until you have beaten them once, and later runs of a 可重复 dungeon end without a boss (the event's `oneLimit_` = 0). Each dungeon says which.
- **Dungeon layout.** The number of events is fixed per dungeon. The fixed events (usually a chest at 99% and the boss at 100%) come first; when a one-time boss is gone, its slot is filled like the others. The rest alternate: one random slot, then one guaranteed fight, and so on. Only the random slots vary; "nothing" slots show nothing on screen.
- **Repeatable dungeons.** 可重复 dungeons can be run again after clearing; 一次性 (one-time) ones can't be entered again once cleared (the game's `infinity_` flag; all BOSS迷宫 are one-time).
- **Dungeon chests (宝箱).** Each chest picks one row of the dungeon's chest table (chance ∝ weight).
''')

# --- Sea areas
B.append('\n# Sea areas (海域)\n')
area_land = {}
for l in d['landList_']:
    for a in l['area_']: area_land[a] = l['name_']
for a in sorted(d['landAreaList_'], key=lambda a: (min([i for i, l in enumerate(d['landList_']) if a['id_'] in l['area_']] or [99]), a['enemyLv_'])):
    aid = a['id_']
    land = area_land.get(aid, '?')
    B.append(f'\n## {land} · Lv.{a["enemyLv_"]} 海域 (area #{aid})\n')
    B += enemy_table(a['enemyListID_'], a['enemyLv_'], False)
    if a['bossGroup_'] in GROUP:
        B += boss_lines([(a['bossGroup_'], False)], False)

# --- Dungeons
B.append('\n# Dungeons (迷宫)\n')
SE = {e['id_']: e for e in d['searchEventList_']}
CITY = {c['id_']: c for c in d['cityList_']}
LAND = {l['id_']: l['name_'] for l in d['landList_']}
WA = {w['id_']: w for w in d['wharfAreaList_']}


def stamina(c):
    """CourseData.GetCost -> Player.GetSubQuestStamina: value - (冒险家 in party, max 2), halved by weekly bonus, min 1."""
    v = c['costValue_']
    if v <= 0: return [0, 0, 0]
    return [v, max(v - 1, 1), max(v - 2, 1)]


EV_KIND = {0: '战斗', 1: '宝箱', 3: '剧情', 4: 'BOSS战', 5: '居民对话', 6: '怪物蛋', 7: '回复(半)', 8: '回复(全)'}


def ev_kind(evid):
    sc = SE.get(evid, {}).get('script_') or []
    return EV_KIND.get(sc[0][0], f'事件{evid}') if sc and sc[0] else '无事件'


def layout(c, repeat=False):
    """SearchCourse.FirstStart: N = GetRand(eventNum_) event slots. The fixEvent_ entries ([pos%, eventId]) come first.
    Then, until N is reached: GetRand(eventInterval_) random slots (each: scan randomEvent_ rows in order, row taken when
    Random(0..99) <= weight, looping), followed by one filler slot that is always searchEvent 0 (a fight).
    All dungeons have eventInterval_ = [1, 1], so random and filler slots alternate.
    A fixed event with oneLimit_ == 0 is only added while its exeNum_ is 0 (never run): repeat=True leaves those out."""
    n = c['eventNum_'][0]  # min == max in every dungeon
    fixed = [[pos, ev_kind(e)] for pos, e in (c['fixEvent_'] or []) if not (repeat and SE.get(e, {}).get('oneLimit_') == 0)]
    k = max(c['eventInterval_'][0], 1)
    left, rand, filler = n - len(fixed), 0, 0
    while left > 0:
        take = min(k, left); rand += take; left -= take
        if left > 0: filler += 1; left -= 1
    odds, fail = {}, 1.0
    for w, e in (c['randomEvent_'] or []):
        p = min((w + 1) / 101, 1); odds[ev_kind(e)] = odds.get(ev_kind(e), 0) + fail * p; fail *= 1 - p
    tot = 1 - fail
    odds = {kd: v / tot for kd, v in odds.items()} if tot > 0 else {}
    exp = {}
    for _, kd in fixed: exp[kd] = exp.get(kd, 0) + 1
    exp['战斗'] = exp.get('战斗', 0) + filler
    for kd, p in odds.items(): exp[kd] = exp.get(kd, 0) + rand * p
    return {'n': n, 'fixed': fixed, 'filler': filler, 'rand': rand,
            'odds': [[kd, round(p, 4)] for kd, p in odds.items()], 'exp': {kd: round(v, 2) for kd, v in exp.items()}}


def layout_text(L):
    g = {}
    for _, kd in L['fixed']: g[kd] = g.get(kd, 0) + 1
    if L['filler']: g['战斗'] = g.get('战斗', 0) + L['filler']
    order = ['战斗', '宝箱', 'BOSS战']
    fixed = ' · '.join(f'{kd}×{g[kd]}' for kd in sorted(g, key=lambda x: (order.index(x) if x in order else 9, x)))
    rnd = ' / '.join(f'{kd} {pct(p)}' for kd, p in L['odds'])
    return f'{L["n"]} events: guaranteed {fixed}; {L["rand"]} random (each: {rnd})'


def port_of(c):
    w = WA.get(c['areaIndex_'])
    city = CITY.get(w['cityIndex_']) if w else None
    if not city: return '?', '?'
    ln = LAND.get(city.get('landIndex_')) or next((l['name_'] for l in d['landList_'] if city['id_'] in l['citys_']), '')
    return city['name_'], ln
ranks = X['courseRank']
for i, c in enumerate(d['courseDataList_']):
    rk = ranks[i]
    pn, ln = port_of(c)
    B.append(f'\n## #{c["id_"]} {c["name_"]} (Lv {c["lv_"]}, rank {rank_str(rk)}) · 港口 {pn}{('（' + ln + '）') if ln else ''} · {'可重复' if c['infinity_'] == 0 else '一次性'}\n')
    sv = stamina(c)
    B.append(f'Stamina (体力) to enter: **{sv[0]}** (with 1 冒险家 in the party {sv[1]}, with 2+ {sv[2]}; halved during the 探险体力减半 weekly bonus, min 1)'
             + (f'. Requires fleet rank (船团等级) ≥ {c["costRank_"]}.' if c['costRank_'] > 0 else '.') + '\n')
    cas = c['getCasket_'] or []
    tot = sum(r[0] + 1 for r in cas) or 1
    if cas:
        B.append('Chest contents (one row per chest):\n')
        B.append('| reward | chance |\n|---|---:|')
        for r in cas:
            if r[1] == 4: nm = thing(4, r[2], r[3], casket=True)
            elif len(r) >= 5: nm = thing(r[1], r[2], r[3], r[4], casket=True)
            else: nm = thing(r[1], r[2], casket=True)
            B.append(f'| {nm} | {pct((r[0] + 1) / tot)} |')
    L = layout(c)
    B.append(f'\nLayout: {layout_text(L)}. Average chests per run: {L["exp"].get("宝箱", 0):.2f}.')
    if c['infinity_'] == 0:
        R = layout(c, True)
        if R['fixed'] != L['fixed']:
            B.append(f'Repeat runs (one-time events already done): {layout_text(R)}. Average chests per run: {R["exp"].get("宝箱", 0):.2f}.')
    if group_pool(c['battleGroupID_']):
        B.append('')
        B += enemy_table(c['battleGroupID_'], c['lv_'], True)
    B += boss_lines(boss_groups(c), True)

# --- deck pickups
B.append('\n# Small deck pickups (when an enemy drops no analyze item: 50%)\n')
B.append('Table chosen by the enemy\'s tier (0 = normal … 4 = leader). Numbers are raw row weights (the exact roll for this table was not decoded; read them as relative odds).\n')
for t in d['dropItemList_']:
    rows = t['script_']; tot = sum(r[0] + 1 for r in rows)
    parts = []
    for r in rows:
        if r[1] == 0: nm = f'金钱 {rng(r[2], r[3])}'
        elif r[1] == 1: nm = f'资源 {rng(r[2], r[3])}'
        else: nm = ITEM.get(r[2], f'item#{r[2]}')
        parts.append(f'{nm} (w{r[0]})')
    B.append(f'- Tier {t["id_"]}: ' + ', '.join(parts))

open(os.path.join(HERE, 'loot.md'), 'w', encoding='utf-8').write('\n'.join(B) + '\n')
print('ok', len(A), len(B))


# ---------------------------------------------------------------- loot.html (same data, structured)
def enemy_data(list_id):
    out = []
    for kind, mid, p, e, r40 in enemy_rows(list_id):
        name, rows = enemy_drops(kind, mid)
        ch, _ = drop_chances(rows)
        x = {'n': name, 'p': round(p, 4), 'e': round(e, 4), 'd': [[el, round(q, 5)] for el, q in ch], 'f': [[el, round(q, 5)] for el, q in drop_chances(rows, 30)[0]]}
        if r40: x['r40'] = 1
        out.append(x)
    return out


def boss_data(bosses):
    """{'w': [[[names per row]] per fight], 'once': [first clear only per fight], 'm': [distinct members with drops]}"""
    seen, out = set(), []
    for gid, _ in bosses:
        for k, m in sorted(set(members(gid))):
            if (k, m) in seen: continue
            seen.add((k, m))
            name, rows = enemy_drops(k, m)
            ch, _ = drop_chances(rows)
            out.append({'n': name, 'd': [[el, round(p, 5)] for el, p in ch], 'f': [[el, round(p, 5)] for el, p in drop_chances(rows, 30)[0]]})
    return {'w': [row_names(g) for g, _ in bosses], 'once': [o for _, o in bosses], 'm': out}


H = {'elements': [e['name_'] for e in ELEM], 'seas': [], 'dungeons': [], 'pickups': []}
for a in sorted(d['landAreaList_'], key=lambda a: (min([i for i, l in enumerate(d['landList_']) if a['id_'] in l['area_']] or [99]), a['enemyLv_'])):
    H['seas'].append({'id': a['id_'], 'land': area_land.get(a['id_'], '?'), 'lv': a['enemyLv_'],
                      'rank': rank_str(get_rank(a['enemyLv_'])), 'modes': mode_info(a['enemyLv_'], False),
                      'enemies': enemy_data(a['enemyListID_']), 'boss': boss_data([(a['bossGroup_'], False)] if a['bossGroup_'] in GROUP else [])})
kinds = {0: '战斗', 1: '宝箱', 2: '无事件'}
for i, c in enumerate(d['courseDataList_']):
    cas = c['getCasket_'] or []
    tot = sum(r[0] + 1 for r in cas) or 1
    chests = []
    for r in cas:
        if r[1] == 4: nm = thing(4, r[2], r[3], casket=True)
        elif len(r) >= 5: nm = thing(r[1], r[2], r[3], r[4], casket=True)
        else: nm = thing(r[1], r[2], casket=True)
        chests.append([nm, round((r[0] + 1) / tot, 4)])
    ev = c['randomEvent_'] or []
    et = sum(w for w, _ in ev) or 1
    boss = boss_data(boss_groups(c))
    pn, ln = port_of(c)
    H['dungeons'].append({'id': c['id_'], 'name': c['name_'], 'port': pn, 'land': ln, 'cost': stamina(c), 'needRank': c['costRank_'], 'repeat': c['infinity_'] == 0, 'lv': c['lv_'], 'rank': rank_str(ranks[i]),
                          'modes': mode_info(c['lv_'], True), 'chests': chests,
                          'layout': layout(c), 'layoutRepeat': layout(c, True) if c['infinity_'] == 0 and layout(c, True)['fixed'] != layout(c)['fixed'] else None,
                          'enemies': enemy_data(c['battleGroupID_']), 'boss': boss})
for t in d['dropItemList_']:
    rows = []
    for r in t['script_']:
        if r[1] == 0: nm = f'金钱 {rng(r[2], r[3])}'
        elif r[1] == 1: nm = f'资源 {rng(r[2], r[3])}'
        else: nm = ITEM.get(r[2], f'item#{r[2]}')
        rows.append([nm, r[0]])
    H['pickups'].append({'tier': t['id_'], 'rows': rows})

tpl = open(os.path.join(HERE, 'loot_template.html'), encoding='utf-8').read()
payload = json.dumps(H, ensure_ascii=False, separators=(',', ':')).replace('</', '<' + chr(92) + '/')
open(os.path.join(HERE, 'loot.html'), 'w', encoding='utf-8').write(tpl.replace('/*LOOT_DATA*/null', payload))
print('html ok', len(payload))


# ---------------------------------------------------------------- analyze.html
KIND = {0: 'point', 1: 'item', 2: 'equip', 3: 'money'}
AH = []
for e in ELEM:
    levels = []
    for rows in e['items_']:
        levels.append([[w, name, KIND[r[1]]] for (w, name), r in zip(element_rows(rows), rows)])
    AH.append({'id': e['id_'], 'name': e['name_'], 'bp': e['breakPoint_'], 'levels': levels})
atpl = open(os.path.join(HERE, 'analyze_template.html'), encoding='utf-8').read()
apay = json.dumps(AH, ensure_ascii=False, separators=(',', ':')).replace('</', '<' + chr(92) + '/')
open(os.path.join(HERE, 'analyze.html'), 'w', encoding='utf-8').write(atpl.replace('/*ANALYZE_DATA*/null', apay))
print('analyze html ok', len(apay))
