using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace HSSQoL;

/// <summary>
/// Chinese text for every config entry. Apply() puts it above the English description, so
/// hssqol.cfg shows both. Entries without a translation are logged at startup.
/// </summary>
internal static class ConfigZh
{
    private static readonly Dictionary<string, string> Text = new()
    {
        // [AutoFeed] 强化满级
        ["AutoFeed.KeepAtLeast"] = "每种食物至少保留这么多个，不会吃到低于这个数。",
        ["AutoFeed.ExcludeIds"] = "永不使用的食物 ID，用逗号分隔，例如 22,23。每次喂食开始时，日志会列出所有食物的 ID。",
        ["AutoFeed.MaxBites"] = "每次最多喂几口（安全上限）。",
        ["AutoFeed.PrioritizeStats"] = "先选还能提升能力的食物（每点经验带来的预期能力加成最高的），再按「库存最多」的规则选。只考虑不超出剩余所需经验的食物，所以不会浪费经验。",
        ["AutoFeed.StatFoodMaxTier"] = "为了能力加成最多动用到哪个等级的食物（0-3）。调低可保留稀有的高级料理。",
        ["AutoFeed.JobClearWindow"] = "升到满级时弹出游戏自己的职业通关窗口（职业通关动画），关闭窗口时由游戏发放通关奖励，和游戏自己的流程完全一样。设为 false 则不弹窗，直接发放奖励。",
        ["AutoFeed.ButtonConfirm"] = "设为 true 时，菜单里的按钮要点两次（第一次预览，第二次喂食）。设为 false 时点一次就喂。",
        ["AutoFeed.Method"] = "Direct：瞬间完成，按游戏自己的喂食规则逐口处理（经验、带上限的能力加成、职业通关）。Game：改为对计划中的每种食物打开游戏自己的团队强化窗口（每种食物一次升级动画）。",

        // [AutoSell] 批量出售
        ["AutoSell.Enabled"] = "在港口商店的出售页面显示批量出售按钮。",
        ["AutoSell.ThresholdPercent"] = "某样东西的持有数超过持有上限的这个百分比时才卖。",
        ["AutoSell.Keep"] = "卖的东西每样保留多少个。",
        ["AutoSell.MinCap"] = "持有上限低于这个值时什么都不做。",
        ["AutoSell.ExcludeIds"] = "永远不卖的东西，用逗号分隔：游戏里显示的名字，或 e:<id>（装备）/ i:<id>（道具）。",
        ["AutoSell.Line1"] = "按钮文字第一行。",
        ["AutoSell.Line2"] = "按钮文字第二行。",
        ["AutoSell.ButtonX"] = "按钮中心在出售页面上的 X 坐标（游戏单位）。",
        ["AutoSell.ButtonY"] = "按钮中心在出售页面上的 Y 坐标（游戏单位）。",

        // [ContinuousExplore] 连续探索
        ["ContinuousExplore.Enabled"] = "在迷宫出发（选择队伍）页面显示连续探索按钮。",
        ["ContinuousExplore.ButtonX3"] = "按钮中心位置，占窗口宽度的比例（默认：左下角的空位，在「返回」对面）。",
        ["ContinuousExplore.ButtonY3"] = "按钮中心位置，占窗口高度的比例。",
        ["ContinuousExplore.IdleFrames"] = "每次回港后，要连续多少帧没有打开任何窗口才再次出发（让游戏自己的回港事件先显示）。",
        ["ContinuousExplore.MaxRuns"] = "最多连续探索几次（0 = 直到体力用完）。",

        // [CrewWork] 船员只做勾选的活动
        ["CrewWork.StrictCheckboxes"] = "船员只做活动设置里勾选的活动（收集甲板道具、补给、解析、船内设施）。一个都没勾时和原版游戏一样。修改后需重启游戏。",

        // [Debug] 调试
        ["Debug.OverlayToggleKey"] = "显示或隐藏屏幕上的调试信息（强化满级面板）。每次启动默认关闭。日志总是开启。",
        ["Debug.MenuProbe"] = "启用 F6：把一帧的菜单绘制调用写进日志（会每帧挂钩 Graphics，平时请保持关闭）。",

        // [DungeonSkip] 跳过迷宫结算
        ["DungeonSkip.Enabled"] = "快进迷宫通关结算（横幅、宝箱展示、队伍经验结算）。用 ToggleKey 切换，会记住上次的设置。",
        ["DungeonSkip.ToggleKey"] = "开关「跳过迷宫结算」的按键。",
        ["DungeonSkip.ExtraUpdates"] = "这些画面每帧额外运行的次数（在游戏自己按住确定键时的 6-15 次之外）。",
        ["DungeonSkip.Types"] = "要快进的画面（SubForm）类型：41 迷宫通关，79 队伍经验结算。80 = 职业通关窗口（通常不放在这里，用 F4）。",
        ["DungeonSkip.AutoImprison"] = "开启时：迷宫俘虏全部直接关进牢房（等同游戏的「全部收监」并选「是」），不弹列表和确认框。",
        ["DungeonSkip.SkipTalk"] = "开启时：迷宫里的对话窗口（SubForm 类型 0，例如 Boss 对话）也快进。迷宫外的对话不受影响。",
        ["DungeonSkip.ShowIndicator"] = "快进开启时在右上角显示一个小标签。",

        // [EggSell] 全部出售（蛋）
        ["EggSell.Enabled"] = "在怪物牧场的出售蛋列表上显示全部出售按钮。",
        ["EggSell.Line1"] = "按钮文字第一行。",
        ["EggSell.Line2"] = "按钮文字第二行。",
        ["EggSell.ButtonX3"] = "按钮中心在出售蛋列表上的 X 坐标（游戏单位；默认在「切换」上方）。",
        ["EggSell.ButtonY3"] = "按钮中心在出售蛋列表上的 Y 坐标（游戏单位）。",

        // [Export] 导出
        ["Export.ShipExportKey"] = "把船舱布局、设施等级和相关数据导出到 BepInEx\\hssqol-ship.json，供船舱布局设计器使用。",
        ["Export.ExportOnLoad"] = "读取存档后（船员载入时）也自动导出一次。",
        ["Export.DataDumpKey"] = "把游戏的主数据表导出到 BepInEx\\hssqol-data.json（数据挖掘用）。",
        ["Export.DataDumpOnBoot"] = "游戏数据载入后立刻自动导出一次数据（不需要读档）。",

        // [FeedButton] 强化满级按钮外观
        ["FeedButton.Enabled"] = "在角色菜单的「強化」旁边显示强化满级按钮。",
        ["FeedButton.Line1"] = "按钮文字第一行。",
        ["FeedButton.Line2"] = "按钮文字第二行。",
        ["FeedButton.Line2Indent"] = "第二行文字向右缩进的距离（游戏单位）。",
        ["FeedButton.OffsetY"] = "文字上移（负数）或下移（正数）。",
        ["FeedButton.JobChangeOffsetX"] = "转职页面：把这一对按钮从「确定」和「道具」的中点向左（负数）或向右（正数）移动。",
        ["FeedButton.SwitchLine1"] = "转职页面「转职满级」按钮的第一行文字。",
        ["FeedButton.SwitchLine2"] = "转职页面「转职满级」按钮的第二行文字。",
        ["FeedButton.ChainLine1"] = "转职页面「连续转职」按钮的第一行文字。",
        ["FeedButton.ChainLine2"] = "转职页面「连续转职」按钮的第二行文字。",
        ["FeedButton.Gap"] = "按钮和被右移的「团队强化」图标之间的间距。",
        ["FeedButton.PixelSize"] = "圆形按钮上一个「像素」的大小（游戏单位；1 ≈ 游戏自己的像素画）。",
        ["FeedButton.RingWidth"] = "外圈较亮的圆环宽度（以圆形像素计）。",
        ["FeedButton.DiameterInset"] = "圆比 34 单位的图标区域小多少。",
        ["FeedButton.CenterDarkenPercent"] = "圆心比外圈暗多少（百分比）。",
        ["FeedButton.TextColor"] = "文字颜色，AARRGGBB 十六进制。",
        ["FeedButton.OutlineColor"] = "文字描边颜色，AARRGGBB 十六进制。",
        ["FeedButton.NativeOutline"] = "使用游戏自己的描边文字（DrawStringBold）。设为 false 时，在 8 个方向按 OutlineWidth 偏移绘制描边。",
        ["FeedButton.OutlineWidth"] = "NativeOutline 为 false 时的描边粗细（游戏单位，0 = 无描边）。",
        ["FeedButton.FontScalePercent"] = "文字大小，占菜单字体的百分比。",

        // [FormationFit] 迷宫战斗阵型显示
        ["FormationFit.Enabled"] = "迷宫战斗中每排 4 人时，收紧每一排，让最下面的位置不被截掉。",
        ["FormationFit.BottomY"] = "每排最下面位置的战斗 Y 坐标，越小越靠上（游戏每排 4 人时用 83，每排 3 人时用 67）。",
        ["FormationFit.SlotSpacing"] = "同一排相邻位置的纵向间距（游戏每排 4 人时用 18，每排 3 人时用 17）。",

        // [Input] 鼠标滚轮
        ["Input.WheelScroll"] = "鼠标滚轮发送上 / 下方向键。",
        ["Input.WheelInvert"] = "反转滚轮方向。",
        ["Input.WheelStepsPerNotch"] = "滚轮每格按几次方向键。",

        // [ItemHotkey] 道具快捷键
        ["ItemHotkey.Enabled"] = "道具快捷键：队伍在探索（迷宫或海上）且没有打开窗口时，按键使用一个对应的药品。",
        ["ItemHotkey.BattleOnly"] = "只在探索队伍战斗中（迷宫或海上探索的战斗）才使用道具，船上甲板战不算。false = 探索中任何时候都可使用。",
        ["ItemHotkey.ShowToast"] = "每次使用快捷键后在屏幕上短暂显示提示。",
        ["ItemHotkey.HpSmall"] = "使用一个迷你回复药（道具 ID 25）的按键。None = 不设键。",
        ["ItemHotkey.HpMedium"] = "使用一个回复药剂（道具 ID 26）的按键。None = 不设键。",
        ["ItemHotkey.HpLarge"] = "使用一个回复秘药（道具 ID 27）的按键。None = 不设键。",
        ["ItemHotkey.SpSmall"] = "使用一个 SP回复水（道具 ID 28）的按键。None = 不设键。",
        ["ItemHotkey.SpMedium"] = "使用一个 SP回复药（道具 ID 29）的按键。None = 不设键。",
        ["ItemHotkey.SpLarge"] = "使用一个 SP能量饮料（道具 ID 30）的按键。None = 不设键。",
        ["ItemHotkey.Aura"] = "使用一个光环之药（道具 ID 31）的按键。None = 不设键。",
        ["ItemHotkey.Speed"] = "使用一个速度之药（道具 ID 32）的按键。None = 不设键。",

        // [JobMasterSkip] 跳过职业通关动画
        ["JobMasterSkip.Enabled"] = "职业升到满级时弹出的职业通关窗口快速走完。用 ToggleKey 切换，会记住上次的设置。",
        ["JobMasterSkip.ToggleKey"] = "开关「跳过职业通关动画」的按键。",
        ["JobMasterSkip.ExtraUpdates"] = "窗口每帧额外运行的次数（它的动画约 20 步）。",
        ["JobMasterSkip.AutoConfirm"] = "同时替你按确定，让窗口自己关闭（职业通关奖励照常发放）。关闭此项：只加快动画，仍需自己点击关闭。",
        ["JobMasterSkip.Types"] = "要快进的画面（SubForm）类型：80 职业通关窗口。加上 39 会包含「所有职业都已通关」的提示，但其他提示窗口也用这个类型。",
        ["JobMasterSkip.ShowIndicator"] = "快进开启时在右上角显示一个小标签。",

        // [JobSwitch] 转职满级 / 连续转职 / 转职后装回技能
        ["JobSwitch.Enabled"] = "在转职页面显示转职满级按钮（强化满级旁边）。",
        ["JobSwitch.ChainButton"] = "在转职页面显示连续转职按钮（「道具」右边）：反复执行转职满级，直到没有可转的职业，或本轮全部职业都已通关。再按一次可停止。",
        ["JobSwitch.AllowMedalJobs"] = "也选择需要消耗勋章才能转的职业（会像游戏自己转职一样扣掉勋章）。关闭时跳过这些职业。",
        ["JobSwitch.RestoreSkill"] = "每次转职后（游戏自己的转职或转职满级），装回角色转入某职业时被游戏卸下的那个职业技能。游戏只在新职业的技能还没学会时才装回，所以反复通关职业时从不装回。使用游戏存档里本来就有的字段，Mod 不另存任何数据。",

        // [KnowLog] 知识获取记录
        ["KnowLog.Enabled"] = "记录战斗队伍中的船员获得的知识及其来源（设施、战斗），供 F8 面板查看。",
        ["KnowLog.PanelKey"] = "显示或隐藏知识获取面板的按键。",

        // [MoneyLog] 资金流水
        ["MoneyLog.Enabled"] = "记录每一笔资金变动及其来源（商店、锻造、战斗、访客…），供 F7 面板查看。",
        ["MoneyLog.PanelKey"] = "显示或隐藏资金流水面板的按键。",

        // [PartyNoHarvest] 战斗队伍成员不去收获设施
        ["PartyNoHarvest.Enabled"] = "编入战斗队伍的船员不去收获设施（农田、植树厂、牧场 鸡/猪/牛）。不在队伍里的船员不受影响。修改后需重启游戏。",

        // [SmithFeed] 投入所有相性相符
        ["SmithFeed.Enabled"] = "在锻造的素材页面显示「投入所有相性相符」按钮。",
        ["SmithFeed.Keep"] = "每样装备保留多少个库存（不算正在强化的那一件）。",
        ["SmithFeed.StopAtMaxLevel"] = "最后一次锻造只放刚好够升到满级的素材。",
        ["SmithFeed.Confirm"] = "锻造前弹出确认框，显示投入的装备数量和金币花费。",
        ["SmithFeed.Line1"] = "按钮文字第一行。",
        ["SmithFeed.Line2"] = "按钮文字第二行。",
        ["SmithFeed.ButtonX"] = "按钮中心在素材页面上的 X 坐标（游戏单位）。",
        ["SmithFeed.ButtonY"] = "按钮中心在素材页面上的 Y 坐标（游戏单位）。",
        ["SmithFeed.FontScalePercent"] = "文字大小，占菜单字体的百分比。",
        ["SmithFeed.DiscScalePercent"] = "按钮大小，占游戏圆形图标的百分比。",

        // [Speed] 游戏加速
        ["Speed.ToggleKey"] = "开关游戏加速的按键。",
        ["Speed.Multiplier"] = "加速开启时的游戏速度倍率（需要几秒钟才会稳定）。",
        ["Speed.ShowIndicator"] = "加速开启时显示一个小的倍率标签。",
    };

    private static readonly FieldInfo DescriptionField =
        typeof(ConfigEntryBase).GetField("<Description>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Call after every Bind: prefixes each description with its Chinese text, then rewrites the file.</summary>
    internal static void Apply(ConfigFile cfg)
    {
        if (DescriptionField == null)
        {
            Plugin.L.LogWarning("[config] can't add Chinese descriptions: ConfigEntryBase layout changed");
            return;
        }
        foreach (var entry in cfg.Values)
        {
            var key = $"{entry.Definition.Section}.{entry.Definition.Key}";
            if (!Text.TryGetValue(key, out var zh))
            {
                Plugin.L.LogWarning($"[config] no Chinese description for {key}");
                continue;
            }
            var d = entry.Description;
            var en = d?.Description;
            var text = string.IsNullOrEmpty(en) ? zh : zh + "\n" + en;
            DescriptionField.SetValue(entry, new ConfigDescription(text, d?.AcceptableValues, d?.Tags ?? new object[0]));
        }
        cfg.Save();
    }
}
