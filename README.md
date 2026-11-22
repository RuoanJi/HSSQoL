<a id="chinese"></a>

# HSSQoL — 大海贼探险物语DX 便利性 Mod

**中文** | [English](#english)

HSSQoL 是 Kairosoft《大海贼探险物语DX》（High Sea Saga DX，Steam / Windows 版）的便利性（QoL）Mod，基于 BepInEx 6 运行，不修改任何游戏文件。目前包含：

- **强化满级**：一键把角色的当前职业升到满级，并自动挑选消耗哪些食物。
- **游戏加速**：按 F2 开关，倍率可自由设置。
- **鼠标滚轮**：在列表中用滚轮上下移动。

> ⚠️ 本 Mod 会直接修改存档里的道具和经验值，且存档由 Steam 云同步。首次使用前请先备份 `saves` 文件夹。本项目与 Kairosoft 无关。

## 强化满级

![强化满级按钮](docs/feed-button.png)

在角色菜单里，「强化」右边会多出一个圆形的 **强化满级** 按钮（原来的「团队强化」往右移了一格）。点一下，Mod 就会给这个角色喂食物，直到当前职业升到满级，然后像游戏自己的强化流程一样，发放职业通关奖励（「转职」旁的星星 +1）。

这个按钮出现在两个地方：

- 船员列表 → 点选角色
- 主菜单 → 战斗编队 → 点选编队里的角色

### 怎么挑食物

游戏自带的批量喂食会把选中的数量全部吃掉，哪怕早就满级了也一样，多出来的经验全部浪费。强化满级会一口一口地喂，每一口之前都重新计算：

1. **还差多少经验**：当前职业满级所需经验 − 现有经验。
2. **每种食物大约给多少经验**：同一种食物每次给的经验是随机的（例如「新鲜蔬菜」约 78–88），所以 Mod 会先取样算出平均值。
3. **先拿能力加成**：只考虑不会让经验溢出的食物。如果其中有食物还能提升某项能力（没到这种食物的上限），就选「每点经验带来的预期能力加成」最高的那种。每种食物只提升一项能力，每口只加 0–1 或 0–2 点，而升满一个职业的经验有限，所以经验小的食物能吃更多口、拿到更多加成，稀有的大份料理也自然不会被用掉。某项能力到达上限后，就换成提升另一项能力的食物。可用 `PrioritizeStats` 关闭这一步。
4. **再追求经验效率：优先吃库存最多的**：在「平均经验不超过剩余需求」的食物里，选**库存最多**的那种（数量相同就选经验小的）。这样不会动用稀有的高级料理，各种食物的库存也会被慢慢拉平。这不是贪心地挑经验最高的。
5. **最后一口尽量少浪费**：如果所有食物都会超出剩余需求，就吃经验**最小**的那种。所以溢出的经验最多只有一小口。

实测两个角色从 Lv 12–13 升到 Lv 35，各吃了约 100 口，溢出的经验只有几十点。

### 和游戏自己的强化完全一致

每一口都严格按照游戏自身强化的规则处理（直接读取游戏代码得出，并与游戏自己的强化窗口逐项对比验证）：

- **能力加成**：每种食物对每项能力有一个随机范围。加成有上限，上限按食物等级而定：(等级 + 1) × 10，即 10 / 20 / 30 / 40。比较的是角色这项能力的累计加成（包括职业通关奖励），达到上限后这种食物就不再提升这项能力。
- **经验值**：与游戏一样分段加入，所以达到满级时的处理也与游戏相同。
- **职业通关**：发放职业通关奖励；所有职业都通关后，按游戏规则重置已通关职业列表。

如果食物不够升到满级，Mod 会吃完所有允许使用的食物，然后停在差一点满级的位置，不会发放职业通关奖励。

每一口都会记录在 `BepInEx\LogOutput.log` 里（`[autofeed]` 开头）：吃了什么、经验和等级前后的变化、库存的变化。

可在 `BepInEx\config\hssqol.cfg` 的 `[AutoFeed]` 中设置：

| 设置 | 默认 | 说明 |
|---|---|---|
| `KeepAtLeast` | 0 | 每种食物至少保留这么多个 |
| `ExcludeIds` | 空 | 永不使用的食物 ID，用逗号分隔，例如 `22,23`。每次喂食开始时，日志会列出所有食物的 ID |
| `MaxBites` | 3000 | 每次最多喂几口（安全上限） |
| `PrioritizeStats` | true | 先选还能提升能力的食物（见第 3 步） |
| `StatFoodMaxTier` | 3 | 为了能力加成最多动用到哪个等级的食物（0–3），调低可保留稀有料理 |
| `ButtonConfirm` | false | 设为 true 时需要点两次：第一次预览，第二次喂食 |
| `Method` | Direct | `Direct`：瞬间完成，没有动画。`Game`：改用游戏自己的团队强化窗口，每种食物弹出一次升级动画 |

## 游戏加速

按 **F2** 开启或关闭加速。开启时右上角会显示 `▶▶ x5`。

可在 `[Speed]` 中设置：

| 设置 | 默认 | 说明 |
|---|---|---|
| `Multiplier` | 5 | 加速倍率，1–10，可以是小数 |
| `ToggleKey` | F2 | 开关按键 |
| `ShowIndicator` | true | 是否显示右上角的倍率标签 |

原理和 Cheat Engine 的变速功能相同：让游戏读到的时间（包括系统日期时间）走得更快。不受游戏内部帧率补偿机制的限制，倍率设多少就是多少。需要注意：

- 开关后需要几秒钟才会完全生效或恢复。
- 开启期间游戏内的日期时间会走得比现实快，关闭后也不会倒退回去（与 Cheat Engine 相同），直到重新启动游戏。这期间保存的存档会带有略微超前的时间。

## 其他

- **鼠标滚轮**：在列表中用滚轮代替上下方向键（`[Input]`：`WheelScroll`、`WheelInvert`、`WheelStepsPerNotch`）。右键保持游戏原本的功能。
- **F1**：显示或隐藏调试面板（强化满级的最近一次结果），每次启动默认关闭（`[Debug]`）。

## 安装

### 第一步：安装 BepInEx 6（IL2CPP 版）

本游戏使用 Unity 的 **IL2CPP** 方式编译，不是 Mono。稳定版 BepInEx 5 只支持 Mono，所以必须使用 **BepInEx 6 的 IL2CPP 版本**。BepInEx 6 目前仍是预发布版本，要从 bleeding edge 构建页面下载。

1. 打开 <https://builds.bepinex.dev/projects/bepinex_be>，下载文件名形如 `BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.<编号>+<哈希>.zip` 的压缩包。
   - 必须选 **`Unity.IL2CPP`**（不是 `Unity.Mono`）。
   - 必须选 **`win-x86`**（游戏是 32 位的，x64 版无法加载）。
   - 已测试版本：**6.0.0-be.793**。更新的 be 版本通常也能用。
2. 打开游戏根目录：在 Steam 里右键游戏 → 管理 → 浏览本地文件（即 `KairoGames.exe` 所在的文件夹）。
3. 把压缩包里的全部内容解压到这个文件夹。完成后，`KairoGames.exe` 旁边应该有 `BepInEx\`、`dotnet\`、`winhttp.dll` 和 `doorstop_config.ini`。
4. 启动一次游戏。第一次启动会**比较慢**（可能要一两分钟），因为 BepInEx 需要联网下载 Unity 基础库，并生成 `BepInEx\interop`。出现标题画面后就可以关闭游戏。
5. 打开 `BepInEx\LogOutput.log`，如果能找到 `Chainloader startup complete`，就说明 BepInEx 已经正常工作。

### 第二步：安装本 Mod

1. 获取 `HSSQoL.dll`：从 [Releases](https://github.com/RuoanJi/HSSQoL/releases) 下载，或按下方「编译」自行编译。
2. 把它放到 `BepInEx\plugins\`（没有这个文件夹就新建一个）。
3. 启动游戏，读取存档后即可使用。第一次运行会生成配置文件 `BepInEx\config\hssqol.cfg`，修改配置后需要重启游戏才会生效。

**卸载**：删除 `BepInEx\plugins\HSSQoL.dll`。要完全移除 BepInEx，再删除游戏根目录下的 `winhttp.dll`（删掉它 BepInEx 就不会再加载）。

## 编译

需要 .NET SDK 6 或更新版本，以及一个已安装 BepInEx 并至少运行过一次的游戏目录（编译时要引用 `BepInEx\core` 和 `BepInEx\interop` 里的 DLL）。

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\High Sea Saga DX"
```

仓库可以放在任何位置。游戏目录按以下顺序确定：

1. 命令行参数 `-p:GameDir="..."`
2. 环境变量 `HSS_GAME_DIR`（设置一次即可，之后直接 `dotnet build -c Release`）
3. Steam 默认位置 `C:\Program Files (x86)\Steam\steamapps\common\High Sea Saga DX`

编译完成后会自动把 `HSSQoL.dll` 复制到 `BepInEx\plugins\`。游戏运行时 DLL 被占用，会跳过复制并给出警告。

## 许可证

[MIT](LICENSE)

---

<a id="english"></a>

# HSSQoL — Quality-of-life mod for High Sea Saga DX

[中文](#chinese) | **English**

HSSQoL is a quality-of-life mod for Kairosoft's *High Sea Saga DX* (大海贼探险物语DX; Steam / Windows). It runs on BepInEx 6 and doesn't modify any game files. It currently adds:

- **Power up to max (强化满级)**: one click levels a character's current job to max, choosing which food to use automatically.
- **Speed-up**: toggled with F2, at a multiplier you choose.
- **Mouse wheel**: scrolls up and down in lists.

> ⚠️ This mod changes items and EXP in your save, and saves sync through Steam Cloud. Back up your `saves` folder before first use. This project is not affiliated with Kairosoft.

## Power up to max

![Power-up-to-max button](docs/feed-button.png)

In the character menu, a round **强化满级** ("power up to max") button appears to the right of 强化 (Power up). The team power-up icon moves over one slot. One click feeds the character until their current job reaches max level. It then grants the job-clear bonus (the star next to 转职 goes up by one), just like the game's own power-up flow.

The button appears in both character menus:

- the crew list → click a character
- Main menu → combat roster → click a character in a party

### How it picks food

The game's own bulk feed eats the whole amount you select, even long after the character hits max level, and the extra EXP is wasted. Power up to max feeds one bite at a time and re-plans before every bite:

1. **EXP still needed:** EXP to max the current job, minus current EXP.
2. **EXP per food:** each food gives a random amount per bite (新鲜蔬菜, for example, gives about 78–88). The mod samples each food to get its average.
3. **Stat bonuses first:** only foods that fit within the remaining EXP need are considered. If any of them can still raise a stat (below that food's cap), it picks the one with the **best expected stat gain per EXP**. Each food raises one stat by 0–1 or 0–2 per bite, and a job only takes so much EXP, so small foods mean more bites and more stat gains. That also leaves big, rare dishes alone. Once a stat reaches a food's cap, it moves on to a food for another stat. Turn this step off with `PrioritizeStats`.
4. **Then EXP efficiency, most plentiful food first:** among foods whose average EXP fits within the remaining need, it eats the one you **hold the most of** (ties go to the smaller EXP). That spares rare high-tier dishes and evens out your stocks over time. It deliberately does not pick the biggest food first.
5. **Minimal overshoot:** when every food would overshoot, it eats the **smallest** one, so at most one small bite of EXP is wasted.

In testing, two characters went from Lv 12–13 to Lv 35 in about 100 bites each, overshooting by only a few dozen EXP.

### Exactly what the game's own power-up does

Every bite follows the game's own power-up rules. These were read from the game's code and checked against the game's own power-up window, result for result:

- **Stat bonuses:** each food raises each stat by a random amount within a range, up to a cap set by the food's tier: (tier + 1) × 10, i.e. 10 / 20 / 30 / 40. The cap is checked against the character's total bonus for that stat (including job-clear bonuses). Once a stat reaches a food's cap, that food no longer raises it.
- **EXP:** added in slices, like the game, so reaching max level is handled exactly as the game does.
- **Job clear:** grants the job-clear bonus. Once every job has been mastered, the mastered-job list resets, per the game's rules.

If there isn't enough food to reach max, it uses all allowed food, stops short of max, and does not grant the job-clear bonus.

Every bite is logged to `BepInEx\LogOutput.log` (lines starting with `[autofeed]`): the food, EXP and level before and after, and the stock left.

Settings, under `[AutoFeed]` in `BepInEx\config\hssqol.cfg`:

| Setting | Default | Meaning |
|---|---|---|
| `KeepAtLeast` | 0 | Never take any food below this count |
| `ExcludeIds` | empty | Comma-separated food ids never to use, e.g. `22,23`. Each feed logs every food's id at the start |
| `MaxBites` | 3000 | Safety cap on bites per feed |
| `PrioritizeStats` | true | Prefer foods that can still raise a stat (step 3) |
| `StatFoodMaxTier` | 3 | Highest food tier that may be spent on stats (0–3). Lower it to keep rare dishes |
| `ButtonConfirm` | false | If true, the button needs two clicks: preview, then feed |
| `Method` | Direct | `Direct`: instant, no animation. `Game`: uses the game's own team power-up window instead, with one level-up animation per food |

## Speed-up

Press **F2** to turn the speed-up on or off. While it's on, `▶▶ x5` shows in the top-right corner.

Settings, under `[Speed]`:

| Setting | Default | Meaning |
|---|---|---|
| `Multiplier` | 5 | Speed multiplier, 1–10, decimals allowed |
| `ToggleKey` | F2 | Key that toggles the speed-up |
| `ShowIndicator` | true | Show the speed label in the top-right corner |

It works the same way as Cheat Engine's speedhack: the game's clocks, including the system date and time, run faster. It isn't limited by the game's internal frame-rate catch-up, so the multiplier you set is the speed you get. Two things to know:

- It takes a few seconds to fully kick in or wear off after each toggle.
- While it's on, the in-game date and time run ahead of real time. They don't jump back when you turn it off (same as Cheat Engine), until you restart the game. Saves made in that time carry slightly-future timestamps.

## Other

- **Mouse wheel:** scrolls lists like the Up/Down keys (`[Input]`: `WheelScroll`, `WheelInvert`, `WheelStepsPerNotch`). Right-click keeps the game's own behaviour.
- **F1:** shows or hides the debug panel with the last power-up-to-max result. It's off at every launch (`[Debug]`).

## Install

### Step 1: Install BepInEx 6 (IL2CPP build)

The game is built with Unity's **IL2CPP** backend, not Mono. The stable BepInEx 5 only supports Mono, so you need the **IL2CPP build of BepInEx 6**. BepInEx 6 is still in pre-release, so it comes from the bleeding-edge build page.

1. Go to <https://builds.bepinex.dev/projects/bepinex_be> and download the file named like `BepInEx-Unity.IL2CPP-win-x86-6.0.0-be.<number>+<hash>.zip`.
   - It must be **`Unity.IL2CPP`** (not `Unity.Mono`).
   - It must be **`win-x86`**: the game is 32-bit, and the x64 build won't load.
   - Tested with **6.0.0-be.793**. Newer be builds usually work too.
2. Open the game folder: in Steam, right-click the game → Manage → Browse local files (the folder containing `KairoGames.exe`).
3. Extract everything from the zip into that folder. Afterwards you should see `BepInEx\`, `dotnet\`, `winhttp.dll` and `doorstop_config.ini` next to `KairoGames.exe`.
4. Start the game once. The first start is **slow** (a minute or two): BepInEx downloads Unity's base libraries, so it needs internet, and generates `BepInEx\interop`. Once the title screen appears, close the game.
5. Open `BepInEx\LogOutput.log`. If it contains `Chainloader startup complete`, BepInEx is working.

### Step 2: Install the mod

1. Get `HSSQoL.dll`: download it from [Releases](https://github.com/RuoanJi/HSSQoL/releases), or build it yourself (see Building below).
2. Put it in `BepInEx\plugins\` (create the folder if it doesn't exist).
3. Start the game and load your save. The config file `BepInEx\config\hssqol.cfg` is created on the first run. Restart the game after changing it.

**Uninstall:** delete `BepInEx\plugins\HSSQoL.dll`. To remove BepInEx entirely, also delete `winhttp.dll` from the game folder (without it, BepInEx no longer loads).

## Building

You need the .NET SDK 6 or later, and a game folder with BepInEx installed and run at least once (the build references the DLLs in `BepInEx\core` and `BepInEx\interop`).

```
dotnet build -c Release -p:GameDir="D:\SteamLibrary\steamapps\common\High Sea Saga DX"
```

The repo can live anywhere. The game folder is taken from, in order:

1. `-p:GameDir="..."` on the command line
2. the `HSS_GAME_DIR` environment variable (set it once, then just run `dotnet build -c Release`)
3. the default Steam location, `C:\Program Files (x86)\Steam\steamapps\common\High Sea Saga DX`

The build copies `HSSQoL.dll` into `BepInEx\plugins\`. If the game is running, the DLL is locked, so the copy is skipped with a warning.

## License

[MIT](LICENSE)
