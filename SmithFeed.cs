using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using data;
using form;
using HarmonyLib;
using Graphics = kairo.unity.ui.Graphics;

namespace HSSQoL;

/// <summary>
/// Smith (锻造) material page: one button that reforges the base item with every compatible piece
/// (the 150% match: same type_ and weight_), batch after batch, exactly as repeated manual 合成 would.
///
/// From the ISIL of SubForm.ProWharfSmith (smith = SubForm type 53, TYPE_WHARF_SMITH):
///   processStep_ 2 = the material page; mainEquip_ (+0x2A0) = the base. A material is any EquipData with
///   GetNum() > 0 (one fewer for the base itself). At most GetSmithMaxNum() = Math.Min(wharfShopLv_[8] / 2 + 1, 9)
///   materials per 合成.
///   合成: cost = Σ GetSmithCost() over the materials; if GetMoney() &lt; cost nothing happens (message); else
///   AddMoney(2, -cost), each material Add(-1), then processStep_ 3 plays one material at a time:
///     frames f = 1..10: exp = GetSmithExp(m) + GetSmithExp(m) * wharfShopLv_[8] / 100 (re-read every frame),
///     d = Graph.Easing(0, exp, 10, f, 0) - Graph.Easing(0, exp, 10, f - 1, 0); if d > 0: base.AddExp(d);
///     then Player.AddSmithCountExp() and the next material.
///   When all are done: eventList_[146].ExeEvent(); r = SmithMainEquip(page_); r &lt; 2 → processStep_ 0; else
///   (cycling page_ while ary1_ is empty) processStep_ 1 with select_ = ary1_.IndexOf(base), scroll reset.
/// EquipData.AddExp clamps exp_ only when called with the item already at max level, so the slices matter.
/// GetSmithCompatiRatio(e) is 0 (both match, 150%), 1 (one differs, 100%) or 2 (both differ, 50%).
/// </summary>
[HarmonyPatch]
internal static class SmithFeed
{
    internal static ConfigEntry<bool> Enabled, StopAtMaxLevel, Confirm;
    internal static ConfigEntry<int> Keep, ButtonX, ButtonY, FontScalePercent, DiscScalePercent;
    internal static ConfigEntry<string> Line1, Line2;

    private const int SmithPage = 53;        // SubForm.TYPE_WHARF_SMITH
    private const int StepEntrance = 0, StepBaseSelect = 1, StepMaterials = 2;
    private const int CompatBest = 0;        // GetSmithCompatiRatio: same type and weight (150%)
    private const int ShopSmith = 8;         // index of the smith in player_.wharfShopLv_
    private const int EaseFrames = 10;       // the effect's frames per material
    private const int MoneyTypeOther = 2;    // AddMoney type ProWharfSmith uses
    private const int EventAfterSmith = 146; // eventList_ index ProWharfSmith runs after every 合成
    private const int SeSmith = 24;          // the effect's sound
    private const int MaxBatches = 100000;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("SmithFeed", "Enabled", true, "Show the 投入所有相性相符 button on the smith's material page.");
        Keep = cfg.Bind("SmithFeed", "Keep", 10,
            "How many of each equipment to keep in stock (not counting the base item itself).");
        StopAtMaxLevel = cfg.Bind("SmithFeed", "StopAtMaxLevel", true,
            "In the last reforge, add only as many materials as needed to reach max level.");
        Confirm = cfg.Bind("SmithFeed", "Confirm", true, "Ask before reforging, showing the item count and the gold cost.");
        Line1 = cfg.Bind("SmithFeed", "Line1", "投入所有", "Button text, first line.");
        Line2 = cfg.Bind("SmithFeed", "Line2", "相性相符", "Button text, second line.");
        ButtonX = cfg.Bind("SmithFeed", "ButtonX", 272, "Button centre X on the material page (game units).");
        ButtonY = cfg.Bind("SmithFeed", "ButtonY", 172, "Button centre Y on the material page (game units).");
        FontScalePercent = cfg.Bind("SmithFeed", "FontScalePercent", 65, "Text size as a percentage of the menu font.");
        DiscScalePercent = cfg.Bind("SmithFeed", "DiscScalePercent", 135, "Button size as a percentage of the game's round icons.");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[smithfeed] {s}");

    [HarmonyPatch(typeof(SubForm), "DrawWharfSmith")]
    [HarmonyPostfix]
    private static void DrawButton(SubForm __instance, Graphics g)
    {
        if (!Enabled.Value || __instance.type_ != SmithPage || __instance.processStep_ != StepMaterials) return;
        FeedButton.DrawOurs(__instance, g, ButtonX.Value, ButtonY.Value, FeedButton.SmithValue, Line1.Value, Line2.Value,
            DiscScalePercent.Value / 100f, FontScalePercent.Value, 0);
    }

    private sealed class Cand
    {
        public EquipData Equip;
        public bool IsBase;
        public double Rank;
    }

    private sealed class Plan
    {
        public int Pieces, Batches, LvFrom, LvTo;
        public long Cost;
        public bool MoneyShort;
    }

    // Waiting for the yes/no dialog.
    private static SubForm _dialog, _page;

    private static bool Same(Il2CppSystem.Object a, Il2CppSystem.Object b) => a != null && b != null && a.Pointer == b.Pointer;

    private static int Bonus() => main.AppData.GetInstance().player_.wharfShopLv_[ShopSmith];

    /// <summary>The EXP the effect gives the base for one material right now (GetSmithExp plus the smith-level bonus).</summary>
    private static int ExpOf(EquipData baseEq, EquipData m, int bonus)
    {
        int e = baseEq.GetSmithExp(m);
        return e + e * bonus / 100;
    }

    private static long NeedToMax(EquipData b) => b.GetLvExp(b.GetSmithLvMax()) - b.exp_.Get();

    /// <summary>Every compatible piece, most EXP per gold first.</summary>
    private static List<Cand> Candidates(EquipData baseEq)
    {
        var list = new List<Cand>();
        var all = main.AppData.GetInstance().equipList_;
        int bonus = Bonus();
        for (int i = 0; i < all.Length; i++)
        {
            var e = all[i];
            if (e == null || baseEq.GetSmithCompatiRatio(e) != CompatBest) continue;
            list.Add(new Cand { Equip = e, IsBase = Same(e, baseEq), Rank = (double)ExpOf(baseEq, e, bonus) / Math.Max(1, e.GetSmithCost()) });
        }
        return list.OrderByDescending(c => c.Rank).ThenByDescending(c => c.Equip.GetNum()).ToList();
    }

    /// <summary>How many of this piece may go in, given 'used' already planned: stock − (1 if base) − keep.</summary>
    private static int Spare(Cand c, int used) => c.Equip.GetNum() - (c.IsBase ? 1 : 0) - Math.Max(0, Keep.Value) - used;

    /// <summary>
    /// One reforge's materials: in candidate order, up to 'slots', and (StopAtMaxLevel) only until their EXP covers 'need'.
    /// 'used' counts pieces already taken by earlier planned batches (dry run only).
    /// </summary>
    private static List<EquipData> PickBatch(List<Cand> cands, int slots, long need, EquipData baseEq, int bonus, Dictionary<IntPtr, int> used)
    {
        var batch = new List<EquipData>();
        long exp = 0;
        foreach (var c in cands)
        {
            used.TryGetValue(c.Equip.Pointer, out int u);
            int spare = Spare(c, u);
            int per = ExpOf(baseEq, c.Equip, bonus);
            while (spare > 0 && batch.Count < slots && !(StopAtMaxLevel.Value && exp >= need))
            {
                batch.Add(c.Equip);
                spare--; u++;
                exp += per;
            }
            used[c.Equip.Pointer] = u;
            if (batch.Count >= slots || (StopAtMaxLevel.Value && exp >= need)) break;
        }
        return batch;
    }

    private static long CostOf(List<EquipData> batch) => batch.Sum(m => (long)m.GetSmithCost());

    /// <summary>Dry run for the confirmation: same picking, EXP and cost at today's levels (exact except when the base feeds on its own copies).</summary>
    private static Plan MakePlan(SubForm page, EquipData baseEq, List<Cand> cands)
    {
        var plan = new Plan { LvFrom = baseEq.GetLv() };
        int slots = page.GetSmithMaxNum(), bonus = Bonus(), lvMax = baseEq.GetSmithLvMax();
        long money = main.AppData.GetInstance().player_.GetMoney();
        long exp = baseEq.exp_.Get(), max = baseEq.GetLvExp(lvMax);
        var used = new Dictionary<IntPtr, int>();
        while (plan.Batches < MaxBatches && exp < max)
        {
            var mark = new Dictionary<IntPtr, int>(used);
            var batch = PickBatch(cands, slots, max - exp, baseEq, bonus, used);
            if (batch.Count == 0) break;
            long cost = CostOf(batch);
            if (money < cost) { plan.MoneyShort = true; used = mark; break; }
            money -= cost;
            plan.Cost += cost;
            plan.Pieces += batch.Count;
            plan.Batches++;
            exp += batch.Sum(m => (long)ExpOf(baseEq, m, bonus));
        }
        int lv = plan.LvFrom;
        while (lv < lvMax && exp >= baseEq.GetLvExp(lv + 1)) lv++;
        plan.LvTo = lv;
        return plan;
    }

    internal static void ButtonPressed(SubForm page)
    {
        AutoFeed.Safe(() =>
        {
            if (page.type_ != SmithPage || page.processStep_ != StepMaterials || _dialog != null) return;
            var baseEq = page.mainEquip_;
            if (baseEq == null) { Log("no base item"); return; }
            if (baseEq.IsLvMax()) { AutoFeed.Show("SmithFeed: already at max level."); return; }

            var cands = Candidates(baseEq);
            var plan = MakePlan(page, baseEq, cands);
            Log($"plan for {baseEq.name_} Lv{plan.LvFrom}: {plan.Pieces} pieces in {plan.Batches} reforges, {plan.Cost:N0} G, " +
                $"Lv{plan.LvFrom} -> Lv{plan.LvTo}{(plan.MoneyShort ? " (money runs out)" : "")}");
            if (plan.Pieces == 0)
            {
                string why = plan.MoneyShort ? "金钱不足" : $"没有可投入的相性相符装备（每样保留 {Math.Max(0, Keep.Value)} 个）";
                page.Push(SubForm.MakeDialog(why));
                return;
            }
            if (!Confirm.Value) { Run(page); return; }

            string text = $"投入 {plan.Pieces} 件相性相符的装备\n花费 {plan.Cost:N0} G\n{baseEq.GetName()} Lv{plan.LvFrom} → Lv{plan.LvTo}" +
                          (plan.MoneyShort ? "（金钱不足）" : "") + "\n要锻造吗？";
            _page = page;
            _dialog = SubForm.MakeDialog(text, SubForm.BUTTONSTR_HAI_IIE);
            page.Push(_dialog);
        });
    }

    /// <summary>Called every frame: runs the reforge once the dialog closes with はい.</summary>
    internal static void Tick()
    {
        if (_dialog == null) return;
        if (_dialog.IsAlive()) return;
        var d = _dialog; var page = _page;
        _dialog = null; _page = null;
        int result = d.result_;
        Log($"dialog closed, result_ = {result}");
        if (result != 0) return;
        AutoFeed.Safe(() =>
        {
            if (page == null || !page.IsAlive() || page.type_ != SmithPage || page.processStep_ != StepMaterials) { Log("page gone; nothing done"); return; }
            Run(page);
        });
    }

    /// <summary>Repeats the game's 合成 + effect until the base is at max or nothing is left.</summary>
    private static void Run(SubForm page)
    {
        var app = main.AppData.GetInstance();
        var player = app.player_;
        var baseEq = page.mainEquip_;
        var cands = Candidates(baseEq);
        int slots = page.GetSmithMaxNum();
        int lvFrom = baseEq.GetLv(), batches = 0, pieces = 0;
        long spent = 0, gained = 0;
        string stop = "nothing left to add";

        while (batches < MaxBatches)
        {
            if (baseEq.IsLvMax()) { stop = "max level"; break; }
            int bonus = Bonus();
            var batch = PickBatch(cands, slots, NeedToMax(baseEq), baseEq, bonus, new Dictionary<IntPtr, int>());
            if (batch.Count == 0) break;

            long cost = CostOf(batch);
            if (player.GetMoney() < cost) { stop = "not enough money"; break; }
            MoneyLog.BeginSource("铁匠", "SmithFeed", SmithPage);
            try { player.AddMoney(MoneyTypeOther, -cost); }
            finally { MoneyLog.EndSource(); }
            foreach (var m in batch) m.Add(-1);

            long before = baseEq.exp_.Get();
            foreach (var m in batch)
            {
                int prev = 0;
                for (int f = 1; f <= EaseFrames; f++)
                {
                    int exp = ExpOf(baseEq, m, Bonus());
                    prev = kairo.unity.graph.Graph.Easing(0, exp, EaseFrames, f - 1, 0);
                    int d = kairo.unity.graph.Graph.Easing(0, exp, EaseFrames, f, 0) - prev;
                    if (d > 0) baseEq.AddExp(d);
                }
                player.AddSmithCountExp();
            }
            gained += baseEq.exp_.Get() - before;
            spent += cost;
            pieces += batch.Count;
            batches++;
        }

        if (batches > 0)
        {
            app.PlaySe(SeSmith);
            BackToBaseSelect(page, app, baseEq);
        }
        string msg = $"{baseEq.name_}: Lv{lvFrom} -> Lv{baseEq.GetLv()}, {pieces} pieces in {batches} reforges, {spent:N0} G, +{gained:N0} EXP; stopped: {stop}";
        Log(msg);
        AutoFeed.Show("SmithFeed: " + msg);
    }

    /// <summary>What ProWharfSmith does when the effect queue empties: tutorial event, rebuild the list, back to the base list.</summary>
    private static void BackToBaseSelect(SubForm page, main.AppData app, EquipData baseEq)
    {
        try
        {
            var events = app.eventList_;
            if (events != null && events.Length > EventAfterSmith) events[EventAfterSmith]?.ExeEvent();
            int r = page.SmithMainEquip(page.page_);
            if (r < 2) { page.processStep_ = StepEntrance; return; }
            for (int i = 0; i < 10 && (page.ary1_?.Size() ?? 0) <= 0; i++)
                page.SmithMainEquip((page.page_ + 11) % 10);
            page.processStep_ = StepBaseSelect;
            int idx = page.ary1_?.IndexOf(baseEq) ?? -1;
            page.select_ = Math.Max(0, idx);
            page._slipScrollUpdateValueTmp_ = int.MaxValue;
            page._slipScrollUpdateSizeTmp_ = int.MaxValue;
            page.SlipScrollControl(page.ary1_?.Size() ?? 0, 6, 24, 0);
        }
        catch (Exception e) { Log($"returning to the base list failed: {e.Message}"); }
    }
}
