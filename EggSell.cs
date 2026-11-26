using System;
using BepInEx.Configuration;
using form;
using game;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Graphics = kairo.unity.ui.Graphics;

namespace HSSQoL;

/// <summary>
/// Monster ranch (port egg shop) → 卖蛋 → "选择想要出售的蛋": a round button that sells every egg in the list.
///
/// The ranch's sell command builds the list with SubForm.GetEggList() and opens SubForm type 72
/// (TYPE_EGG_LIST) with mode_ 0 (EGG_LIST_MODE_SALE) and ary1_ = that list. Each list entry is
/// Object[] { int[] loc, Egg egg }, and the egg itself is stored in a ship warehouse:
///   player_.currentShip_.ship_.inMap_[loc[0]].mapchips_[loc[2]][loc[1]].building_.GetWarehouseItem()
/// as Object[] { JInteger(1), egg }.
///
/// The game's sale of one egg (ProEggList, processStep_ 1, "yes" in the confirm dialog), replayed per egg:
///   search the warehouse list from the end for [JInteger 1, egg]; if found:
///   player_.AddMoney(2, egg.GetPrice()); warehouse.RemoveElementAt(j); ary1_.RemoveElementAt(select_);
///   SlipScrollControl(ary1_.Size(), 4, 40); when ary1_ is empty: Pop() the list and Push a dialog.
/// </summary>
[HarmonyPatch]
internal static class EggSell
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> ButtonX, ButtonY;
    internal static ConfigEntry<string> Line1, Line2;

    private const int EggListPage = 72;   // SubForm.TYPE_EGG_LIST
    private const int ModeSale = 0;       // EGG_LIST_MODE_SALE (1 = pick an egg to hatch)
    private const int StepSelect = 0;     // EGG_LIST_PRO_SELECT (1 = confirm dialog)
    private const int MoneyTypeSale = 2;
    private const int StoredEgg = 1;      // warehouse entry tag for eggs
    private const int SeSell = 20;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("EggSell", "Enabled", true, "Show the sell-all button on the monster ranch's egg sale list.");
        Line1 = cfg.Bind("EggSell", "Line1", "全部", "Button text, first line.");
        Line2 = cfg.Bind("EggSell", "Line2", "出售", "Button text, second line.");
        ButtonX = cfg.Bind("EggSell", "ButtonX3", -36, "Button centre X on the egg sale list (game units; default: above 切换).");
        ButtonY = cfg.Bind("EggSell", "ButtonY3", 157, "Button centre Y on the egg sale list (game units).");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[eggsell] {s}");

    private static bool IsSaleList(SubForm sf) =>
        sf != null && sf.type_ == EggListPage && sf.mode_ == ModeSale && sf.processStep_ == StepSelect;

    [HarmonyPatch(typeof(SubForm), "DrawEggList")]
    [HarmonyPostfix]
    private static void DrawButton(SubForm __instance, Graphics g)
    {
        if (!Enabled.Value || !IsSaleList(__instance) || (__instance.ary1_?.Size() ?? 0) == 0) return;
        FeedButton.DrawOurs(__instance, g, ButtonX.Value, ButtonY.Value, FeedButton.EggSellValue, Line1.Value, Line2.Value);
    }

    internal static void ButtonPressed(SubForm page) => AutoFeed.Safe(() =>
    {
        if (!IsSaleList(page)) { Log($"ignored: page type {page?.type_} mode {page?.mode_} step {page?.processStep_}"); return; }
        var list = page.ary1_;
        int n = list?.Size() ?? 0;
        if (n == 0) return;

        var app = main.AppData.GetInstance();
        var player = app.player_;
        var maps = player.currentShip_?.ship_?.inMap_;
        if (maps == null) { Log("no ship maps; nothing sold"); return; }

        long before = player.GetMoney();
        int sold = 0, missing = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            var entry = list.ElementAt(i)?.TryCast<Il2CppReferenceArray<Il2CppSystem.Object>>();
            var loc = entry?[0]?.TryCast<Il2CppStructArray<int>>();
            var egg = entry?[1]?.TryCast<Egg>();
            if (loc == null || egg == null) { missing++; continue; }

            var store = maps[loc[0]].mapchips_[loc[2]][loc[1]]?.building_?.GetWarehouseItem();
            int j = FindStored(store, egg);
            if (j < 0) { missing++; continue; }   // the game also does nothing when the egg isn't found

            int price = egg.GetPrice();
            player.AddMoney(MoneyTypeSale, price);
            store.RemoveElementAt(j);
            list.RemoveElementAt(i);
            sold++;
        }
        long gained = player.GetMoney() - before;
        Log($"sold {sold} of {n} eggs for {gained:N0} G" + (missing > 0 ? $"; {missing} not found in a warehouse" : ""));
        if (sold == 0) return;

        app.PlaySe(SeSell);
        int left = list.Size();
        string summary = $"出售了 {sold} 个蛋\n获得 {gained:N0} G" + (missing > 0 ? $"\n{missing} 个蛋未在仓库中找到，未出售" : "");
        if (left > 0)
        {
            page.select_ = 0;
            page.SlipScrollControl(left, 4, 40);
            page.Push(SubForm.MakeDialog(summary));
        }
        else
        {
            // Same as the game after the last egg is sold: close the list, then show a dialog.
            page.Pop();
            page.Push(SubForm.MakeDialog(summary));
        }
    });

    /// <summary>Index of [JInteger 1, egg] in a warehouse list, searched from the end like the game does.</summary>
    private static int FindStored(java.util.Vector<Il2CppReferenceArray<Il2CppSystem.Object>> store, Egg egg)
    {
        if (store == null) return -1;
        for (int j = store.Size() - 1; j >= 0; j--)
        {
            var e = store.ElementAt(j);
            if (e == null || e.Length < 2) continue;
            var tag = e[0]?.TryCast<java.lang.JInteger>();
            if (tag == null || tag.IntValue() != StoredEgg) continue;
            if (e[1] != null && e[1].Pointer == egg.Pointer) return j;
        }
        return -1;
    }
}
