using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using form;
using HarmonyLib;
using kairo.unity.form;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Money flow log and statistics (资金流水, toggle the panel with F7).
///
/// Every change to the player's money goes through Player.AddMoney(int type, long val) (ISIL: ExLong.Add on
/// money_ +0x38, then the money high score and moneyTable_[type][sign] += |val|). type is coarse
/// (MONEY_TYPE_GUEST 0, RUNNING 1, OTHER 2, and nearly every caller passes 2), so the source is taken from the
/// caller instead: each of the 26 methods that call AddMoney (found in the ISIL dump) gets a prefix/postfix that
/// pushes its name on a small stack, and AddMoney's postfix records the innermost one. Money that changes without
/// an AddMoney call (shouldn't happen, but it's checked every frame) is logged as "未归类".
///
/// Everything is kept in memory only (nothing is written to disk): the F7 panel totals income and spending per
/// source since launch (or since 重置), and lists the latest entries.
/// </summary>
internal static class MoneyLog
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyCode> PanelKey;

    /// <summary>Game method that calls AddMoney → readable source name. Names are best guesses from the method.</summary>
    private static readonly (string type, string method, string label)[] Callers =
    {
        ("form.SubForm", "ProWharfShopItem", "港口商店·道具装备"),
        ("form.SubForm", "ProWharfShopEgg", "港口商店·蛋"),
        ("form.SubForm", "ProWharfShopSelect", "港口商店·选择"),
        ("form.SubForm", "ProWharfSmith", "铁匠"),
        ("form.SubForm", "ProBuildWharfBlock", "港口建设"),
        ("form.SubForm", "ProEggList", "蛋列表"),
        ("form.SubForm", "ProGetEvent", "事件奖励"),
        ("form.SubForm", "ProNetBattleGetCrew", "网络对战·船员"),
        ("form.SubForm", "ProPrizonCrewList", "囚犯"),
        ("form.SubForm", "ProRoyalFamily", "王族"),
        ("form.SubForm", "ProSelectCannon", "大炮"),
        ("form.SubForm", "ProTreasureList", "宝物"),
        ("form.SubForm", "SugorkuScript", "双六"),
        ("system.form.BillingForm", "ProExShop", "特别商店"),
        ("system.form.BillingForm", "ProMedalShop", "奖牌商店"),
        ("form.GameForm", "Key", "船上操作"),
        ("game.FighterBase", "AddEffectAddMoney", "战斗"),
        ("game.FighterBase", "ColDamage", "战斗"),
        ("game.FighterBase", "HitScript", "战斗"),
        ("game.Human", "Script", "人物行动"),
        ("game.Human", "VisitAction", "设施使用"),
        ("game.Merchant", "Script", "商人"),
        ("game.NetBattleManager", "AddBonus", "网络对战奖励"),
        ("game.Player", "ProReserveCasket", "宝箱"),
        ("game.Player", "UpdateAnalyzeTelop", "解析"),
    };

    private static readonly Dictionary<MethodBase, (string label, string method)> _labels = new();
    private record Frame(string Label, string Method, int Screen, string Detail, long UseCost);
    private static readonly List<Frame> _stack = new();

    /// <summary>Facility model id → name (from docs/data/facilities.json); the game's own name_ is the fallback.</summary>
    private static readonly Dictionary<int, string> FacilityNames = new()
    {
        [0] = "地板", [1] = "楼梯", [2] = "柱子", [3] = "拼块地板", [4] = "寝室", [5] = "寝室", [6] = "医务室", [7] = "寝室", [8] = "寝室", [9] = "寝室", [10] = "寝室", [11] = "寝室", [12] = "仓库", [13] = "巨大仓库", [14] = "船员食堂", [15] = "解析室", [16] = "未使用", [17] = "格斗场", [18] = "游泳池", [19] = "舞厅", [20] = "厨房", [21] = "射击训练场", [22] = "特训场", [23] = "祈祷处", [24] = "宝物资料馆", [25] = "情报信息馆", [26] = "澡堂", [27] = "图书馆", [28] = "探测室", [29] = "职务室", [30] = "画室", [31] = "通信室", [32] = "弹药库", [33] = "饮料吧", [34] = "钓鱼竿保存处", [35] = "谈话室", [36] = "厕所", [37] = "训练设施", [38] = "宠物小屋", [39] = "农田", [40] = "植树厂", [41] = "牧场(鸡)", [42] = "牧场(猪)", [43] = "牧场(牛)", [44] = "电灯", [45] = "椅子", [46] = "观叶植物", [47] = "观叶树木", [48] = "传说之剑", [49] = "守护之铠", [50] = "女神像", [51] = "伟大的椅子", [52] = "绘画角", [53] = "水瓮", [54] = "指南针", [55] = "水槽", [56] = "幸运之鸟", [57] = "牢房", [58] = "红色绒毯", [77] = "桌游室", [78] = "投标盘", [79] = "花",
    };

    internal record Entry(DateTime Time, string Source, string Method, int Type, long Delta, long Balance, int Screen, string Detail = "");

    private class Agg { public long In, Out; public int Count; }

    // Fixed-size state: running totals per source (a few dozen at most) plus the latest entries for the list.
    // Nothing grows with play time.
    private const int RecentMax = 300;
    private static readonly Dictionary<string, Agg> _totals = new();
    private static readonly Queue<Entry> _recent = new();
    private static Entry[] _recentView = Array.Empty<Entry>();   // newest first, rebuilt only when something changes
    private static bool _recentDirty;
    private static long _in, _out;
    private static DateTime _sessionStart = DateTime.Now;
    private static long _sessionStartMoney = long.MinValue;
    private static long _last = long.MinValue;                 // last known money of AppData.player_
    private static IntPtr _lastPlayer;
    private static long _before;
    private static bool _show;
    private static Vector2 _scroll, _tableScroll;
    private static readonly HashSet<string> _expanded = new() { "设施使用" };

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("MoneyLog", "Enabled", true,
            "Records every change to your money with its source (shop, smith, battle, visitors…) for the F7 panel.");
        PanelKey = cfg.Bind("MoneyLog", "PanelKey", KeyCode.F7, "Shows or hides the money flow panel.");
    }

    /// <summary>Called from Plugin.PatchLate (SubForm's static constructor needs a loaded save).</summary>
    internal static void Patch(Harmony h)
    {
        if (!Enabled.Value) return;
        var add = AccessTools.Method(typeof(game.Player), nameof(game.Player.AddMoney));
        h.Patch(add, prefix: new HarmonyMethod(typeof(MoneyLog), nameof(AddPrefix)),
            postfix: new HarmonyMethod(typeof(MoneyLog), nameof(AddPostfix)));
        var push = new HarmonyMethod(typeof(MoneyLog), nameof(CallerPrefix));
        var pop = new HarmonyMethod(typeof(MoneyLog), nameof(CallerPostfix));
        foreach (var (typeName, method, label) in Callers)
        {
            var t = AccessTools.TypeByName(typeName);
            var ms = t == null ? new List<MethodInfo>() : AccessTools.GetDeclaredMethods(t).Where(m => m.Name == method).ToList();
            if (ms.Count == 0) { Plugin.L.LogWarning($"[money] caller {typeName}.{method} not found; its changes will show as \"其他\""); continue; }
            foreach (var m in ms)
            {
                _labels[m] = (label, $"{t.Name}.{method}");
                try { h.Patch(m, prefix: push, postfix: pop); }
                catch (Exception e) { Plugin.L.LogError($"[money] patching {typeName}.{method} failed: {e.Message}"); }
            }
        }
    }

    private static void CallerPrefix(MethodBase __originalMethod, object __instance)
    {
        if (!_labels.TryGetValue(__originalMethod, out var l)) return;
        int screen = __instance is SubForm sf ? sf.type_ : -1;
        string label = l.label, detail = "";
        long cost = long.MinValue;
        if (__instance is game.Human hu && l.method == "Human.VisitAction") (label, detail, cost) = Visit(hu);
        _stack.Add(new Frame(label, l.method, screen, detail, cost));
    }

    /// <summary>
    /// Human.VisitAction: someone using a ship facility. The fee is ShipInChipModel.GetUseCost() of the visited
    /// model (visitModelID_), paid only if you can afford it. A second AddMoney in the same method is behind a
    /// favourite-facility + ex-skill 33 check; amounts that don't match the fee are marked 其他.
    /// </summary>
    private static (string label, string detail, long cost) Visit(game.Human h)
    {
        string fac = "?", who = "";
        long cost = long.MinValue;
        try
        {
            int id = h.visitModelID_;
            var models = main.AppData.GetInstance()?.shipInChipModel_;
            // The model table is indexed by model id (index == id for all 80 entries in the data dump).
            var model = models != null && id >= 0 && id < models.Length ? models[id] : null;
            if (!FacilityNames.TryGetValue(id, out fac)) fac = model?.name_ ?? $"#{id}";
            if (model != null) cost = model.GetUseCost();
        }
        catch { }
        try { who = h.crew_?.GetName() ?? ""; } catch { }
        return ($"设施使用·{fac}", who, cost);
    }

    private static void CallerPostfix()
    {
        if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1);
    }

    private static void AddPrefix(game.Player __instance)
    {
        try { _before = __instance.GetMoney(); } catch { _before = long.MinValue; }
    }

    private static void AddPostfix(game.Player __instance, int type, long val)
    {
        try
        {
            long after = __instance.GetMoney();
            long delta = _before == long.MinValue ? val : after - _before;
            bool main = IsMainPlayer(__instance);
            if (main) _last = after;
            if (delta == 0) return;
            string label = "其他", method = "?", detail = "";
            int screen = -1;
            if (_stack.Count > 0)
            {
                var f = _stack[^1];
                (label, method, screen, detail) = (f.Label, f.Method, f.Screen, f.Detail);
                if (f.UseCost != long.MinValue && delta != -f.UseCost) detail = (detail + " 其他").Trim();
            }
            if (screen < 0) screen = TopScreen();
            if (!main) label += " (非本存档)";
            Record(new Entry(DateTime.Now, label, method, type, delta, after, screen, detail));
        }
        catch (Exception e) { Plugin.L.LogError($"[money] {e.Message}"); }
    }

    private static bool IsMainPlayer(game.Player p)
    {
        var mp = main.AppData.GetInstance()?.player_;
        return mp != null && mp.Pointer == p.Pointer;
    }

    private static int TopScreen()
    {
        try
        {
            var top = FormManagerBase.GetInstance()?.Top;
            var sf = top?.TryCast<SubForm>();
            return sf != null ? sf.type_ : top?.TryCast<GameForm>() != null ? -2 : -1;
        }
        catch { return -1; }
    }

    /// <summary>Every frame: catch money changes made outside AddMoney, and notice save loads.</summary>
    internal static void Tick()
    {
        if (Input.GetKeyDown(PanelKey.Value)) _show = !_show;
        _stack.Clear(); // no caller is mid-call between frames; clears anything left by an exception
        if (!Enabled.Value || !Plugin.LateReady) return;
        var p = main.AppData.GetInstance()?.player_;
        if (p == null) return;
        long now;
        try { now = p.GetMoney(); } catch { return; }
        if (p.Pointer != _lastPlayer)
        {
            // New save loaded (or first frame): start from here instead of logging the jump.
            bool first = _lastPlayer == IntPtr.Zero;
            _lastPlayer = p.Pointer;
            _last = now;
            if (_sessionStartMoney == long.MinValue || !first) { _sessionStartMoney = now; }
            if (!first) Plugin.L.LogInfo($"[money] save changed, balance {now:N0}");
            return;
        }
        if (now != _last)
        {
            Record(new Entry(DateTime.Now, "未归类", "?", -1, now - _last, now, TopScreen()));
            _last = now;
        }
    }

    private static void Record(Entry e)
    {
        if (!_totals.TryGetValue(e.Source, out var a)) _totals[e.Source] = a = new Agg();
        if (e.Delta > 0) { a.In += e.Delta; _in += e.Delta; } else { a.Out -= e.Delta; _out -= e.Delta; }
        a.Count++;
        _recent.Enqueue(e);
        while (_recent.Count > RecentMax) _recent.Dequeue();
        _recentDirty = true;
    }

    // ---------------------------------------------------------------- panel

    private static GUIStyle _box, _head, _cell, _num, _pos, _neg;
    private static Texture2D _bg;

    private static void Styles()
    {
        if (_box != null) return;
        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.12f, 0.92f));
        _bg.Apply();
        _box = new GUIStyle(GUI.skin.box);
        _box.normal.background = _bg;
        _head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        _head.normal.textColor = Color.white;
        _cell = new GUIStyle(GUI.skin.label) { fontSize = 14, clipping = TextClipping.Clip };
        _cell.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        _num = new GUIStyle(_cell) { alignment = TextAnchor.UpperRight };
        _pos = new GUIStyle(_num);
        _pos.normal.textColor = new Color(0.45f, 0.9f, 0.5f);
        _neg = new GUIStyle(_num);
        _neg.normal.textColor = new Color(1f, 0.5f, 0.45f);
    }

    internal static void Draw()
    {
        if (!_show) return;
        Styles();
        float w = Math.Min(760, Screen.width - 20), h = Math.Min(640, Screen.height - 20);
        var r = new Rect(Screen.width - w - 10, 10, w, h);
        GUI.Box(r, GUIContent.none, _box);
        float x = r.x + 12, y = r.y + 8, iw = w - 24;

        DateTime from = _sessionStart;
        GUI.Label(new Rect(x, y, iw - 260, 24), $"资金流水 ({PanelKey.Value} 关闭)", _head);
        if (GUI.Button(new Rect(x + iw - 80, y, 80, 24), "重置")) ResetSession();
        y += 30;

        long inc = _in, outg = _out;
        double hours = Math.Max((DateTime.Now - from).TotalHours, 1.0 / 60);
        long bal = _last == long.MinValue ? 0 : _last;
        string start = _sessionStartMoney != long.MinValue ? $"{_sessionStartMoney:N0} → " : "";
        GUI.Label(new Rect(x, y, iw, 22), $"余额 {start}{bal:N0}    自 {from:MM-dd HH:mm}，{FormatSpan(DateTime.Now - from)}", _cell);
        y += 22;
        GUI.Label(new Rect(x, y, 160, 22), "收入", _cell);
        GUI.Label(new Rect(x + 40, y, 140, 22), $"+{inc:N0}", _pos);
        GUI.Label(new Rect(x + 200, y, 60, 22), "支出", _cell);
        GUI.Label(new Rect(x + 240, y, 140, 22), $"-{outg:N0}", _neg);
        GUI.Label(new Rect(x + 400, y, 60, 22), "净额", _cell);
        long net = inc - outg;
        GUI.Label(new Rect(x + 440, y, 140, 22), Signed(net), net >= 0 ? _pos : _neg);
        GUI.Label(new Rect(x + 590, y, iw - 590, 22), $"{Signed((long)(net / hours))}/小时", net >= 0 ? _pos : _neg);
        y += 28;

        // Per-source table, biggest net effect first. "组·项" sources fold under their group (click to expand).
        var by = _totals;
        var groups = by.GroupBy(kv => Group(kv.Key))
            .Select(g => (name: g.Key, items: g.OrderBy(kv => kv.Value.In - kv.Value.Out).ToList(),
                total: new Agg { In = g.Sum(kv => kv.Value.In), Out = g.Sum(kv => kv.Value.Out), Count = g.Sum(kv => kv.Value.Count) }))
            .OrderBy(g => g.total.In - g.total.Out).ToList();
        float c2 = 200, c3 = 330, c4 = 460, c5 = 590, c6 = iw - 80;
        GUI.Label(new Rect(x, y, 190, 22), "来源", _head);
        GUI.Label(new Rect(x + c2, y, 120, 22), "收入", _num);
        GUI.Label(new Rect(x + c3, y, 120, 22), "支出", _num);
        GUI.Label(new Rect(x + c4, y, 120, 22), "净额", _num);
        GUI.Label(new Rect(x + c5, y, 80, 22), "占支出", _num);
        GUI.Label(new Rect(x + c6, y, 60, 22), "次数", _num);
        y += 22;
        float tableBottom = r.y + h * 0.6f;
        int lines = groups.Sum(g => 1 + (g.items.Count > 1 || g.items[0].Key != g.name ? (_expanded.Contains(g.name) ? g.items.Count : 0) : 0));
        var tview = new Rect(x, y, iw, tableBottom - y);
        _tableScroll = GUI.BeginScrollView(tview, _tableScroll, new Rect(0, 0, iw - 20, Math.Max(lines, 1) * 20));
        float ty = 0;
        foreach (var g in groups)
        {
            bool folds = g.items.Count > 1 || g.items[0].Key != g.name;
            bool open = _expanded.Contains(g.name);
            string title = folds ? $"{(open ? "▼" : "▶")} {g.name} ({g.items.Count})" : g.name;
            if (folds && GUI.Button(new Rect(0, ty, 190, 20), GUIContent.none, GUIStyle.none))
            { if (open) _expanded.Remove(g.name); else _expanded.Add(g.name); }
            Row(ty, title, g.total, outg, _cell, c2, c3, c4, c5, c6);
            ty += 20;
            if (!folds || !open) continue;
            foreach (var (name, a) in g.items)
            {
                Row(ty, "　　" + Item(name), a, outg, _cell, c2, c3, c4, c5, c6);
                ty += 20;
            }
        }
        if (by.Count == 0) GUI.Label(new Rect(0, 0, iw, 20), "还没有记录。", _cell);
        GUI.EndScrollView();
        y = tableBottom;

        // Latest entries.
        y += 6;
        GUI.Label(new Rect(x, y, iw, 22), $"最近 {RecentMax} 条", _head);
        y += 24;
        var view = new Rect(x, y, iw, r.yMax - y - 8);
        if (_recentDirty) { _recentView = _recent.Reverse().ToArray(); _recentDirty = false; }
        int rows = _recentView.Length;
        _scroll = GUI.BeginScrollView(view, _scroll, new Rect(0, 0, iw - 20, rows * 20));
        for (int i = 0; i < rows; i++)
        {
            var e = _recentView[i];
            float ry = i * 20;
            GUI.Label(new Rect(0, ry, 80, 20), e.Time.ToString("HH:mm:ss"), _cell);
            GUI.Label(new Rect(80, ry, 170, 20), e.Source, _cell);
            GUI.Label(new Rect(250, ry, 210, 20), e.Detail != "" ? e.Detail : $"{e.Method}{(e.Screen >= 0 ? $" [{e.Screen}]" : "")}", _cell);
            GUI.Label(new Rect(460, ry, 120, 20), Signed(e.Delta), e.Delta >= 0 ? _pos : _neg);
            GUI.Label(new Rect(580, ry, iw - 600, 20), $"{e.Balance:N0}", _num);
        }
        GUI.EndScrollView();
    }

    private static string Group(string source) { int i = source.IndexOf('·'); return i < 0 ? source : source[..i]; }
    private static string Item(string source) { int i = source.IndexOf('·'); return i < 0 ? source : source[(i + 1)..]; }

    private static void Row(float y, string name, Agg a, long outg, GUIStyle st, float c2, float c3, float c4, float c5, float c6)
    {
        long n = a.In - a.Out;
        GUI.Label(new Rect(0, y, 195, 20), name, st);
        GUI.Label(new Rect(c2, y, 120, 20), a.In > 0 ? $"+{a.In:N0}" : "", _pos);
        GUI.Label(new Rect(c3, y, 120, 20), a.Out > 0 ? $"-{a.Out:N0}" : "", _neg);
        GUI.Label(new Rect(c4, y, 120, 20), Signed(n), n >= 0 ? _pos : _neg);
        GUI.Label(new Rect(c5, y, 80, 20), outg > 0 && a.Out > 0 ? $"{100.0 * a.Out / outg:0.#}%" : "", _num);
        GUI.Label(new Rect(c6, y, 60, 20), a.Count.ToString(), _num);
    }

    private static void ResetSession()
    {
        _totals.Clear();
        _recent.Clear();
        _recentDirty = true;
        _in = _out = 0;
        _sessionStart = DateTime.Now;
        _sessionStartMoney = _last;
    }

    private static string Signed(long v) => v > 0 ? $"+{v:N0}" : v < 0 ? $"-{-v:N0}" : "0";

    private static string FormatSpan(TimeSpan s) =>
        s.TotalHours >= 1 ? $"{(int)s.TotalHours} 小时 {s.Minutes} 分" : $"{Math.Max(s.Minutes, 0)} 分 {s.Seconds} 秒";
}
