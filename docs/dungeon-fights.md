# Dungeon fights: enemies, group count and bosses

How the game builds a random fight in a dungeon (迷宫), what level the enemies are, and when the boss appears. Decoded from the game code (`CourseData.ChoiceEnemyGroupIndex`, `Player.GetEnemyGroup` / `GetChoiceEnemyGroup`, `SearchBattle._init`, `FieldBase.AddEnemyGroup`, `Player.GetEnemyLvRatio`, `SearchCourse.FirstStart`). The rules were checked with a spawn log of 16 fights in #116 高级迷宫 (冰冷岛) in nightmare mode (2026-11-25). For what each enemy drops and the numbers for every dungeon, see [`loot.md`](loot.md) / [`loot.html`](https://ruoanji.github.io/HSSQoL/docs/loot.html). For sea boardings, see [`encounters.md`](encounters.md).

## Summary

- **A random dungeon fight is several enemy groups, not one.** The game picks one group, then keeps drawing more from the same pool until it has the target count. That count grows with the enemy level: #116 has 4 groups per fight in normal mode and 8 in nightmare mode.
- **The pool is every group with the dungeon's enemy-list ID**, whether the game data marks it as a sea group or a dungeon group. Sea areas and dungeons share these ID numbers, so a dungeon is mostly fought against sea groups.
- **Enemy level = dungeon level × game mode**: ×1 normal, ×2 hard mode (困难), ×5 nightmare mode (噩梦). Analyze-item drop levels rise with it.
- **The boss is one fixed group**, and you fight every enemy in it at once. In most dungeons the boss only appears until you have beaten it once. Later runs of a repeatable (可重复) dungeon end without a boss.

## How one random fight is built

1. **Pool.** `Player.GetEnemyGroup(listId)` collects every enemy group whose `upperID_` equals the dungeon's `battleGroupID_`. It doesn't check `upperType_` (0 = sea, 1 = dungeon). A group with a `campaign_` tag only joins once your stamina rank (体力等级, `GetStaminaRank`) is 40 or higher.
2. **First group.** `Player.GetChoiceEnemyGroup` picks a group from the pool uniformly at random. It then rolls 0–100 and keeps the group if the roll is at most the group's `upperRatio_`. Otherwise it picks again. A group's chance is therefore proportional to ratio + 1.
3. **Enemy level.** `SearchBattle` gets the dungeon's `lv_` × `Player.GetEnemyLvRatio`: 1 normally, 2 with the hard-mode flag (event flag 2), 5 with the nightmare flag (event flag 13). Nightmare overrides hard.
4. **Group count.** `SearchBattle._init` works out a target count. It then draws more groups from the same pool, with the same weighting, until the fight has that many. Repeats are allowed.
5. **Spawn.** `FieldBase.AddEnemyGroup` spawns every member of every drawn group at once.

### Group count

With `lv` = enemy level and rank tier `t` = `CourseData.GetRank(lv) / 10` (F = 0, E = 1, D = 2, C = 3, …):

```
count = max(lv / 50 + 1, 2 × t)       (integer division)
        at least 4 in hard mode
        at most 25
```

At rank F (t = 0) in normal mode, the fight is the first group alone. This also needs `MyConfig.ENEMY_LV_LIMIT_BREAK` > lv, which is assumed and wasn't read from the game.

| Example | Enemy lv | Rank | Groups per fight |
|---|---:|---|---:|
| #116 normal | 75 | D+ (t = 2) | max(2, 4) = **4** |
| #116 hard | 150 | D+++ (t = 2) | max(4, 4, 4) = **4** |
| #116 nightmare | 375 | C (t = 4) | max(8, 8) = **8** |

The hard-mode and nightmare flags are separate. The docs count nightmare without the hard-mode minimum of 4, which only matters for dungeons below Lv 30.

### Group contents

- A group's `data_` has up to two rows. These are front and back rows of the same fight, not waves: both spawn together.
- A member is `[kind, id]` (kind 0 = monster, 1 = human enemy). A member written `[kind, id, lo, hi]` spawns `Lib.GetRand(lo, hi)` copies, and lo..hi is inclusive. In #116, groups 76, 77 and 79 hold 1–3 copies of 骨子剑士, 河童太郎 and 骨子海贼.

## Worked example: #116 高级迷宫 (冰冷岛, 可重复, 体力 10)

The pool for list ID 7 has 13 groups: the 9 sea groups 72–80, the 3 dungeon groups 81–83, and campaign group 477 at stamina rank 40+. The weight is ratio + 1.

| Group | Members (front / back) | Ratio | Share of draws (rank 40+) |
|---|---|---:|---:|
| 72 | 骨子剑士, 骨子剑士 / 顾家风暴 | 60 | 23.1% |
| 73 | 河童太郎, 越前螃蟹 | 50 | 19.3% |
| 74 | 骨子剑士, 骨子剑士, 骨子海贼 / 骨子船长 | 40 | 15.5% |
| 76 | 骨子剑士 ×1–3 | 19 | 7.6% |
| 77 | 河童太郎 ×1–3 | 18 | 7.2% |
| 78 | 骨子海贼, 蘑菇原人 / 骨子船长 | 17 | 6.8% |
| 79 | 骨子海贼 ×1–3 | 16 | 6.4% |
| 75 | 骨子剑士, 棉花糖大王 | 15 | 6.1% |
| 82 | 舞者, 维京海盗 / 牛楠楠 | 5 | 2.3% |
| 83 | 僧侣, 僧侣, 维京海盗 / 僧侣, 王国兵 | 4 | 1.9% |
| 81 | 鲁莽汉, 维京海盗 / 风拳龙 | 3 | 1.5% |
| 477 | 青涩水龙 (campaign) | 3 | 1.5% |
| 80 | 蘑菇原人, 河童太郎 / 骨子船长 | 1 | 0.8% |

The human-enemy groups that the old loot page listed as the dungeon's only enemies (81–83) together make up under 6% of draws.

**What the spawn log showed** (16 fights, nightmare mode, stamina rank 56):

- Every fight drew from exactly this 13-group pool, and the first group spawned was always the picked group.
- Every fight had 8 groups and 18–25 enemies, all at lv 375. One fight parses as 7 groups, because a ×3 group can't be told apart from ×1 + ×2.
- Groups 76, 77 and 79 spawned 1, 2 and 3 copies.
- Over the 111 extra groups drawn, the shares were close to the weights above.

## Bosses

- The boss is a fixed event at 100% in `CourseData.fixEvent_`. Its search event runs script `[4, groupId]` (`SCR_SPECIFY_BATTLE`) and fights that one group, with every member at once. Nothing is rolled.
- **One-time or every run.** When a run starts, `SearchCourse.FirstStart` adds a fixed event only if it has never run (`exeNum_` = 0) or its `oneLimit_` is 1. The name reads backwards: `oneLimit_` = 0 means the event happens once. Of the dungeons' boss events, 156 are one-time and 38 return every run.
- When a one-time boss is gone, the dungeon keeps the same number of events and fills the freed slot like the others. #116: the first run has 2 guaranteed fights, a chest, the boss and 2 random slots. Later runs have 2 guaranteed fights, a chest and 3 random slots.
- In the code, only BOSS迷宫 (course type 4) marks its boss fight as a boss battle. In other dungeons, the boss fight is an ordinary fight against the fixed group.

In the spawn log, none of the #116 boss's members (characters 102–106) appeared across several repeat runs. That matches its event 119 having `oneLimit_` = 0.

## Not yet confirmed

- **Is the boss fight topped up with extra groups too?** `SearchBattle._init` runs the same fill for every fight it builds. The boss group has no list ID of its own (`upperID_` = -1), so the code appears to fall back to the dungeon's list. If so, the boss fight would be the boss group plus random groups up to the count. No boss fight was logged, so this is unverified.
- The value of `MyConfig.ENEMY_LV_LIMIT_BREAK`, which decides whether a rank-F fight in normal mode skips the extra groups.
- Whether the hard-mode flag stays set in nightmare mode. If it does, nightmare fights have at least 4 groups.
- How many groups a sea fight has. It also draws several groups, through `LandArea.ChoiceEnemyGroup`, but the count wasn't decoded (see [`encounters.md`](encounters.md)).
