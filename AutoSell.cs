using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using data;
using form;
using HarmonyLib;
using UnityEngine;
using Graphics = kairo.unity.ui.Graphics;

namespace HSSQoL;

/// <summary>
/// Port-shop sell screen: a round button that sells surplus stock down to a small amount.
///
/// Every port shop (weapons, armour, items) is SubForm type 52 (TYPE_WHARF_SHOP_ITEM); its sell list is
/// processStep_ 2, built by SetShopList(false) from every owned EquipData and ItemData. The game sells one
/// entry in processStep_ 4 as: AddMoney(2, count × GetSalePrice()), entry.Add(-count), PlaySe(20).
/// Stock counts (GetNum) exclude gear crew are wearing (Character.Equip moves one in/out of stock).
///
/// Qualifies: all equipment; power-up food (ItemData.type_ 1); HP/SP potions (type_ 2, allType_ 0/1).
/// Never: general items (type_ 0: materials, egg colour items…), other potions (aura, speed, star-up,
/// knowledge-up), birth-job items (3), whistles (4), name items (5); plus anything in ExcludeIds.
/// Rule: the holding cap (Player.GetItemMaxNum, same as GetEquipMaxNum) must be ≥ MinCap; an entry with more
/// than ThresholdPercent % of the cap in stock is sold down to Keep.
/// </summary>
[HarmonyPatch]
internal static class AutoSell
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> ThresholdPercent, Keep, MinCap, ButtonX, ButtonY;
    internal static ConfigEntry<string> ExcludeIds, Line1, Line2;

    private const int ShopPage = 52;          // SubForm.TYPE_WHARF_SHOP_ITEM
    private const int StepSellList = 2;       // ProWharfShopItem: processStep_ 2 = sell list
    private const int MoneyTypeSale = 2;      // AddMoney type the game uses for selling
    private const int SeSell = 20;
    private const int ItemPowerUp = 1, ItemDrug = 2, DrugHp = 0, DrugSp = 1;

    private sealed class Sale
    {
        public BaseDataExt Entry;
        public string Name;
        public bool Equip;
        public int Id, Have, Count, Price;
        public long Total => (long)Count * Price;
    }

    private static int _lastStep = int.MinValue;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("AutoSell", "Enabled", true, "Show the auto-sell button on the port shops' sell screen.");
        ThresholdPercent = cfg.Bind("AutoSell", "ThresholdPercent", 50,
            "Sell an item when you hold more than this percentage of the holding cap.");
        Keep = cfg.Bind("AutoSell", "Keep", 30, "How many of each sold item to keep.");
        MinCap = cfg.Bind("AutoSell", "MinCap", 100, "Do nothing while the holding cap is below this.");
        ExcludeIds = cfg.Bind("AutoSell", "ExcludeIds", "",
            "Never sell these. Comma-separated: item names as shown in game, or e:<id> for equipment / i:<id> for items.");
        Line1 = cfg.Bind("AutoSell", "Line1", "批量", "Button text, first line.");
        Line2 = cfg.Bind("AutoSell", "Line2", "出售", "Button text, second line.");
        ButtonX = cfg.Bind("AutoSell", "ButtonX", 260, "Button centre X on the sell screen (game units).");
        ButtonY = cfg.Bind("AutoSell", "ButtonY", 199, "Button centre Y on the sell screen (game units).");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[autosell] {s}");

    [HarmonyPatch(typeof(SubForm), "DrawWharfShopItem")]
    [HarmonyPostfix]
    private static void DrawButton(SubForm __instance, Graphics g)
    {
        if (!Enabled.Value || __instance.type_ != ShopPage) return;
        int step = __instance.processStep_;
        if (step != _lastStep) { _lastStep = step; Log($"shop processStep_ = {step}"); }
        if (step != StepSellList) return;
        FeedButton.DrawOurs(__instance, g, ButtonX.Value, ButtonY.Value, FeedButton.SellValue, Line1.Value, Line2.Value);
    }

    private static HashSet<string> Excluded() =>
        new((ExcludeIds.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0));

    private static bool Qualifies(ItemData it) =>
        it.type_ == ItemPowerUp || (it.type_ == ItemDrug && (it.allType_ == DrugHp || it.allType_ == DrugSp));

    /// <summary>What would be sold right now, by the rules above.</summary>
    private static List<Sale> Plan(out int cap, out int limit)
    {
        var app = main.AppData.GetInstance();
        cap = app.player_.GetItemMaxNum();
        int lim = limit = cap * ThresholdPercent.Value / 100;
        var list = new List<Sale>();
        if (cap < MinCap.Value) return list;
        var ex = Excluded();
        int keep = Math.Max(0, Keep.Value);

        void Consider(BaseDataExt e, bool equip, int id, string name, int price)
        {
            int have = e.GetNum();
            if (have <= lim || have <= keep) return;
            if (ex.Contains(name) || ex.Contains((equip ? "e:" : "i:") + id)) return;
            list.Add(new Sale { Entry = e, Equip = equip, Id = id, Name = name, Have = have, Count = have - keep, Price = price });
        }

        var equips = app.equipList_;
        for (int i = 0; i < equips.Length; i++)
        {
            var e = equips[i];
            if (e != null) Consider(e, true, e.id_, e.name_, e.GetSalePrice());
        }
        var items = app.itemList_;
        for (int i = 0; i < items.Length; i++)
        {
            var it = items[i];
            if (it != null && Qualifies(it)) Consider(it, false, it.id_, it.name_, it.GetSalePrice());
        }
        return list;
    }

    internal static void ButtonPressed(SubForm page)
    {
        AutoFeed.Safe(() =>
        {
            var plan = Plan(out int cap, out int limit);
            if (cap < MinCap.Value) { AutoFeed.Show($"AutoSell: holding cap is {cap}, below {MinCap.Value}; nothing sold."); return; }
            if (plan.Count == 0) { AutoFeed.Show($"AutoSell: nothing above {limit} (cap {cap}); nothing to sell."); return; }

            long total = plan.Sum(s => s.Total);
            int pieces = plan.Sum(s => s.Count);
            Log($"plan: cap {cap}, sell above {limit}, keep {Keep.Value}: {plan.Count} kinds, {pieces} pieces, {total:N0} G");
            foreach (var s in plan)
                Log($"  {(s.Equip ? "equip" : "item")} {s.Id} {s.Name}: {s.Have} -> {s.Have - s.Count}, sell {s.Count} x {s.Price} = {s.Total:N0}");

            Sell(plan, page);
        });
    }

    /// <summary>The game's own per-entry sale (ProWharfShopItem processStep_ 4), once per planned entry.</summary>
    private static void Sell(List<Sale> plan, SubForm page)
    {
        var app = main.AppData.GetInstance();
        long before = app.player_.GetMoney();
        foreach (var s in plan)
        {
            int count = Math.Min(s.Count, s.Entry.GetNum() - Math.Max(0, Keep.Value)); // stock may have changed
            if (count <= 0) continue;
            app.player_.AddMoney(MoneyTypeSale, (long)count * s.Price);
            s.Entry.Add(-count);
        }
        app.PlaySe(SeSell);
        long gained = app.player_.GetMoney() - before;

        // Rebuild the sell list the way the screen does, and keep the cursor in range.
        try
        {
            page.ary1_ = page.SetShopList(false);
            int n = page.ary1_?.Size() ?? 0;
            if (page.select_ >= n) page.select_ = Math.Max(0, n - 1);
        }
        catch (Exception e) { Log($"list refresh failed: {e.Message}"); }

        AutoFeed.Show($"AutoSell: sold {plan.Count} kinds for {gained:N0} G.");
    }
}
