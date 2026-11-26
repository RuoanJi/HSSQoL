using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace HSSQoL;

[BepInPlugin(Guid, Name, Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "hssqol";
    public const string Name = "High Sea Saga QoL";
    public const string Version = "0.3.0";

    internal static ManualLogSource L;
    internal static ConfigEntry<KeyCode> DebugOverlayKey;
    internal static ConfigEntry<bool> MenuProbeEnabled;

    /// <summary>On-screen debug info (the 强化满级 panel). Toggled with F1; always starts off.</summary>
    internal static bool DebugOverlay;

    public override void Load()
    {
        L = Log;
        DebugOverlayKey = Config.Bind("Debug", "OverlayToggleKey", KeyCode.F1,
            "Shows or hides on-screen debug info (the 强化满级 panel). Off at every launch. Logging is always on.");
        MenuProbeEnabled = Config.Bind("Debug", "MenuProbe", false,
            "Enables F6: logs one frame of menu draw calls (hooks Graphics every frame, so leave off normally).");
        InputRemap.Bind(Config);
        AutoFeed.Bind(Config);
        FeedButton.Bind(Config);
        SpeedHack.Bind(Config);
        JobSwitch.Bind(Config);
        AutoSell.Bind(Config);
        SmithFeed.Bind(Config);
        ShipExport.Bind(Config);
        DataDump.Bind(Config);
        DungeonSkip.Bind(Config);
        JobMasterSkip.Bind(Config);
        ContinuousExplore.Bind(Config);
        MoneyLog.Bind(Config);
        KnowLog.Bind(Config);
        FormationFit.Bind(Config);
        CrewWork.Bind(Config);
        PartyNoHarvest.Bind(Config);

        // Registers the type with IL2CPP and attaches it to a persistent GameObject.
        AddComponent<QoLBehaviour>();

        // All patches touch game types (e.g. form.SubForm) whose static constructors need loaded
        // game data, so they're applied once a save is loaded: see PatchLate.
        Harmony = new Harmony(Guid);
        L.LogInfo($"{Name} {Version} loaded");
    }

    internal static Harmony Harmony;
    private static bool _latePatched;
    internal static bool LateReady => _latePatched;

    /// <summary>Applies patches that are only safe once a save is loaded. Called every frame until done.</summary>
    internal static void PatchLate()
    {
        if (_latePatched) return;
        if (main.AppData.GetInstance()?.player_ == null) return;
        _latePatched = true;
        // FeedButton first: the probe is optional and must never take the real feature down with it.
        foreach (var t in new[] { typeof(FeedButton), typeof(AutoSell), typeof(SmithFeed), typeof(DungeonSkip), typeof(ContinuousExplore), typeof(FormationFit), typeof(CrewWork), typeof(PartyNoHarvest), typeof(JobSwitch.GameSwitchPatch), typeof(MenuProbe), typeof(MenuProbe.Results) })
        {
            // The menu probe hooks hot Graphics calls every frame; only patch it when asked for.
            if ((t == typeof(MenuProbe) || t == typeof(MenuProbe.Results)) && !MenuProbeEnabled.Value) continue;
            try { Harmony.PatchAll(t); }
            catch (Exception e) { L.LogError($"Late patching {t.Name} failed: {e}"); }
        }
        try { MoneyLog.Patch(Harmony); }
        catch (Exception e) { L.LogError($"Late patching MoneyLog failed: {e}"); }
        try { KnowLog.Patch(Harmony); }
        catch (Exception e) { L.LogError($"Late patching KnowLog failed: {e}"); }
        // Listing patched methods makes it easy to spot a target that vanished after a game update.
        foreach (var m in Harmony.GetPatchedMethods())
            L.LogInfo($"Patched {m.DeclaringType?.FullName}.{m.Name}({m.GetParameters().Length} args)");
    }
}

public class QoLBehaviour : MonoBehaviour
{
    public QoLBehaviour(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        if (Input.GetKeyDown(Plugin.DebugOverlayKey.Value))
        {
            Plugin.DebugOverlay = !Plugin.DebugOverlay;
            Plugin.L.LogInfo($"Debug overlay {(Plugin.DebugOverlay ? "on" : "off")}");
        }
        Plugin.PatchLate();
        if (Plugin.LateReady && Plugin.MenuProbeEnabled.Value) MenuProbe.Tick();
        SpeedHack.Tick();
        DungeonSkip.Tick();
        JobMasterSkip.Tick();
        if (Plugin.LateReady)
        {
            try { ContinuousExplore.Tick(); }
            catch (Exception e) { Plugin.L.LogError($"ContinuousExplore: {e}"); }
        }
        if (Plugin.LateReady) GameFeed.Tick();
        if (Plugin.LateReady) JobChain.Tick();
        if (Plugin.LateReady)
        {
            try { SmithFeed.Tick(); }
            catch (Exception e) { Plugin.L.LogError($"SmithFeed: {e}"); }
        }
        if (Plugin.LateReady) ShipExport.Tick();
        DataDump.Tick();
        try { MoneyLog.Tick(); }
        catch (Exception e) { Plugin.L.LogError($"MoneyLog: {e}"); }
        try { KnowLog.Tick(); }
        catch (Exception e) { Plugin.L.LogError($"KnowLog: {e}"); }
        try { InputRemap.Tick(); }
        catch (Exception e) { Plugin.L.LogError($"InputRemap: {e}"); InputRemap.WheelScroll.Value = false; }
    }

    private void OnGUI()
    {
        SpeedHack.Draw();
        DungeonSkip.Draw();
        JobMasterSkip.Draw();
        ContinuousExplore.Draw();
        MoneyLog.Draw();
        KnowLog.Draw();
        if (!Plugin.DebugOverlay) return;
        GUI.Label(new Rect(10, 10, 600, 24), $"HSSQoL {Plugin.Version} debug ({Plugin.DebugOverlayKey.Value} to hide)");
        AutoFeed.Draw();
    }
}
