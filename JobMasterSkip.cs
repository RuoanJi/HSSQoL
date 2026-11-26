using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using form;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Fast-forwards the Job Master window (toggle with F4), the one 强化满级 / 转职满级 and the game's own feeding
/// open each time a job reaches max level.
///
/// SubForm.CreateJobMaster(c) = type 80 (TYPE_JOB_MASTER). InitJobMaster only sets the effect; ProJobMaster is a
/// two-step state machine on processStep_: step 0 waits for CheckKeyPulse(OK) (or the soft label), step 1 counts
/// frames (+172) past 20 while the window slides in (DrawMoveWindow) and the bonus numbers ease up
/// (Graph.Easing), then on OK it runs event 103 (one-time tutorial), Character.AddJobMasterBonus(), pushes
/// CreateAllJobMaster when every job is mastered, and Pops. So the bonus is granted by the game itself, just sooner.
///
/// The mechanism is DungeonSkip's (its SubForm.Update and Canvas key patches): SubForm.Update is re-run
/// ExtraUpdates more times per frame, and with AutoConfirm OK also reads as pressed (on every other call, like
/// clicks) so the window closes by itself. CreateAllJobMaster is type 39 (a message window also used elsewhere),
/// so it isn't in the default Types.
/// </summary>
internal static class JobMasterSkip
{
    internal static ConfigEntry<bool> Enabled, AutoConfirm, ShowIndicator;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<int> ExtraUpdates;
    internal static ConfigEntry<string> Types;

    private static HashSet<int> _types = new();
    private static float _toastUntil;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("JobMasterSkip", "Enabled", false,
            "Fast-forward the Job Master window shown when a job reaches max level. Toggled with ToggleKey; remembered.");
        ToggleKey = cfg.Bind("JobMasterSkip", "ToggleKey", KeyCode.F4, "Toggles the Job Master window fast-forward.");
        ExtraUpdates = cfg.Bind("JobMasterSkip", "ExtraUpdates", 10,
            new ConfigDescription("Extra update steps per frame for the window (its animation takes about 20 steps).",
                new AcceptableValueRange<int>(0, 60)));
        AutoConfirm = cfg.Bind("JobMasterSkip", "AutoConfirm", true,
            "Also press OK for you, so the window closes by itself (the job-clear bonus is still granted). Off: only the animation is sped up and you click to close.");
        Types = cfg.Bind("JobMasterSkip", "Types", "80",
            "SubForm types to fast-forward: 80 Job Master window. 39 would add the all-jobs-mastered message, but that type is also used by other message windows.");
        ShowIndicator = cfg.Bind("JobMasterSkip", "ShowIndicator", true, "Show a small label while the fast-forward is on.");
        ParseTypes();
        Types.SettingChanged += (_, _) => ParseTypes();
    }

    private static void ParseTypes() =>
        _types = new HashSet<int>(Types.Value.Split(',', ';', ' ')
            .Select(s => int.TryParse(s.Trim(), out int v) ? v : -1).Where(v => v >= 0));

    /// <summary>0 = leave alone, 1 = extra updates only, 2 = extra updates and OK pressed.</summary>
    internal static int Mode(SubForm f) =>
        !Enabled.Value || !_types.Contains(f.type_) ? 0 : AutoConfirm.Value ? 2 : 1;

    internal static void Tick()
    {
        if (!Input.GetKeyDown(ToggleKey.Value)) return;
        Enabled.Value = !Enabled.Value;
        _toastUntil = Time.unscaledTime + 2f;
        Plugin.L.LogInfo($"[jobmasterskip] {(Enabled.Value ? "on" : "off")}");
    }

    internal static void Draw()
    {
        bool toast = Time.unscaledTime < _toastUntil;
        if (!toast && !(Enabled.Value && ShowIndicator.Value)) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        string text = Enabled.Value ? "⏭ 跳过满级动画" : "跳过满级动画 关";
        float y = 8;
        if (SpeedHack.Active && SpeedHack.ShowIndicator.Value) y += 22;
        if (DungeonSkip.LabelShown) y += 22;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(Screen.width - 151, y + 1, 150, 24), text, style);
        GUI.color = Color.white;
        GUI.Label(new Rect(Screen.width - 152, y, 150, 24), text, style);
    }
}
