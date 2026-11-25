# 大海贼探险物语DX — Facility data / 设施数据

Exported from the game's loaded data (2026-11-22) with a one-off dump; raw data in `data/facilities.json` (all fields).

## How the numbers work (verified against all 126 placed pieces on a real ship)

- **Knowledge per visit (facility part)**: `base + base × lv / 100 + lv / 2` + extra-effect slot for the facility's own knowledge type (max 100) + training value / 10 (training-type only). Then crew 賢さ/10, skill, favourite ×1.5 and bonuses. Facility level `lv` is per facility type, max 300 (150 EXP per level).
- **Param value**: `paramValue + lv / 2 + Σ matching area effects of neighbours`, max 500.
- **Neighbours**: other finished, selectable pieces with a tile within Manhattan distance ≤ 3 (|dx| + |dy| ≤ 3) of any tile of this one, each counted once.
- **Area effect**: a neighbour's `[param type, value]` adds `value + neighbour lv / 5` when the type equals this piece's param type.
- **Extra effect**: a neighbour's `[slot, value]` adds `value` (no level scaling) to that slot. Caps: param slots 500, knowledge slots 100, shopping 50, resources 5.
- **Boxed in**: a piece with no empty floor tile directly up/down/left/right of any of its tiles gets knowledge base 1, extra 0, param value 1, and gives no effects to others. Pillars, stairways and other pieces are not empty floor.
- **Footprint**: the anchor tile (the one stored in the save) is the bottom-right tile.

## Facilities and decorations

| ID | Name | Kind / category | Size | Knowledge (base) | Param type: value | Area effect on neighbours | Extra effect on neighbours | Levels |
|---|---|---|---|---|---|---|---|---|
| 6 | 医务室 | facility / 建物 | 1×1 | 探究心 80 | 舒适 160 | 舒适 +128 (+lv/5) | 勇敢战士知识 +5 | yes |
| 12 | 仓库 | warehouse / 設備 | 1×1 | — | 收获 44 | 收获 +22 (+lv/5)<br>解析 +6 (+lv/5) | 购物 shopping +1 | no |
| 13 | 巨大仓库 | huge warehouse / 設備 | 2×2 | — | 收获 96 | 收获 +40 (+lv/5)<br>解析 +14 (+lv/5) | 资源 resources +1 | no |
| 14 | 船员食堂 | restaurant / 建物 | 2×2 | 工作心 80 | 舒适 20 | 舒适 +46 (+lv/5)<br>收获 +16 (+lv/5) | 购物 shopping +4 | yes |
| 15 | 解析室 | lab / 設備 | 2×2 | 探究心 60 | 解析 48 | 解析 +64 (+lv/5)<br>舒适 +22 (+lv/5) | 探究心知识 +5 | yes |
| 17 | 格斗场 | facility / 建物 | 2×2 | 勇敢战士 90 | 锻炼 116 | 锻炼 +50 (+lv/5)<br>解析 +16 (+lv/5) | 近战知识 +10 | yes |
| 18 | 游泳池 | facility / 建物 | 2×2 | 海员 105 | 锻炼 60 | 锻炼 +112 (+lv/5)<br>舒适 +40 (+lv/5) | — | yes |
| 19 | 舞厅 | facility / 建物 | 2×2 | 秘传 85 | 锻炼 72 | 锻炼 +32 (+lv/5)<br>舒适 +10 (+lv/5) | 勇敢战士知识 +5 | yes |
| 20 | 厨房 | facility / 建物 | 2×1 | 工作心 80 | 舒适 20 | 舒适 +76 (+lv/5)<br>锻炼 +26 (+lv/5) | — | yes |
| 21 | 射击训练场 | facility / 建物 | 2×1 | 远程 90 | 锻炼 48 | 锻炼 +22 (+lv/5)<br>解析 +6 (+lv/5) | 远程知识 +5 | yes |
| 22 | 特训场 | facility / 建物 | 2×1 | 勇敢战士 85 | 锻炼 80 | 锻炼 +34 (+lv/5)<br>解析 +12 (+lv/5) | 近战知识 +5 | yes |
| 23 | 祈祷处 | facility / 建物 | 1×2 | 秘传 85 | 舒适 76 | 舒适 +32 (+lv/5)<br>锻炼 +10 (+lv/5) | 秘传知识 +8 | yes |
| 24 | 宝物资料馆 | facility / 建物 | 1×1 | 探究心 95 | 锻炼 48 | 收获 +34 (+lv/5)<br>解析 +12 (+lv/5) | 购物 shopping +5 | yes |
| 25 | 情报信息馆 | facility / 建物 | 1×1 | 海员 85 | 锻炼 48 | 锻炼 +22 (+lv/5)<br>解析 +6 (+lv/5) | 购物 shopping +2 | yes |
| 26 | 澡堂 | facility / 建物 | 1×1 | 工作心 75 | 舒适 12 | 舒适 +32 (+lv/5)<br>锻炼 +10 (+lv/5) | — | yes |
| 27 | 图书馆 | facility / 建物 | 1×1 | 探究心 100 | 锻炼 20 | 锻炼 +14 (+lv/5)<br>解析 +4 (+lv/5) | 探究心知识 +5 | yes |
| 28 | 探测室 | facility / 建物 | 1×1 | 海员 100 | 锻炼 36 | 锻炼 +20 (+lv/5)<br>解析 +6 (+lv/5) | 海员知识 +15 | yes |
| 29 | 职务室 | facility / 建物 | 1×1 | 探究心 90 | 锻炼 48 | 收获 +22 (+lv/5)<br>解析 +6 (+lv/5) | 探究心知识 +5 | yes |
| 30 | 画室 | facility / 建物 | 1×1 | 探究心 95 | 锻炼 24 | 锻炼 +16 (+lv/5)<br>舒适 +4 (+lv/5) | 探究心知识 +5 | yes |
| 31 | 通信室 | facility / 建物 | 1×1 | 探究心 100 | 锻炼 40 | 锻炼 +20 (+lv/5)<br>收获 +6 (+lv/5) | 海员知识 +5 | yes |
| 32 | 弹药库 | facility / 建物 | 1×1 | 远程 80 | 锻炼 20 | 锻炼 +38 (+lv/5)<br>解析 +12 (+lv/5) | 远程知识 +5 | yes |
| 33 | 饮料吧 | facility / 建物 | 1×1 | 近战 70 | 舒适 32 | 舒适 +46 (+lv/5)<br>锻炼 +16 (+lv/5) | 购物 shopping +1 | yes |
| 34 | 钓鱼竿保存处 | facility / 建物 | 1×1 | 工作心 80 | 锻炼 24 | 锻炼 +16 (+lv/5)<br>收获 +4 (+lv/5) | — | yes |
| 35 | 谈话室 | facility / 建物 | 1×1 | 海员 80 | 舒适 28 | 舒适 +26 (+lv/5)<br>锻炼 +8 (+lv/5) | 海员知识 +5 | yes |
| 36 | 厕所 | facility / 建物 | 1×1 | 工作心 80 | 舒适 32 | 舒适 +22 (+lv/5)<br>收获 +6 (+lv/5) | 资源 resources +1 | yes |
| 37 | 训练设施 | training room / 建物 | 1×1 | 近战 80 | 锻炼 20 | 锻炼 +14 (+lv/5)<br>收获 +4 (+lv/5) | 近战知识 +5 | yes |
| 38 | 宠物小屋 | monster house / 設備 | 1×1 | — | 舒适 180 | 收获 +56 (+lv/5)<br>舒适 +20 (+lv/5) | 工作心知识 +5 | no |
| 39 | 农田 | farm / 設備 | 1×1 | 工作心 75 | 收获 16 | 收获 +14 (+lv/5) | 资源 resources +1 | yes |
| 40 | 植树厂 | tree farm / 設備 | 1×1 | 工作心 100 | 收获 32 | 收获 +18 (+lv/5) | — | yes |
| 41 | 牧场(鸡) | ranch / 設備 | 1×1 | 海员 70 | 收获 48 | 收获 +22 (+lv/5) | — | yes |
| 42 | 牧场(猪) | ranch / 設備 | 1×1 | 海员 85 | 收获 56 | 收获 +26 (+lv/5) | 资源 resources +1 | yes |
| 43 | 牧场(牛) | ranch / 設備 | 1×1 | 海员 95 | 收获 92 | 收获 +40 (+lv/5) | 资源 resources +1 | yes |
| 44 | 电灯 | facility / 環境 | 1×1 | 海员 55 | 舒适 8 | 舒适 +20 (+lv/5)<br>锻炼 +6 (+lv/5) | 资源 resources +1 | yes |
| 45 | 椅子 | facility / 環境 | 1×1 | 探究心 55 | 舒适 4 | 舒适 +18 (+lv/5)<br>锻炼 +6 (+lv/5) | 购物 shopping +2 | yes |
| 46 | 观叶植物 | plant / 環境 | 1×1 | 海员 55 | 舒适 12 | 舒适 +22 (+lv/5)<br>锻炼 +6 (+lv/5) | 购物 shopping +2 | yes |
| 47 | 观叶树木 | plant / 環境 | 1×1 | 海员 85 | 锻炼 12 | 舒适 +26 (+lv/5)<br>锻炼 +8 (+lv/5) | 海员知识 +5 | yes |
| 48 | 传说之剑 | facility / 環境 | 1×1 | 勇敢战士 100 | 锻炼 220 | 锻炼 +104 (+lv/5)<br>解析 +36 (+lv/5) | 勇敢战士知识 +5 | yes |
| 49 | 守护之铠 | facility / 環境 | 1×1 | 秘传 95 | 锻炼 180 | 舒适 +82 (+lv/5)<br>锻炼 +28 (+lv/5) | 秘传知识 +5 | yes |
| 50 | 女神像 | facility / 環境 | 1×1 | 勇敢战士 95 | 舒适 192 | 舒适 +88 (+lv/5)<br>收获 +30 (+lv/5) | 勇敢战士知识 +10 | yes |
| 51 | 伟大的椅子 | facility / 環境 | 1×1 | 勇敢战士 90 | 舒适 120 | 舒适 +52 (+lv/5)<br>解析 +18 (+lv/5) | 勇敢战士知识 +5 | yes |
| 52 | 绘画角 | facility / 環境 | 1×1 | 勇敢战士 80 | 锻炼 60 | 舒适 +28 (+lv/5)<br>解析 +10 (+lv/5) | 购物 shopping +10 | yes |
| 53 | 水瓮 | facility / 環境 | 1×1 | 海员 80 | 舒适 20 | 舒适 +22 (+lv/5)<br>锻炼 +6 (+lv/5) | 资源 resources +1 | yes |
| 54 | 指南针 | facility / 環境 | 1×1 | 海员 105 | 锻炼 252 | 舒适 +124 (+lv/5)<br>解析 +44 (+lv/5) | 探究心知识 +5 | yes |
| 55 | 水槽 | facility / 環境 | 1×1 | 工作心 90 | 锻炼 112 | 舒适 +48 (+lv/5)<br>收获 +16 (+lv/5) | 购物 shopping +3 | yes |
| 56 | 幸运之鸟 | facility / 環境 | 1×1 | 远程 95 | 锻炼 200 | 舒适 +92 (+lv/5)<br>收获 +32 (+lv/5) | 购物 shopping +15 | yes |
| 57 | 牢房 | prison / 設備 | 1×1 | — | 锻炼 40 | 锻炼 +20 (+lv/5)<br>解析 +6 (+lv/5) | — | no |
| 77 | 桌游室 | facility / 建物 | 1×1 | 海员 85 | 舒适 80 | 解析 +34 (+lv/5)<br>锻炼 +12 (+lv/5) | 秘传知识 +5 | yes |
| 78 | 投标盘 | facility / 建物 | 1×1 | 远程 80 | 舒适 64 | 锻炼 +34 (+lv/5)<br>解析 +12 (+lv/5) | 海员知识 +5 | yes |
| 79 | 花 | facility / 環境 | 1×1 | 探究心 75 | 舒适 8 | 舒适 +18 (+lv/5)<br>收获 +6 (+lv/5) | 秘传知识 +5 | yes |

## Bedrooms (寝室)

Bedrooms give no knowledge to visitors and can't level, but several carry large extra effects (up to +20) and are 1×1.

| ID | Param: value | Area effect | Extra effect |
|---|---|---|---|
| 4 | 舒适 48 | 舒适 +10 (+lv/5) | — |
| 5 | 舒适 32 | 舒适 +10 (+lv/5) | — |
| 7 | 舒适 64 | 舒适 +12 (+lv/5) | — |
| 8 | 舒适 80 | 舒适 +12 (+lv/5) | — |
| 9 | 舒适 96 | 舒适 +14 (+lv/5) | — |
| 10 | 舒适 288 | 舒适 +32 (+lv/5)<br>锻炼 +10 (+lv/5) | 海员知识 +10 |
| 11 | 舒适 240 | 舒适 +42 (+lv/5)<br>收获 +14 (+lv/5) | 秘传知识 +8 |
| 59 | 舒适 256 | 舒适 +204 (+lv/5) | 探究心知识 +10 |
| 60 | 舒适 64 | 舒适 +6 (+lv/5)<br>锻炼 +2 (+lv/5) | 工作心知识 +5 |
| 61 | 舒适 32 | 舒适 +8 (+lv/5)<br>收获 +2 (+lv/5) | 资源 resources +3 |
| 62 | 舒适 80 | 舒适 +30 (+lv/5) | 工作心知识 +10 |
| 63 | 舒适 32 | 舒适 +6 (+lv/5)<br>收获 +2 (+lv/5) | 海员知识 +10 |
| 64 | 舒适 128 | 舒适 +24 (+lv/5)<br>收获 +8 (+lv/5) | 秘传知识 +5 |
| 65 | 舒适 64 | 舒适 +8 (+lv/5)<br>锻炼 +2 (+lv/5) | 远程知识 +15 |
| 66 | 舒适 96 | 舒适 +12 (+lv/5)<br>解析 +4 (+lv/5) | 探究心知识 +10 |
| 67 | 舒适 112 | 舒适 +14 (+lv/5)<br>解析 +4 (+lv/5) | 探究心知识 +10 |
| 68 | 舒适 160 | 舒适 +6 (+lv/5) | 勇敢战士知识 +5 |
| 69 | 舒适 64 | 舒适 +12 (+lv/5)<br>锻炼 +4 (+lv/5) | 近战知识 +15 |
| 70 | 舒适 288 | 舒适 +38 (+lv/5)<br>锻炼 +12 (+lv/5) | 勇敢战士知识 +15 |
| 71 | 舒适 320 | 舒适 +44 (+lv/5)<br>解析 +14 (+lv/5) | 秘传知识 +20 |
| 72 | 舒适 128 | 舒适 +16 (+lv/5)<br>锻炼 +4 (+lv/5) | 海员知识 +10 |
| 73 | 舒适 64 | 舒适 +16 (+lv/5)<br>收获 +4 (+lv/5) | 购物 shopping +5 |
| 74 | 舒适 144 | 舒适 +14 (+lv/5)<br>收获 +4 (+lv/5) | 海员知识 +5 |
| 75 | 舒适 96 | 舒适 +2 (+lv/5) | 勇敢战士知识 +15 |
| 76 | 舒适 160 | 舒适 +20 (+lv/5)<br>解析 +6 (+lv/5) | 秘传知识 +10 |

## Fixed and floor pieces

| ID | Name | Note |
|---|---|---|
| 0 | 地板 | empty floor |
| 1 | 楼梯 | stairway (blocks like a building, gives nothing) |
| 2 | 柱子 | pillar (blocks like a building, gives nothing: not selectable) |
| 3 | 拼块地板 | floor variant (not selectable, gives nothing) |
| 16 | 未使用 | unused in game |
| 58 | 红色绒毯 | floor variant (not selectable, gives nothing) |

## Extra-effect providers by knowledge type

Who raises each knowledge slot (the term capped at 100). Value per provider in range; no level scaling.

| Knowledge slot | Providers (value) |
|---|---|
| 近战知识 | 寝室 #69 +15, 格斗场 +10, 训练设施 +5, 特训场 +5 |
| 远程知识 | 寝室 #65 +15, 弹药库 +5, 射击训练场 +5 |
| 海员知识 | 探测室 +15, 寝室 #72 +10, 寝室 #63 +10, 寝室 #10 +10, 通信室 +5, 谈话室 +5, 观叶树木 +5, 投标盘 +5, 寝室 #74 +5 |
| 探究心知识 | 寝室 #67 +10, 寝室 #66 +10, 寝室 #59 +10, 解析室 +5, 职务室 +5, 画室 +5, 指南针 +5, 图书馆 +5 |
| 工作心知识 | 寝室 #62 +10, 寝室 #60 +5, 宠物小屋 +5 |
| 勇敢战士知识 | 寝室 #75 +15, 寝室 #70 +15, 女神像 +10, 舞厅 +5, 寝室 #68 +5, 医务室 +5, 传说之剑 +5, 伟大的椅子 +5 |
| 秘传知识 | 寝室 #71 +20, 寝室 #76 +10, 祈祷处 +8, 寝室 #11 +8, 花 +5, 桌游室 +5, 寝室 #64 +5, 守护之铠 +5 |

## Ship size / 船只扩建

The player ship is 迷你军舰 (ship data #0), with extension levels 0–4. Each extension (`SubForm.ProAddShipSize`, opened by a story/event) raises the level, swaps the deck map and regrows both interior floors (`AddShipInMap`: about +1 row and +2 columns per floor, pillars and stairways re-placed). The interior stays at 2 floors.

| Extension level | Max crew | Max parties | Deck map |
|---|---|---|---|
| 0 | 12 | 2 | 0 |
| 1 | 24 | 3 | 1 |
| 2 | 32 | 4 | 2 |
| 3 | 48 | 5 | 3 |
| 4 | 60 | 6 | 4 |

Placement rule (`ShipInChip.CheckBuild`): every tile of the footprint must currently be plain floor (model type 0). Floor sizes per level are listed below.

## Ship floors at every extension level / 各扩建等级的船舱布局

Derived from the base layouts in the game's map data (`ShipInMap.Create`) and the extension rule in `SubForm.ProAddShipSize` / `AddShipInMap`: each extension expands every floor once (+1 row at the bottom, +1 column on each side, so existing pieces shift one column right), and a second time when the new level is even. The extension to level 1 also adds a pillar in the middle of the new bottom row. Stairways are rebuilt in the four corners. Replaying this reproduces a real level-2 ship's floors exactly (sizes, pillars, stairways).

`S` stairway, `P` pillar, `.` floor. (Starting furniture from the base map isn't shown.)

### Floor 1, level 0: 7 columns × 10 rows, 64 free tiles
```
S . . . . . S
. . . . . . .
. . . P . . .
. . . . . . .
. . . . . . .
. . . . . . .
. . . . . . .
. . . P . . .
. . . . . . .
S . . . . . S
```
### Floor 1, level 1: 9 columns × 11 rows, 92 free tiles
```
S . . . . . . . S
. . . . . . . . .
. . . . P . . . .
. . . . . . . . .
. . . . . . . . .
. . . . . . . . .
. . . . . . . . .
. . . . P . . . .
. . . . . . . . .
. . . . . . . . .
S . . . P . . . S
```
### Floor 1, level 2: 13 columns × 13 rows, 162 free tiles
```
S . . . . . . . . . . . S
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
S . . . . . . . . . . . S
```
### Floor 1, level 3: 15 columns × 14 rows, 203 free tiles
```
S . . . . . . . . . . . . . S
. . . . . . . . . . . . . . .
. . . . . . . P . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . P . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . P . . . . . . .
. . . . . . . . . . . . . . .
. . . . . . . . . . . . . . .
S . . . . . . . . . . . . . S
```
### Floor 1, level 4: 19 columns × 16 rows, 297 free tiles
```
S . . . . . . . . . . . . . . . . . S
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . P . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . P . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . P . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . . . .
S . . . . . . . . . . . . . . . . . S
```
### Floor 2, level 0: 5 columns × 10 rows, 44 free tiles
```
S . . . S
. . . . .
. . P . .
. . . . .
. . . . .
. . . . .
. . . . .
. . P . .
. . . . .
S . . . S
```
### Floor 2, level 1: 7 columns × 11 rows, 70 free tiles
```
S . . . . . S
. . . . . . .
. . . P . . .
. . . . . . .
. . . . . . .
. . . . . . .
. . . . . . .
. . . P . . .
. . . . . . .
. . . . . . .
S . . P . . S
```
### Floor 2, level 2: 11 columns × 13 rows, 136 free tiles
```
S . . . . . . . . . S
. . . . . . . . . . .
. . . . . P . . . . .
. . . . . . . . . . .
. . . . . . . . . . .
. . . . . . . . . . .
. . . . . . . . . . .
. . . . . P . . . . .
. . . . . . . . . . .
. . . . . . . . . . .
. . . . . P . . . . .
. . . . . . . . . . .
S . . . . . . . . . S
```
### Floor 2, level 3: 13 columns × 14 rows, 175 free tiles
```
S . . . . . . . . . . . S
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
. . . . . . P . . . . . .
. . . . . . . . . . . . .
. . . . . . . . . . . . .
S . . . . . . . . . . . S
```
### Floor 2, level 4: 17 columns × 16 rows, 265 free tiles
```
S . . . . . . . . . . . . . . . S
. . . . . . . . . . . . . . . . .
. . . . . . . . P . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . P . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . P . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
. . . . . . . . . . . . . . . . .
S . . . . . . . . . . . . . . . S
```
