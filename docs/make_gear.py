"""Builds equipment.md, items.md and gear.html from BepInEx/hssqol-data.json (HSSQoL DataDump, F10 / auto at boot).

Game rules used (read from the ISIL of data.EquipData / data.ItemData / SubForm.ProWharfSmith):

Equipment (EquipData)
  * Stat at level lv, GetParam(t, job, crew, lv): min, max = param_[t]; L = smithLvMax_.
    If lv > L: L = 2L, max = 2max (the line is extended past the base max level).
    v = min + trunc(float(max - min) / float(L - 1) * float(lv - 1))   (float32 math)
    Weapons (class 0) then get the job's weapon aptitude: v * r / 100 with r = 125 (good, or the matching
    契合度提升 ex-skill), 100 (normal), 20 (bad; a bad 杖 still keeps 80 for 智慧). Positive results are at least 1.
  * Max level, GetSmithLvMax = smithLvMax_ * GetLvMaxMul(): normal x1, hard x2, nightmare by type:
    剑 light 4 / normal 3 / heavy 8, 杖 8, 斧 6, 枪 3, 拳 8, 火枪 5, 弓 6,
    防具 (head/body) light 12 / normal 12 / heavy 4, 饰品 10. IsLvMax: lv >= that.
  * EXP: GetLvExp(n) = floor(n*n/2) * smithExpRate_ * n * 4 / 100 = cumulative EXP to reach Lv n
    (GetExpLv: highest n with exp_ >= GetLvExp(n)).
  * Smith EXP when `this` eats material e, GetSmithExp(e):
      c = (type differs) + (weight differs)  -> 0, 1, 2 ; mult = (3 - c) * 50  -> 150 / 100 / 50 %
      w = sum over stats of max(0, min_i + trunc((lv_e - 1) * (mul_this * max_i - min_i) / (mul_e * L_e - 1)))
      exp = max(5, (this.smithExpRate_ * w / 100) * mult / 100 * 3 / 4)
      x2 with either the subscription bonus or weekly bonus 9, x3 with both.
  * Value V(lv), _getBuyPrice: P = shopPrice_.
      normal: P + GetRatio(lv - 1, L) * P / 100
      hard / nightmare: P + GetRatio(lv - 1, L * mul) * P * 3 / 100        (GetRatio = clamp(a*100/b, 0, 100))
    Buy = max(5, V) (halved with weekly bonus 10). Sale = max(1, V / 30). Smith gold cost = sum over materials of
    max(5, V_material / 2).
  * Shields: damage cut and effect go linearly min -> max from Lv 1 to smithLvMax_ and stop there.

Items (ItemData)
  * GetAddExp: Lib.GetRand(addExp_[0], addExp_[1]); x1.5 with weekly bonus 8, x2 with the subscription bonus, x2.5 with both.
  * Stat cap per stat for power-ups: GetEffectLimitParam = (effectLimit_ + 1) * 10 lifetime food bonus.
  * Buy = max(5, shopPrice_) (halved with weekly bonus 10). Sale = max(1, shopPrice_ / 20).
"""
import json, os, struct, sys

HERE = os.path.dirname(os.path.abspath(__file__))
GAME = os.environ.get('HSS_GAME_DIR') or os.path.abspath(os.path.join(HERE, '..', '..', '..', '..'))
SRC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(GAME, 'BepInEx', 'hssqol-data.json')
d = json.load(open(SRC, encoding='utf-8'))

STAT = ['HP', 'SP', '攻击', '防御', '速度', '智慧']
CLASS = ['武器', '盾', '头部', '身体', '饰品']
TYPE = ['剑', '杖', '斧', '枪', '拳', '火枪', '弓', '防具']
WEIGHT = ['轻', '普通', '重']
SHIELD = ['眩晕', '远程伤害减轻', 'SP', '加速', '反射']
DIFF = ['普通', '困难', '噩梦']
ITYPE = ['道具', '强化', '药品', '秘籍书', '笛子', '改名']
JOB = {j['id_']: j['name_'] for j in d['jobList_']}

f32 = lambda x: struct.unpack('f', struct.pack('f', x))[0]
def trunc(x): return int(x)  # C cast: toward zero
def cdiv(a, b): return int(a / b) if (a < 0) != (b < 0) else a // b


def mul(e, diff):
    if diff == 0: return 1
    if diff == 1: return 2
    t, w = e['type_'], e['weight_']
    if t == 0: return {0: 4, 1: 3, 2: 8}.get(w, 3)
    if t == 7:
        if e['class_'] == 4: return 10
        return {0: 12, 1: 12}.get(w, 4)
    return {1: 8, 2: 6, 3: 3, 4: 8, 5: 5, 6: 6}.get(t, 3)


def stat(e, i, lv):
    mn, mx = e['param_'][i]
    L = e['smithLvMax_']
    if L < lv: L, mx = L * 2, mx * 2
    v = mn + trunc(f32(f32(f32(mx - mn) / f32(L - 1)) * f32(lv - 1)))
    return max(1, v) if v > 0 else v


def lv_exp(e, n): return (n * n // 2) * e['smithExpRate_'] * n * 4 // 100


def ratio(a, b): return 0 if a <= 0 or b <= 0 else max(0, min(100, a * 100 // b))


def value(e, lv, diff):
    P, L = e['shopPrice_'], e['smithLvMax_']
    lv1 = max(0, lv - 1)
    if diff == 0: return P + ratio(lv1, L) * P // 100
    return P + ratio(lv1, L * mul(e, diff)) * P * 3 // 100


def material(e, lv, diff, mul_this=None):
    """w term of GetSmithExp when e (at lv) is eaten; mul_this defaults to e's own multiplier."""
    m = mul(e, diff); mt = m if mul_this is None else mul_this
    w = 0
    for mn, mx in e['param_']:
        v = mn + trunc(f32(f32(lv - 1) * f32(f32(mt * mx - mn) / f32(m * e['smithLvMax_'] - 1))))
        if v > 0: w += v
    return w


def rng(a, b): return str(a) if a == b else f'{a}–{b}'
def fmt(n): return f'{n:,}'


# ---------------------------------------------------------------- equipment
EQ = []
for e in d['equipList_']:
    L = e['smithLvMax_']
    row = {'id': e['id_'], 'name': e['name_'], 'cls': e['class_'], 'type': e['type_'], 'weight': e['weight_'],
           'L': L, 'rate': e['smithExpRate_'], 'price': e['shopPrice_'], 'shopLv': e['shopLv_'], 'shopType': e['shopType_'],
           'crit': e['critical_'], 'desc': e['explain_'],
           'min': [p[0] for p in e['param_']], 'base': [p[1] for p in e['param_']], 'w1': material(e, 1, 0), 'diff': []}
    if e['class_'] == 1:
        row['shield'] = {'cut': [e['shieldCutMin_'], e['shieldCutMax_']], 'eff': SHIELD[e['shieldEffectType_']] if 0 <= e['shieldEffectType_'] < 5 else '?',
                         'effv': [e['shieldEffectMin_'], e['shieldEffectMax_']]}
    for df in range(3):
        m = mul(e, df); mx = L * m
        V = value(e, mx, df)
        row['diff'].append({'mul': m, 'maxLv': mx, 'max': [stat(e, i, mx) for i in range(6)], 'exp': lv_exp(e, mx),
                            'value': V, 'sale': max(1, V // 30), 'cost': max(5, V // 2), 'wmax': material(e, mx, df)})
    EQ.append(row)
EQ.sort(key=lambda r: (r['cls'], r['type'], r['id']))


def kind(r): return CLASS[r['cls']] + ('' if r['cls'] in (1, 2, 3, 4) else '·' + TYPE[r['type']])


def stat_cells(r, vals):
    return ' | '.join('' if (r['min'][i] == 0 and r['base'][i] == 0) else str(v) for i, v in enumerate(vals))


M = ['# 大海贼探险物语DX — 装备数据 / Equipment data', '',
     f'从游戏数据表导出（`equipList_`，{len(EQ)} 件）。由 `make_gear.py` 生成，可排序/筛选的网页版见 [gear.html](gear.html)。', '',
     '## 规则（读自游戏代码 `data.EquipData`）', '',
     '- **属性随等级**：`param_` 每项为 [Lv1 值, 基础满级值]，L = 基础满级 `smithLvMax_`。'
     'Lv ≤ L 时 值 = Lv1 + ⌊(满级 − Lv1) × (Lv − 1) / (L − 1)⌋。'
     'Lv > L（只有困难/噩梦能到）时改用 (2L, 2×满级) 这条线并继续外推：值 = Lv1 + ⌊(2×满级 − Lv1) × (Lv − 1) / (2L − 1)⌋。正值至少为 1。',
     '- **职业武器适性**（只影响武器）：擅长 ×125%（或有对应的“契合度提升”额外技能），普通 ×100%，不擅长 ×20%（不擅长的杖仍保留 80% 智慧）。下表数字不含适性。',
     '- **锻造满级**随难度变化：普通 ×1，困难 ×2，噩梦按种类：剑 轻 ×4 / 普通 ×3 / 重 ×8，杖 ×8，斧 ×6，枪 ×3，拳 ×8（盾也是拳类，×8），火枪 ×5，弓 ×6，'
     '头部/身体 轻 ×12 / 普通 ×12 / 重 ×4，饰品 ×10。',
     '- **锻造 EXP**：到达 Lv n 的累计 EXP = ⌊n²/2⌋ × 锻造EXP率 × n × 4 / 100（锻造EXP率 = `smithExpRate_`）。',
     '- **当素材时给的 EXP**：素材值 w = 素材各项属性在其当前等级的值之和（只算正值），'
     'EXP = max(5, 目标的锻造EXP率 × w / 100 × 相性 / 100 × 3/4)。相性：同种类且同重量 150%，有一项不同 100%，都不同 50%。'
     '月卡加成或每周加成 9 有其一 ×2，两个都有 ×3。下表“素材值 Lv1”是素材在 Lv1 时的 w。',
     '- **价格**：装备价值 V 随等级上升。普通：V = 价格 × (1 + 比例)，比例 = min(100%, (Lv−1)/L)，满级约 2 倍；'
     '困难/噩梦：V = 价格 × (1 + 3 × 比例)，比例 = (Lv−1)/(L×倍率)，满级约 4 倍。'
     '买价 = max(5, V)（每周加成 10 时减半）；卖价 = max(1, ⌊V/30⌋)；锻造金币 = 每个素材的 max(5, ⌊V/2⌋) 之和。',
     '- **盾**：格挡率和盾效果从 Lv1 到基础满级 L 线性增长，之后不再增加。',
     '- **锻造券**（道具 62–73）直接让对应部位的装备 +1/+2/+3 级。', '']

for df in range(3):
    M += [f'## 满级属性 — {DIFF[df]}', '',
          '属性栏为 Lv1 → 满级。“价值/卖价/锻造费”都是满级时的数值。', '',
          '| ID | 装备 | 种类 | 重量 | 满级 | ' + ' | '.join(STAT) + ' | 满级累计EXP | 满级价值 | 满级卖价 | 满级锻造费 |',
          '|---|---|---|---|---|' + '---|' * 6 + '---|---|---|---|']
    for r in EQ:
        x = r['diff'][df]
        cells = ' | '.join('' if (r['min'][i] == 0 and r['base'][i] == 0) else f"{r['min'][i]}→{x['max'][i]}" for i in range(6))
        M.append(f"| {r['id']} | {r['name']} | {kind(r)} | {WEIGHT[r['weight']]} | {x['maxLv']} | {cells} | {fmt(x['exp'])} | {fmt(x['value'])} | {fmt(x['sale'])} | {fmt(x['cost'])} |")
    M.append('')

M += ['## 其他数值', '',
      '| ID | 装备 | 种类 | 基础满级 L | 锻造EXP率 | 价格 | 商店等级 | 会心 | 素材值 Lv1 | 倍率 困难 | 倍率 噩梦 | 盾 | 说明 |',
      '|---|---|---|---|---|---|---|---|---|---|---|---|---|']
for r in EQ:
    sh = ''
    if 'shield' in r:
        s = r['shield']; sh = f"格挡 {rng(*s['cut'])}，{s['eff']} {rng(*s['effv'])}"
    M.append(f"| {r['id']} | {r['name']} | {kind(r)} | {r['L']} | {r['rate']} | {fmt(r['price'])} | {r['shopLv'] if r['shopLv'] >= 0 else '—'} | {r['crit']} | {r['w1']} | ×{r['diff'][1]['mul']} | ×{r['diff'][2]['mul']} | {sh} | {r['desc']} |")
open(os.path.join(HERE, 'equipment.md'), 'w', encoding='utf-8').write('\n'.join(M) + '\n')
print('equipment.md', len(EQ))


# ---------------------------------------------------------------- items
IT = []
for i in d['itemList_']:
    a = i['addExp_'] or [0, 0]
    IT.append({'id': i['id_'], 'name': i['name_'], 'type': i['type_'], 'price': i['shopPrice_'], 'shopLv': i['shopLv_'],
               'buy': max(5, i['shopPrice_']), 'sale': max(1, i['shopPrice_'] // 20),
               'exp': a, 'param': i['addParam_'], 'tier': i['effectLimit_'], 'cap': (i['effectLimit_'] + 1) * 10,
               'smith': [i['smithType_'], i['smithValue_']], 'job': JOB.get(i['birthJobID_'], '') if i['birthJobID_'] >= 0 else '',
               'desc': i['explain_'], 'sort': i['sortID_']})
IT.sort(key=lambda r: (r['type'], r['sort'], r['id']))

N = ['# 大海贼探险物语DX — 道具数据 / Item data', '',
     f'从游戏数据表导出（`itemList_`，{len(IT)} 种）。由 `make_gear.py` 生成，网页版见 [gear.html](gear.html)。', '',
     '## 规则（读自游戏代码 `data.ItemData`）', '',
     '- **强化道具 EXP**：每个随机取 EXP 范围内的整数（含两端）。每周加成 8 ×1.5，月卡加成 ×2，两者都有 ×2.5。',
     '- **属性提升**：每项属性随机取范围内的值。上限 = (上限档 + 1) × 10，按船员一生从食物得到的该属性总量计：已到上限得 0，超出部分截掉。',
     '- **价格**：买价 = max(5, 价格)（每周加成 10 时减半）；卖价 = max(1, ⌊价格/20⌋)。商店等级 “—” 表示商店不卖。',
     '- 强化道具喂给角色时 EXP 按 20 段缓动加入；角色满级时不会溢出（详见 AutoFeed）。', '',
     '## 强化道具', '',
     '| ID | 道具 | EXP | 平均EXP | ' + ' | '.join(STAT) + ' | 上限档 | 属性上限 | 价格 | 卖价 | 商店等级 | 每卖价EXP |',
     '|---|---|---|---|' + '---|' * 6 + '---|---|---|---|---|---|']
for r in IT:
    if r['type'] != 1: continue
    avg = (r['exp'][0] + r['exp'][1]) / 2
    ps = ' | '.join('' if p == [0, 0] else rng(*p) for p in r['param'])
    N.append(f"| {r['id']} | {r['name']} | {rng(*r['exp'])} | {avg:g} | {ps} | {r['tier']} | {r['cap']} | {fmt(r['price'])} | {fmt(r['sale'])} | {r['shopLv'] if r['shopLv'] >= 0 else '—'} | {avg / r['sale']:.2f} |")
N.append('')
for t in (0, 2, 3, 4, 5):
    rs = [r for r in IT if r['type'] == t]
    if not rs: continue
    N += [f'## {ITYPE[t]}', '', '| ID | 道具 | 价格 | 卖价 | 商店等级 | 说明 |', '|---|---|---|---|---|---|']
    for r in rs:
        N.append(f"| {r['id']} | {r['name']} | {fmt(r['price'])} | {fmt(r['sale'])} | {r['shopLv'] if r['shopLv'] >= 0 else '—'} | {r['desc']} |")
    N.append('')
open(os.path.join(HERE, 'items.md'), 'w', encoding='utf-8').write('\n'.join(N) + '\n')
print('items.md', len(IT))


# ---------------------------------------------------------------- gear.html
H = {'stat': STAT, 'cls': CLASS, 'type': TYPE, 'weight': WEIGHT, 'diff': DIFF, 'itype': ITYPE, 'eq': EQ, 'it': IT}
tpl = open(os.path.join(HERE, 'gear_template.html'), encoding='utf-8').read()
pay = json.dumps(H, ensure_ascii=False, separators=(',', ':')).replace('</', '<' + chr(92) + '/')
open(os.path.join(HERE, 'gear.html'), 'w', encoding='utf-8').write(tpl.replace('/*GEAR_DATA*/null', pay))
print('gear.html ok', len(pay))
