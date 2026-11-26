using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using game;
using HarmonyLib;

namespace HSSQoL;

/// <summary>
/// 战斗队伍成员不去收获设施: crews in a combat party never pick a harvest facility (农田 39, 植树厂 40,
/// 牧场 鸡/猪/牛 41–43; ShipInChipModel.IsHarvestChip = type 8 farm / 9 tree / 10 ranch, the only models with
/// harvestTime). Everyone else is unaffected.
///
/// Human.WorkShipInChip (ISIL) is the only place crew choose a ship facility on their own. Phase A sends them to
/// a harvest chip (random among the floor's harvest chips); phase B walks all visitable chips by distance with
/// random rolls. Both read player_.currentShip_.GetShip().inMap_[floor].visitChips_ and skip a chip when any slot
/// of chip.building_.visitHuman_ is non-null (already in use), then commit through SetVistQueue. So for a party
/// member we fill one slot of each free harvest chip with that crew for the length of the call (looks occupied),
/// and empty it again afterwards. Nothing else runs in between (single-threaded game loop), and the chosen chip is
/// never one of ours, so the arrays end exactly as they were.
/// </summary>
[HarmonyPatch]
internal static class PartyNoHarvest
{
    internal static ConfigEntry<bool> Enabled;

    private static readonly List<(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Human> arr, int idx)> _masked = new();
    private static readonly HashSet<string> _logged = new();

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("PartyNoHarvest", "Enabled", false,
            "Crew in a combat party never go to harvest facilities (农田, 植树厂, 牧场 鸡/猪/牛). " +
            "Crew outside parties are unaffected. Edit, then restart the game.");
    }

    private static bool InParty(Player p, Human h)
    {
        var parties = p.force_;
        int n = parties?.Size() ?? 0;
        for (int pi = 0; pi < n; pi++)
        {
            var members = parties.Get(pi)?.TryCast<java.util.Vector>();
            int m = members?.Size() ?? 0;
            for (int si = 0; si < m; si++)
                if (members.Get(si)?.TryCast<Human>()?.Pointer == h.Pointer) return true;
        }
        return false;
    }

    [HarmonyPatch(typeof(Human), "WorkShipInChip")]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Pre(Human __instance)
    {
        Restore(); // in case a previous call threw before the finalizer
        if (!Enabled.Value || __instance?.crew_ == null) return;
        try
        {
            var p = main.AppData.GetInstance()?.player_;
            if (p == null || !InParty(p, __instance)) return;
            var floors = p.currentShip_?.GetShip()?.inMap_;
            if (floors == null) return;
            for (int f = 0; f < floors.Length; f++)
            {
                var chips = floors[f]?.visitChips_;
                int n = chips?.Size() ?? 0;
                for (int i = 0; i < n; i++)
                {
                    var chip = chips.Get(i)?.TryCast<game.map.ShipInChip>();
                    var model = chip?.GetModel();
                    if (model == null || !model.IsHarvestChip()) continue;
                    var arr = chip.building_?.visitHuman_;
                    if (arr == null || arr.Length == 0) continue;
                    bool free = true;
                    for (int k = 0; k < arr.Length; k++) if (arr[k] != null) { free = false; break; }
                    if (!free) continue;
                    arr[0] = __instance;
                    _masked.Add((arr, 0));
                }
            }
            if (_masked.Count > 0)
            {
                string name;
                try { name = __instance.crew_.GetName(); } catch { name = "#" + __instance.forceID_; }
                if (_logged.Add(name))
                    Plugin.L.LogInfo($"[partynoharvest] {name} (in party): hiding {_masked.Count} harvest facilities");
            }
        }
        catch (Exception e)
        {
            Restore();
            Plugin.L.LogError($"[partynoharvest] {e.Message}");
        }
    }

    private static int _picks;

    /// <summary>Logs who picks a harvest facility (first 100 per session), so the rule can be checked in the log.</summary>
    [HarmonyPatch(typeof(Human), nameof(Human.SetVistQueue))]
    [HarmonyPostfix]
    private static void PickPost(Human __instance)
    {
        if (!Enabled.Value || _picks >= 100 || __instance?.crew_ == null) return;
        try
        {
            var model = __instance.GetVisitChip()?.GetModel();
            if (model == null || !model.IsHarvestChip()) return;
            var p = main.AppData.GetInstance()?.player_;
            bool party = p != null && InParty(p, __instance);
            _picks++;
            Plugin.L.LogInfo($"[partynoharvest] pick {__instance.crew_.GetName()} -> harvest type {model.type_} (in party: {party}){(party ? " UNEXPECTED" : "")}");
        }
        catch (Exception e) { Plugin.L.LogError($"[partynoharvest] pick: {e.Message}"); }
    }

    [HarmonyPatch(typeof(Human), "WorkShipInChip")]
    [HarmonyFinalizer]
    private static void Fin() => Restore();

    private static void Restore()
    {
        foreach (var (arr, idx) in _masked)
            try { arr[idx] = null; } catch { }
        _masked.Clear();
    }
}
