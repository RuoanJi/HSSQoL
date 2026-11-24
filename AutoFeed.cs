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
/// Feeds the character up to its current job's max level, one item at a time, applying exactly what the
/// game's own 強化 does for each item (read from the game code: SubForm.ProFriendParamDetail single path,
/// SubForm.InitTeamGetExp/ProTeamGetExp team path, ProJobMaster, CreateAllJobMaster; both paths agree):
///  1. Rolls per item, in order: each stat gain[i] += Lib.GetRand(addParam_[i][0], addParam_[i][1]),
///     then exp += item.GetAddExp().
///  2. Stat cap per stat: cap = GetEffectLimitParam() = (effectLimit_+1)*10, have = GetAddParam(i)
///     (lifetime total incl. job-clear bonuses). cap <= have: gain 0; have + gain >= cap: gain = cap - have.
///     Then AddParam(i, gain) (the game eases it in slices; AddParam is linear, so one call is identical).
///  3. item.Add(-1).
///  4. EXP in the game's 20 eased slices: AddExp(Easing(0,exp,20,f,0) - Easing(0,exp,20,f-1,0)), f = 1..20,
///     negative slices skipped. AddExp only clamps when called AT max, so slicing matters for the final EXP.
///  5. No EXP doubling for food (CheckExSkill(18) doubling only applies to mode 0, not food).
///  6. Job clear when IsJobLvMax(): if !IsMastedJob(): eventList_[103].ExeEvent() (one-time tutorial),
///     AddJobMasterBonus(); then (either case) if jobList_.Length <= masterJob_.Size():
///     masterJob_.RemoveAllElements() (the "all jobs mastered" reset).
/// Feeding one item at a time is what the game does when you feed items one by one.
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
    internal enum FeedMethod { Game, Direct }

    internal static ConfigEntry<int> KeepAtLeast, MaxBites, StatFoodMaxTier;
    internal static ConfigEntry<bool> PrioritizeStats;
    internal static ConfigEntry<string> ExcludeIds;
    internal static ConfigEntry<bool> ButtonConfirm, JobClearWindow;
    internal static ConfigEntry<FeedMethod> Method;

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
        PrioritizeStats = cfg.Bind("AutoFeed", "PrioritizeStats", true,
            "Prefer foods that can still raise a stat (best expected stat gain per EXP) before the most-plentiful rule. " +
            "Only foods that fit the remaining EXP need are considered, so no EXP is wasted.");
        StatFoodMaxTier = cfg.Bind("AutoFeed", "StatFoodMaxTier", 3,
            new ConfigDescription("Highest food tier that may be chosen for stats (0-3). Lower it to save rare high-tier food.",
                new AcceptableValueRange<int>(0, 3)));
        JobClearWindow = cfg.Bind("AutoFeed", "JobClearWindow", true,
            "On reaching max level, open the game's own Job Master window (its job-clear animation), which grants the bonus " +
            "when you close it, exactly as the game does. If false, the bonus is applied silently.");
        ButtonConfirm = cfg.Bind("AutoFeed", "ButtonConfirm", false,
            "If true, the in-menu button needs two clicks (preview, then feed). If false, one click feeds.");
        Method = cfg.Bind("AutoFeed", "Method", FeedMethod.Direct,
            "Direct: instant, applies the game's own feeding rules item by item (EXP, stat gains with caps, job clear). " +
            "Game: opens the game's team power-up window for each planned food instead (one level-up animation per food).");
    }

    internal sealed class Food
    {
        public ItemData Item;
        public string Name;
        public int Id, Have, Min, Max;
        public double Avg;
        public int Tier, Cap;               // effectLimit_ and GetEffectLimitParam() = (tier+1)*10
        public int[] StatMin, StatMax;      // addParam_[i] = [min, max] rolled per item with Lib.GetRand
        public double ExpectedStat(int i) => (StatMin[i] + StatMax[i]) / 2.0;

        /// <summary>Expected total stat gain of one item for a character with these food-bonus totals (game's cap rule).</summary>
        public double ExpectedStatGain(int[] totals)
        {
            double g = 0;
            for (int i = 0; i < StatMin.Length && i < totals.Length; i++)
            {
                if (StatMax[i] <= 0 || ExpectedStat(i) <= 0 || Cap <= totals[i]) continue;
                g += Math.Min(ExpectedStat(i), Cap - totals[i]);
            }
            return g;
        }
    }

    /// <summary>The in-menu button. One click feeds, unless ButtonConfirm is on (then: preview, click again to feed).</summary>
    internal static void ButtonPressed(Human h, Character c, SubForm menu)
    {
        Safe(() =>
        {
            if (c == null) { Show("AutoFeed: couldn't tell which character this menu is for."); return; }
            if (Method.Value == FeedMethod.Game)
            {
                if (h != null) { GameFeed.Start(h, c, menu); return; }
                Log("Method = Game needs the character's Human, which wasn't found; feeding directly instead.");
            }
            if (!ButtonConfirm.Value) { Feed(c, menu, requirePreview: false); return; }
            if (IsConfirmable(c)) Feed(c, menu);
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

    internal static void Safe(Action a)
    {
        try { a(); }
        catch (Exception e) { Show($"AutoFeed error: {e.Message}"); Plugin.L.LogError($"[autofeed] {e}"); }
    }

    /// <summary>Sets the debug panel text (kept until the next message) and mirrors it to the log.</summary>
    internal static void Show(string text)
    {
        _overlay = text;
        if (!text.Contains('\n')) Log(text);
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[autofeed] {s}");

    internal static string Describe(Character c)
    {
        if (c == null) return "null";
        int lv = c.GetLv(), max = c.GetLvMax();
        return $"{c.GetName()} job={c.GetJobName()}(id {c.jobID_}) Lv {lv}/{max} exp_={c.exp_} lvExp(max)={c.GetLvExp(max)} " +
               $"jobMaster={c.CheckJobMaster(c.GetJob())} masterJobs={c.masterJob_?.Size()}";
    }

    // ---------- data ----------

    internal static int Need(Character c) => Math.Max(0, c.GetLvExp(c.GetLvMax()) - c.exp_);

    internal static List<Food> Foods(bool sample)
    {
        var excluded = new HashSet<int>(ExcludeIds.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var v) ? v : -1));
        var list = new List<Food>();
        foreach (var it in main.AppData.GetInstance().itemList_)
        {
            if (it == null || it.type_ != ItemData.TYPE_POWER_UP || excluded.Contains(it.id_)) continue;
            var f = new Food { Item = it, Name = it.GetName(), Id = it.id_, Have = it.GetNum(),
                               Tier = it.effectLimit_, Cap = it.GetEffectLimitParam() };
            var rows = it.addParam_;
            int n = rows?.Length ?? 0;
            f.StatMin = new int[n]; f.StatMax = new int[n];
            for (int i = 0; i < n; i++) { f.StatMin[i] = rows[i][0]; f.StatMax[i] = rows[i][1]; }
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

    /// <summary>
    /// Picks the next food. With stat totals given and PrioritizeStats on: among foods that fit the EXP need,
    /// any that can still raise a stat (expected gain under its tier cap) win, ranked by expected stat gain per EXP,
    /// then most held. (Food gains are 0-1 or 0-2 per item and the EXP need is the real budget, so small foods
    /// give the most stat rolls per job; this ranking also leaves big rare dishes alone.)
    /// Otherwise: most held among foods that fit; if none fit, the smallest-EXP food.
    /// </summary>
    internal static Food Choose(List<Food> foods, int need, int[] statTotals, out bool forStats)
    {
        forStats = false;
        int keep = KeepAtLeast.Value;
        var usable = foods.Where(f => f.Have > keep && f.Avg > 0).ToList();
        if (usable.Count == 0) return null;
        var fits = usable.Where(f => f.Avg <= need).ToList();

        if (statTotals != null && PrioritizeStats.Value)
        {
            var statPick = fits.Where(f => f.Tier <= StatFoodMaxTier.Value && f.ExpectedStatGain(statTotals) > 0)
                .OrderByDescending(f => f.ExpectedStatGain(statTotals) / f.Avg).ThenByDescending(f => f.Have).FirstOrDefault();
            if (statPick != null) { forStats = true; return statPick; }
        }

        var pick = fits.OrderByDescending(f => f.Have).ThenBy(f => f.Avg).FirstOrDefault();
        return pick ?? usable.OrderBy(f => f.Avg).First();
    }

    /// <summary>Simulation helper: adds a food's expected (capped) stat gain to the totals.</summary>
    internal static void AddExpectedStats(Food f, int[] totals)
    {
        for (int i = 0; i < f.StatMin.Length && i < totals.Length; i++)
        {
            if (f.StatMax[i] <= 0 || f.ExpectedStat(i) <= 0 || f.Cap <= totals[i]) continue;
            totals[i] += (int)Math.Round(Math.Min(f.ExpectedStat(i), f.Cap - totals[i]));
        }
    }

    // ---------- preview ----------

    private static void Preview(Character c, string confirmHint = null)
    {
        if (c == null) { Show("AutoFeed: couldn't tell which character this menu is for."); return; }

        int need = Need(c);
        var foods = Foods(sample: true);
        var plan = new Dictionary<int, int>();
        var simStats = FoodStats(c, foods.FirstOrDefault()?.StatMin.Length ?? 0);
        int sim = need, bites = 0;
        while (sim > 0 && bites < MaxBites.Value)
        {
            var f = Choose(foods, sim, simStats, out _);
            if (f == null) break;
            plan[f.Id] = plan.GetValueOrDefault(f.Id) + 1;
            AddExpectedStats(f, simStats);
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


    // ---------- feed (faithful to the game's own feeding, one item at a time) ----------

    private const int EaseFrames = 20;     // the game eases EXP and stat gains over 20 frames
    private const int TutorialEvent = 103; // AppData.eventList_ index run by ProJobMaster

    internal static int[] FoodStats(Character c, int count)
    {
        var s = new int[count];
        for (int i = 0; i < count; i++) s[i] = c.GetAddParam(i);
        return s;
    }

    /// <summary>Feeds one item exactly as the game's 強化 does for a single item. Returns the EXP rolled.</summary>
    private static int FeedOne(Character c, ItemData item, int[] statGain)
    {
        // 1. Rolls, same order as the game: stats, then EXP.
        var rows = item.addParam_;
        int n = rows?.Length ?? 0;
        for (int i = 0; i < n; i++) statGain[i] = ext.util.Lib.GetRand(rows[i][0], rows[i][1]);
        int exp = item.GetAddExp();

        // 2. Stat cap against the character's lifetime bonus, then apply.
        int cap = item.GetEffectLimitParam();
        for (int i = 0; i < n; i++)
        {
            int have = c.GetAddParam(i);
            if (cap <= have) statGain[i] = 0;
            else if (cap - statGain[i] - have <= 0) statGain[i] = cap - have;
            if (statGain[i] > 0) c.AddParam(i, statGain[i]);
        }

        // 3. Use up the item.
        item.Add(-1);

        // 4. EXP in the game's eased slices (AddExp clamps only when called at max).
        int prev = kairo.unity.graph.Graph.Easing(0, exp, EaseFrames, 0, 0);
        for (int f = 1; f <= EaseFrames; f++)
        {
            int cur = kairo.unity.graph.Graph.Easing(0, exp, EaseFrames, f, 0);
            int slice = cur - prev;
            prev = cur;
            if (slice >= 0) c.AddExp(slice);
        }
        return exp;
    }

    /// <summary>Job clear, exactly as ProTeamGetExp/ProFriendParamDetail + ProJobMaster + CreateAllJobMaster do it.</summary>
    /// <summary>
    /// Job clear through the game's own windows, as ProTeamGetExp/ProFriendParamDetail do after a feed:
    /// IsJobLvMax and not mastered → CreateJobMaster (its ProJobMaster grants the bonus on close);
    /// already mastered and all jobs mastered → CreateAllJobMaster (resets the mastered list).
    /// </summary>
    internal static string OpenJobClearWindow(Character c, SubForm menu)
    {
        if (!c.IsJobLvMax()) return "";
        if (!c.IsMastedJob())
        {
            bool ok = menu.Push(SubForm.CreateJobMaster(c));
            Log($"opened the game's Job Master window (Push returned {ok}); it grants the job-clear bonus when closed");
            return " + job clear window";
        }
        var jobs = main.AppData.GetInstance().jobList_;
        if (jobs != null && c.masterJob_ != null && jobs.Length <= c.masterJob_.Size())
        {
            bool ok = menu.Push(SubForm.CreateAllJobMaster(c));
            Log($"opened the game's All Job Master window (Push returned {ok})");
            return " + all jobs mastered window";
        }
        return "";
    }

    private static string JobClear(Character c)
    {
        if (!c.IsJobLvMax()) return "";
        var app = main.AppData.GetInstance();
        string note = "";
        if (!c.IsMastedJob())
        {
            var events = app.eventList_;
            if (events != null && events.Length > TutorialEvent && events[TutorialEvent] != null)
                events[TutorialEvent].ExeEvent();
            c.AddJobMasterBonus();
            note = " + job cleared";
        }
        var jobs = app.jobList_;
        var mastered = c.masterJob_;
        if (jobs != null && mastered != null && jobs.Length <= mastered.Size())
        {
            mastered.RemoveAllElements();
            note += " + all jobs mastered (mastered list reset)";
        }
        return note;
    }

    private static void Feed(Character c, SubForm menu, bool requirePreview = true)
    {
        if (c == null || (requirePreview && !IsConfirmable(c)))
        {
            Show("AutoFeed: click the button again to feed (preview expired or different character).");
            return;
        }
        _previewChar = null;
        if (c.IsJobLvMax() || Need(c) == 0) { Show($"AutoFeed: {c.GetName()} is already at max level for this job."); return; }

        int startLv = c.GetLv(), startExp = c.exp_;
        var foods = Foods(sample: true);
        int statCount = foods.FirstOrDefault()?.Item.addParam_?.Length ?? 0;
        var startStats = FoodStats(c, statCount);
        Log($"FEED START {Describe(c)} need={Need(c)} foodStats=[{string.Join(",", startStats)}]");
        foreach (var f in foods.Where(f => f.Have > 0))
            Log($"  food id={f.Id} '{f.Name}' have={f.Have} exp avg={f.Avg:0.0} ({f.Min}-{f.Max}) tier={f.Tier} cap={f.Cap} " +
                $"stats=[{string.Join(" ", f.StatMin.Select((m, i) => $"{m}-{f.StatMax[i]}"))}]");

        var eaten = new Dictionary<int, (int n, long exp)>();
        var gain = new int[statCount];
        int bites = 0, statBites = 0;
        while (!c.IsJobLvMax() && Need(c) > 0 && bites < MaxBites.Value)
        {
            var f = Choose(foods, Need(c), FoodStats(c, statCount), out bool forStats);
            if (f == null) { Log("no usable food left"); break; }
            if (forStats) statBites++;

            int haveBefore = f.Item.GetNum();
            if (haveBefore <= KeepAtLeast.Value) { f.Have = haveBefore; continue; }
            int expBefore = c.exp_, lvBefore = c.GetLv();
            int roll = FeedOne(c, f.Item, gain);
            f.Have = f.Item.GetNum();
            bites++;

            var e = eaten.GetValueOrDefault(f.Id);
            eaten[f.Id] = (e.n + 1, e.exp + (c.exp_ - expBefore));
            Log($"bite {bites} ({(forStats ? "stats" : "exp")}): id={f.Id} '{f.Name}' exp roll={roll} exp {expBefore}->{c.exp_} Lv {lvBefore}->{c.GetLv()} " +
                $"stat gain=[{string.Join(",", gain)}] have {haveBefore}->{f.Have} need={Need(c)}");
        }

        string clear = menu != null && JobClearWindow.Value ? OpenJobClearWindow(c, menu) : JobClear(c);
        bool atMax = c.IsJobLvMax();
        var endStats = FoodStats(c, statCount);
        string delta = string.Join(",", endStats.Select((v, i) => $"{v - startStats[i]:+0;-0;0}"));

        var sb = new StringBuilder();
        sb.AppendLine($"AutoFeed done: {c.GetName()}  Lv {startLv} -> {c.GetLv()}/{c.GetLvMax()}{clear}");
        sb.AppendLine($"EXP {startExp:N0} -> {c.exp_:N0}   ({bites} bites)   stat bonus change [{delta}]");
        foreach (var f in foods.Where(f => eaten.ContainsKey(f.Id)).OrderByDescending(f => eaten[f.Id].n))
            sb.AppendLine($"  {f.Name,-10} x{eaten[f.Id].n,4}   +{eaten[f.Id].exp:N0} exp   left {f.Have}");
        if (!atMax) sb.AppendLine($"Stopped {Need(c):N0} EXP short (out of food or bite cap).");
        Show(sb.ToString().TrimEnd());

        Log($"FEED END {Describe(c)} bites={bites} (for stats {statBites}) atMax={atMax}{clear} foodStats=[{string.Join(",", endStats)}] delta=[{delta}]");
        foreach (var kv in eaten) Log($"  consumed id={kv.Key} x{kv.Value.n} exp={kv.Value.exp}");
    }
}
