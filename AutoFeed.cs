using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using data;
using form;
using game;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Feeds the character open in 強化 up to its current job's max level.
///
/// Game facts this relies on (from the runtime probe):
///  - Food = ItemData with type_ == TYPE_POWER_UP. Count: GetNum(); consume: Add(-n).
///  - ItemData.GetAddExp() returns a fresh random roll each call (e.g. 78..88 for 新鲜蔬菜).
///  - Character.AddExp(exp) adds EXP; the game clamps exp_ at GetLvExp(GetLvMax()).
///  - After a feed that reaches max, the 強化 flow calls Character.AddJobMasterBonus().
///
/// Algorithm ("abundance first, no waste"), re-evaluated after every bite:
///  1. need = GetLvExp(max) - exp_. Stop when need &lt;= 0.
///  2. Usable foods: type POWER_UP, not excluded, have &gt; KeepAtLeast.
///  3. Among foods whose average EXP fits in the remaining need, eat the one we hold the
///     most of (ties: smaller EXP first). Eating lowers its count, so stocks even out.
///  4. If no food fits, eat the smallest-EXP food that covers the need: overshoot is
///     at most one small bite.
/// </summary>
internal static class AutoFeed
{
    internal static ConfigEntry<int> KeepAtLeast, MaxBites;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> CallJobMasterBonus, ButtonConfirm;

    private const int Samples = 40;
    private const float ConfirmWindow = 30f;

    private static string _overlay = "";
    private static Character _previewChar;
    private static float _previewTime;

    internal static void Bind(ConfigFile cfg)
    {
        KeepAtLeast = cfg.Bind("AutoFeed", "KeepAtLeast", 0, "Never take a food below this count.");
        ExcludeIds = cfg.Bind("AutoFeed", "ExcludeIds", "", "Comma-separated food ids never used, e.g. 22,23. Ids are listed in the log at the start of every feed.");
        MaxBites = cfg.Bind("AutoFeed", "MaxBites", 3000, "Safety cap on bites per feed.");
        CallJobMasterBonus = cfg.Bind("AutoFeed", "CallJobMasterBonus", true,
            "After reaching max level, call the game's AddJobMasterBonus() like the 強化 menu does.");
        ButtonConfirm = cfg.Bind("AutoFeed", "ButtonConfirm", false,
            "If true, the in-menu button needs two clicks (preview, then feed). If false, one click feeds.");
    }

    private sealed class Food
    {
        public ItemData Item;
        public string Name;
        public int Id, Have, Min, Max;
        public double Avg;
    }

    /// <summary>The in-menu button. One click feeds, unless ButtonConfirm is on (then: preview, click again to feed).</summary>
    internal static void ButtonPressed(Character c, bool forceConfirm = false)
    {
        Safe(() =>
        {
            if (c == null) { Show("AutoFeed: couldn't tell which character this menu is for."); return; }
            if (!ButtonConfirm.Value && !forceConfirm) { Feed(c, requirePreview: false); return; }
            if (IsConfirmable(c)) Feed(c);
            else Preview(c, $"Click the button again within {ConfirmWindow:0}s to feed.");
        });
    }

    private static bool IsConfirmable(Character c) =>
        _previewChar != null && c.Pointer == _previewChar.Pointer && Time.unscaledTime - _previewTime <= ConfirmWindow;

    /// <summary>Debug panel with the last preview/result. Only drawn while F1 debug mode is on.</summary>
    internal static void Draw()
    {
        if (!Plugin.DebugOverlay || _overlay.Length == 0) return;
        var style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 14, wordWrap = false };
        var size = style.CalcSize(new GUIContent(_overlay));
        float x = Screen.width - size.x - 20;
        GUI.Box(new Rect(x, 60, size.x + 10, size.y + 10), _overlay, style);
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Show($"AutoFeed error: {e.Message}"); Plugin.L.LogError($"[autofeed] {e}"); }
    }

    /// <summary>Sets the debug panel text (kept until the next message) and mirrors it to the log.</summary>
    private static void Show(string text)
    {
        _overlay = text;
        if (!text.Contains('\n')) Log(text);
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[autofeed] {s}");

    private static string Describe(Character c)
    {
        if (c == null) return "null";
        int lv = c.GetLv(), max = c.GetLvMax();
        return $"{c.GetName()} job={c.GetJobName()}(id {c.jobID_}) Lv {lv}/{max} exp_={c.exp_} lvExp(max)={c.GetLvExp(max)} " +
               $"jobMaster={c.CheckJobMaster(c.GetJob())} masterJobs={c.masterJob_?.Size()}";
    }

    // ---------- data ----------

    private static int Need(Character c) => Math.Max(0, c.GetLvExp(c.GetLvMax()) - c.exp_);

    private static List<Food> Foods(bool sample)
    {
        var excluded = new HashSet<int>(ExcludeIds.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1));
        var list = new List<Food>();
        foreach (var it in main.AppData.GetInstance().itemList_)
        {
            if (it == null || it.type_ != ItemData.TYPE_POWER_UP || excluded.Contains(it.id_)) continue;
            var f = new Food { Item = it, Name = it.GetName(), Id = it.id_, Have = it.GetNum() };
            if (sample)
            {
                int min = int.MaxValue, max = 0; long sum = 0;
                for (int i = 0; i < Samples; i++) { int r = it.GetAddExp(); sum += r; min = Math.Min(min, r); max = Math.Max(max, r); }
                f.Avg = (double)sum / Samples; f.Min = min; f.Max = max;
            }
            list.Add(f);
        }
        return list;
    }

    private static Food Choose(List<Food> foods, int need)
    {
        int keep = KeepAtLeast.Value;
        var usable = foods.Where(f => f.Have > keep && f.Avg > 0).ToList();
        if (usable.Count == 0) return null;
        var fits = usable.Where(f => f.Avg <= need);
        var pick = fits.OrderByDescending(f => f.Have).ThenBy(f => f.Avg).FirstOrDefault();
        return pick ?? usable.OrderBy(f => f.Avg).First();
    }

    // ---------- preview ----------

    private static void Preview(Character c, string confirmHint = null)
    {
        if (c == null) { Show("AutoFeed: couldn't tell which character this menu is for."); return; }

        int need = Need(c);
        var foods = Foods(sample: true);
        var plan = new Dictionary<int, int>();
        int sim = need, bites = 0;
        while (sim > 0 && bites < MaxBites.Value)
        {
            var f = Choose(foods, sim);
            if (f == null) break;
            plan[f.Id] = plan.GetValueOrDefault(f.Id) + 1;
            f.Have--; sim -= (int)Math.Round(f.Avg); bites++;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"AutoFeed preview: {c.GetName()}  [{c.GetJobName()}]");
        sb.AppendLine($"Lv {c.GetLv()}/{c.GetLvMax()}   EXP {c.exp_:N0} / {c.GetLvExp(c.GetLvMax()):N0}   need {need:N0}");
        if (need == 0) sb.AppendLine("Already at max level for this job.");
        foreach (var f in foods.Where(f => plan.ContainsKey(f.Id)).OrderByDescending(f => plan[f.Id]))
            sb.AppendLine($"  {f.Name,-10} x{plan[f.Id],4}   ~{f.Avg:0} exp ({f.Min}-{f.Max})   have {f.Have + plan[f.Id]}");
        if (sim > 0 && need > 0) sb.AppendLine($"Not enough food: ~{sim:N0} EXP short.");
        else if (need > 0) sb.AppendLine($"~{bites} bites, est. overshoot {-sim:N0} EXP");
        if (need > 0 && confirmHint != null) sb.AppendLine(confirmHint);
        Show(sb.ToString().TrimEnd());

        Log($"PREVIEW {Describe(c)}");
        foreach (var f in foods) Log($"  food id={f.Id} '{f.Name}' have={f.Have + plan.GetValueOrDefault(f.Id)} exp avg={f.Avg:0.0} min={f.Min} max={f.Max} plan={plan.GetValueOrDefault(f.Id)}");

        _previewChar = need > 0 ? c : null;
        _previewTime = Time.unscaledTime;
    }

    // ---------- feed ----------

    private static void Feed(Character c, bool requirePreview = true)
    {
        if (c == null || (requirePreview && !IsConfirmable(c)))
        {
            Show("AutoFeed: click the button again to feed (preview expired or different character).");
            return;
        }
        _previewChar = null;
        if (Need(c) == 0) { Show($"AutoFeed: {c.GetName()} is already at max level for this job."); return; }

        var job = c.GetJob();
        int startLv = c.GetLv(), startExp = c.exp_, startNeed = Need(c);
        bool masteredBefore = c.CheckJobMaster(job);
        Log($"FEED START {Describe(c)} need={startNeed}");

        var foods = Foods(sample: true);
        foreach (var f in foods.Where(f => f.Have > 0))
            Log($"  food id={f.Id} '{f.Name}' have={f.Have} exp avg={f.Avg:0.0} ({f.Min}-{f.Max})");
        var eaten = new Dictionary<int, (int n, long exp)>();
        int bites = 0;
        while (Need(c) > 0 && bites < MaxBites.Value)
        {
            var f = Choose(foods, Need(c));
            if (f == null) { Log("no usable food left"); break; }

            int haveBefore = f.Item.GetNum();
            if (haveBefore <= KeepAtLeast.Value) { f.Have = haveBefore; continue; }
            int expBefore = c.exp_, lvBefore = c.GetLv();
            int roll = f.Item.GetAddExp();
            int lvUp = c.AddExp(roll);
            f.Item.Add(-1);
            f.Have = f.Item.GetNum();
            bites++;

            var e = eaten.GetValueOrDefault(f.Id);
            eaten[f.Id] = (e.n + 1, e.exp + (c.exp_ - expBefore));
            Log($"bite {bites}: id={f.Id} '{f.Name}' roll={roll} exp {expBefore}->{c.exp_} Lv {lvBefore}->{c.GetLv()} (+{lvUp}) have {haveBefore}->{f.Have} need={Need(c)}");
        }

        bool atMax = Need(c) == 0;
        string bonusNote = "";
        if (atMax && CallJobMasterBonus.Value && !masteredBefore)
        {
            c.AddJobMasterBonus();
            bonusNote = " + job master bonus";
            Log($"AddJobMasterBonus() called. CheckJobMaster now={c.CheckJobMaster(job)} masterJob_.Size={c.masterJob_?.Size()}");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"AutoFeed done: {c.GetName()}  Lv {startLv} -> {c.GetLv()}/{c.GetLvMax()}{bonusNote}");
        sb.AppendLine($"EXP {startExp:N0} -> {c.exp_:N0}   ({bites} bites)");
        foreach (var f in foods.Where(f => eaten.ContainsKey(f.Id)).OrderByDescending(f => eaten[f.Id].n))
            sb.AppendLine($"  {f.Name,-10} x{eaten[f.Id].n,4}   +{eaten[f.Id].exp:N0} exp   left {f.Have}");
        if (!atMax) sb.AppendLine($"Stopped {Need(c):N0} EXP short (out of food or bite cap).");
        Show(sb.ToString().TrimEnd());

        Log($"FEED END {Describe(c)} bites={bites} atMax={atMax}");
        foreach (var kv in eaten) Log($"  consumed id={kv.Key} x{kv.Value.n} exp={kv.Value.exp}");
    }
}
