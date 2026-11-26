using System.Collections.Generic;
using BepInEx.Configuration;
using game;
using HarmonyLib;

namespace HSSQoL;

/// <summary>
/// 船员只做勾选的活动: with StrictCheckboxes on, a crew member never does an activity whose box is unchecked,
/// unless no box is checked at all (then the game's own behaviour is kept).
///
/// How the game picks (Human._ChoiceVisitChip, ISIL): InitPriorityTable builds 4 rows {type, unchecked}:
/// 0 collect deck items (bit 2), 1 wharf supply (bit 1), 2 analyze (bit 4), 3 ship facility (bit 8), each from
/// CheckWork(bit, true). If some but not all boxes are checked, each checked row gets key 100 >> (checked − 1)
/// and is forced when Random(0..100) &lt; key; rows are sorted by key (checked first), then WorkCollectDeckItem /
/// WorkWharfSupply / WorkAnalyze / WorkShipInChip(force) are tried in order until one returns true. Unforced,
/// each needs its own condition (random rolls, ratios). All or none checked: no forcing, default order.
/// WorkShipInChip ignores force and, when Player.GetResearchPointRatio() &lt;= 20, itself falls back to
/// supply, collect (forced) and analyze, so all four functions are patched, not just the chooser.
///
/// CheckWork(flag, true) reads Player.allWorkFlag_ when Player.allWorkEnable_ == 0, else the crew's own
/// crewWorkType_; we call it as the game does so the ship-wide setting is respected. Merchants aren't Humans,
/// so only crew (crew_ != null) are affected.
/// </summary>
[HarmonyPatch]
internal static class CrewWork
{
    internal static ConfigEntry<bool> StrictCheckboxes;

    private const int Supply = 1, Collect = 2, Analyze = 4, InShip = 8;
    private static readonly HashSet<string> _logged = new();

    internal static void Bind(ConfigFile cfg)
    {
        StrictCheckboxes = cfg.Bind("CrewWork", "StrictCheckboxes", false,
            "Crew only do the activities checked in their activity settings (collect deck items, supply, analyze, ship facilities). " +
            "With no box checked they behave as in the original game. Edit, then restart the game.");
    }

    /// <summary>True when this activity must be skipped: feature on, crew has some box checked, but not this one.</summary>
    private static bool Blocked(Human h, int bit, string what)
    {
        if (!StrictCheckboxes.Value || h == null || h.crew_ == null) return false;
        bool any = h.CheckWork(Supply, true) || h.CheckWork(Collect, true) || h.CheckWork(Analyze, true) || h.CheckWork(InShip, true);
        if (!any || h.CheckWork(bit, true)) return false;
        string name = SafeName(h);
        if (_logged.Add(name + "|" + what))
            Plugin.L.LogInfo($"[crewwork] {name}: skipping {what} (unchecked; own flags {h.crewWorkType_})");
        return true;
    }

    private static string SafeName(Human h)
    {
        try { return h.crew_.GetName(); } catch { return "#" + h.forceID_; }
    }

    [HarmonyPatch(typeof(Human), "WorkCollectDeckItem")]
    [HarmonyPrefix]
    private static bool CollectPre(Human __instance, ref bool __result) => Skip(__instance, Collect, "collect", ref __result);

    [HarmonyPatch(typeof(Human), "WorkWharfSupply")]
    [HarmonyPrefix]
    private static bool SupplyPre(Human __instance, ref bool __result) => Skip(__instance, Supply, "supply", ref __result);

    [HarmonyPatch(typeof(Human), "WorkAnalyze")]
    [HarmonyPrefix]
    private static bool AnalyzePre(Human __instance, ref bool __result) => Skip(__instance, Analyze, "analyze", ref __result);

    [HarmonyPatch(typeof(Human), "WorkShipInChip")]
    [HarmonyPrefix]
    private static bool InShipPre(Human __instance, ref bool __result) => Skip(__instance, InShip, "ship facility", ref __result);

    private static bool Skip(Human h, int bit, string what, ref bool result)
    {
        try
        {
            if (!Blocked(h, bit, what)) return true;
        }
        catch (System.Exception e)
        {
            Plugin.L.LogError($"[crewwork] {e.Message}");
            return true;
        }
        result = false;
        return false;
    }
}
