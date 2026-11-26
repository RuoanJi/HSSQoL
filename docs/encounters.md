# Sea encounters: boardings and bosses

How monsters end up on your deck at sea, in port, and during a sea investigation (海域调查), and where sea-area bosses come from. Decoded from the game code (`Player.UpperDeckEnemy`, `Player.CallUpperDeckEnemy`, `LandArea.ChoiceEnemyGroup`, `SurveyCourse`). For what each enemy drops, see [`loot.md`](loot.md).

## Summary

- **Every boarding uses one table: the regular enemy list of the sea area under the ship.** That holds while sailing, while docked at a port, and at the checkpoints of a sea investigation.
- **A port doesn't have its own enemy list.** A port in the sea sits on a tile of that sea area, so it gets that area's monsters.
- **The sea-area boss is not random.** It is a separate fixed group that only appears at the end of a sea investigation. Normal boardings can never produce it.

## Where boardings come from

| Situation | Code path | Enemy table |
|---|---|---|
| Sailing at sea | `Player.UpperDeckEnemy` → tile under ship (`WorldVehicle.GetUnderChip`) → `LandChip.GetArea` → `CallUpperDeckEnemy(area)` | area's `enemyListID_` |
| Docked at a port | same as sailing, plus a port-only check (below) | area's `enemyListID_` (the sea area the port sits in) |
| Sea investigation, 20/40/60/80% | `SurveyCourse.Script` → tile under ship → `LandChip.GetArea` → `CallUpperDeckEnemy(area)` | area's `enemyListID_` |
| Sea investigation, 100% | `SurveyCourse.Script` → boss battle (`SearchBattle`) | area's `bossGroup_` |

Dungeons pick their groups the same way, using `CourseData.battleGroupID_` as the list ID. Sea areas and dungeons share these ID numbers, so a dungeon also draws the sea groups with its ID. See [`dungeon-fights.md`](dungeon-fights.md).

## Picking a group (`LandArea.ChoiceEnemyGroup`)

1. Collect every enemy group whose ID matches the area's `enemyListID_`.
2. Pick one of them uniformly at random, then roll 0–100. Keep the group if the roll is at or below the group's ratio (`upperRatio_`). Otherwise pick again.
3. After 1000 failed tries, take a uniformly random group.

In effect a group's chance is proportional to its ratio. `CallUpperDeckEnemy` can place several groups on the deck at once; the count depends on your ship (`Ship.GetForceMax`) and isn't fully decoded.

## Timing at sea

While sailing, a timer counts down to the next boarding. Each sea area sets its own interval (`LandArea.GetEnemyInterval`), with some random spread. No boarding happens during a pirate-ship battle, or while more than 5 enemies are already on deck.

## The port-only check

While the ship is docked (vehicle state 3) at a port, a boarding can only happen while the port's `merchantTime_` value is above 0. Once it reaches 0, no monsters board.

This very likely matches the game's help text on anchoring (停泊). If the ship stays anchored at an island or sea area long enough, the place's 活力 (vitality) runs out. Then residents and crew stop coming and going, and enemies stop appearing. Vitality comes back with time or when you go to another island. The link between `merchantTime_` and 活力 hasn't been traced in code.

## Sea investigation and the boss (`SurveyCourse`)

When an investigation starts, the game sets up five checkpoints by progress:

| Progress | Event |
|---|---|
| 20% | Boarding from the area's regular enemy list |
| 40% | Boarding from the area's regular enemy list |
| 60% | Boarding from the area's regular enemy list |
| 80% | Boarding from the area's regular enemy list |
| 100% | Boss step |

At the boss step, the game waits until no enemies are left on deck. Then it shows the ボス出現!! (boss appeared) popup and starts the boss battle. The boss fight uses the area's `bossGroup_` at the area's enemy level (`enemyLv_`). Like dungeon bosses, it is one fixed group, not a roll.

The boss only comes from `bossGroup_`, which the normal boarding code never reads. It can turn up in a normal boarding only if the game data also lists that same group under the area's `enemyListID_`.

## Not yet confirmed

- **Can a sea area be investigated again after its boss is beaten?** The code that marks a sea area as cleared (`LandArea.clear_`) wasn't found; the field is only written when a save loads. If the investigate option stays available, every investigation ends with the boss at 100%.
- How many groups one boarding places on the deck.
- Whether `merchantTime_` is the 活力 described in the help text.
