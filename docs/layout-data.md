# Layout designer — data and rules / 布局设计器所需数据

Everything a separate ship-layout designer needs, exported from the game (2026-11-22) and checked against the live save.

## Data files (`docs/data/`)

| File | What's in it |
|---|---|
| `facilities.json` | All 80 facility types, unlocked or not: size (`widthNum`×`heightNum`), knowledge type and base (`getKnow`), param type and value, area effects, extra effect, crew stand points (`visitPoint`), build cost/time, category, whether it levels, your current level/EXP/owned count, the game's name tables |
| `ship.json` | Your ship (extension 2): both floors tile by tile (13×13 and 13×11) — floor, pillar (model 2), stairway (model 1), which building covers each tile, and per placed piece the game's own computed values (`paramAdd`, `exAddParam`, `paramValue`, `knowValue`, boxed-in flag) |
| `jobs_ships.json` | Every job's bedroom (`bedRoomModelId`, -1 = base room #4) and favourite facilities (`goodChips`); ship size table per extension level; your 32 crew (job, bedroom tile, 7 knowledge levels/EXP, wisdom, skill #26 bonus, party, mastered jobs); crew/party/item caps |

Human-readable versions: `facilities.md` (facility table, providers per knowledge slot, ship sizes), `jobs.md` (job stats, knowledge cost of all job switches, bedroom per job).

Names used throughout: knowledge types 近战, 远程, 海员, 探究心, 工作心, 勇敢战士, 秘传; param types 0 舒适 (comfort), 1 锻炼 (training), 2 解析 (analysis), 3 收获 (harvest).

## Rules (all verified: the model reproduces the game's values for all 126 placed pieces)

- **Grid**: tiles addressed (ax, ay); a multi-tile piece is stored at its **bottom-right** tile.
- **Placement**: every footprint tile must currently be plain floor (model type 0).
- **Neighbours**: other finished, selectable pieces with any tile within Manhattan distance ≤ 3 of any tile of this piece; each counted once. Pillars, stairways and floor pieces aren't selectable and give nothing.
- **Area effect**: neighbour's `[paramType, value]` adds `value + neighbourLv / 5` when it matches this piece's param type → `paramAdd`.
- **Extra effect**: neighbour's `[slot, value]` adds `value` to slot (0–3 params cap 500, 4–10 knowledge cap 100, 11 shopping cap 50, 12 resources cap 5).
- **Param value**: `paramValue + lv / 2 + paramAdd`, cap 500.
- **Knowledge per visit (facility part)**: `base + base × lv / 100 + lv / 2` + extra slot for its own knowledge type + training value / 10 (training-type only); then crew wisdom / 10, × (1 + skill #26 %), × 1.5 if a favourite facility of the visitor's job, × 2 weekly, × 1.2 subscription.
- **Boxed in**: no empty floor tile directly up/down/left/right of any of its tiles → knowledge base 1, extra 0, param 1, gives nothing. Diagonals, pillars and stairways don't count as open.
- **Facility level**: per type, max 300, 150 EXP per level; floor, stairways, bedrooms, warehouses, prison, monster house don't level.
- **Bedrooms** follow the resident's **current** job (the game rebuilds the tile when they sleep); one per crew member.
- **Crew movement**: 4 directions; building tiles walkable at ×51 cost (A*); crew pick the nearest facility whose knowledge type they haven't maxed (Lv 20).
- **Ship growth**: extension levels 0–4 (crew 12/24/32/48/60, parties 2–6). Each extension expands every floor once (+1 row at the bottom, +1 column each side; existing pieces shift one column right), twice when the new level is even; the extension to level 1 adds a pillar at the middle of the bottom row; stairways sit in the four corners. Floor sizes per level 0–4: floor 1 = 7×10, 9×11, 13×13, 15×14, 19×16; floor 2 = 5×10, 7×11, 11×13, 13×14, 17×16 (columns × rows). Exact grids in `data/ship_levels.json` and `facilities.md`; replaying the rule reproduces the level-2 save exactly.
