using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using form;
using HarmonyLib;
using kairo.unity.form;
using UnityEngine;
using Canvas = kairo.unity.ui.Canvas;

namespace HSSQoL;

/// <summary>
/// Fast-forwards the dungeon clear sequence (toggle with F3): the CLEAR banner, the chest-by-chest loot
/// showcase and the party EXP screen with its "click to continue" waits.
///
/// Flow (from ISIL): SearchCourse.ProSearch pushes SubForm.CreateQuestClear (type 41, TYPE_QUEST_CLEAR).
/// ProQuestClear is a processStep_ state machine: 0-3 short waits, 4 banner (effect 24 × 3 frames), 5-8 text
/// and BGM waits, 9 one chest per call via Player.ProReserveCasket (this is where the loot is actually granted),
/// 10 waits for OK then pushes CreateTeamGetQuestExp (type 79, TYPE_TEAM_GET_EXP), 11 waits 40 frames + OK,
/// 98 restores HP/SP and Pops. The game already has "hold OK to fast-forward": while CheckKeyState(OK) is true it
/// runs 6 (15 after step 8) steps per frame. ProTeamGetExp drips EXP over frames and waits for CheckKeyPulse(OK).
///
/// ProTeamGetExp step 2 only advances its frame counter past 20 on a call WITHOUT an OK press (with OK it
/// drips 20 - counter = 0 frames), so a press held on every call hangs it. CheckKeyPulse is therefore forced only on
/// every other eligible Update (press, release, press…), like real clicks; CheckKeyState (held) stays forced.
///
/// So nothing is skipped: the game's own logic runs, just faster. While one of these forms is updating, OK
/// (key bit 0x100000) reads as held and pressed, and SubForm.Update is run ExtraUpdates more times per frame
/// while the form is still alive and on top. Windows those screens push (job master, evolution) stay manual
/// unless their types are added to Types.
/// </summary>
[HarmonyPatch]
internal static class DungeonSkip
{
    internal static ConfigEntry<bool> Enabled, ShowIndicator, AutoImprison, SkipTalk;
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<int> ExtraUpdates;
    internal static ConfigEntry<string> Types;

    private const int KeyOk = 0x100000;
    private static HashSet<int> _types = new();
    private static int _forcing;        // >0 while an eligible SubForm.Update is running
    private static int _updates;        // eligible Update calls so far; OK presses only on even ones
    private static bool _looping;
    private static int _lastType = -1;
    private static float _toastUntil;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("DungeonSkip", "Enabled", false,
            "Fast-forward the dungeon clear sequence (banner, loot showcase, party EXP). Toggled with ToggleKey; remembered.");
        ToggleKey = cfg.Bind("DungeonSkip", "ToggleKey", KeyCode.F3, "Toggles the dungeon clear fast-forward.");
        ExtraUpdates = cfg.Bind("DungeonSkip", "ExtraUpdates", 10,
            new ConfigDescription("Extra update steps per frame for those screens (on top of the game's own 6-15 while OK is held).",
                new AcceptableValueRange<int>(0, 60)));
        Types = cfg.Bind("DungeonSkip", "Types", "41,79",
            "SubForm types to fast-forward: 41 dungeon clear, 79 party EXP. 80 = job master window (normally left manual).");
        AutoImprison = cfg.Bind("DungeonSkip", "AutoImprison", true,
            "While on: dungeon prisoners all go straight to prison (the game's own \"imprison all\" + yes), no list or prompt.");
        SkipTalk = cfg.Bind("DungeonSkip", "SkipTalk", true,
            "While on: also fast-forward talk windows (SubForm type 0) inside dungeons, e.g. the boss dialogue. Talk elsewhere is untouched.");
        ShowIndicator = cfg.Bind("DungeonSkip", "ShowIndicator", true, "Show a small label while the fast-forward is on.");
        ParseTypes();
        Types.SettingChanged += (_, _) => ParseTypes();
    }

    /// <summary>Fast-forward active: the user setting, or forced on while 连续探索 runs.</summary>
    internal static bool On => Enabled.Value || ContinuousExplore.Active;

    private static void ParseTypes() =>
        _types = new HashSet<int>(Types.Value.Split(',', ';', ' ')
            .Select(s => int.TryParse(s.Trim(), out int v) ? v : -1).Where(v => v >= 0));

    internal static void Tick()
    {
        if (!Input.GetKeyDown(ToggleKey.Value)) return;
        Enabled.Value = !Enabled.Value;
        _toastUntil = Time.unscaledTime + 2f;
        Plugin.L.LogInfo($"[dungeonskip] {(Enabled.Value ? "on" : "off")}");
    }

    internal static void Draw()
    {
        bool toast = Time.unscaledTime < _toastUntil;
        if (!toast && !(Enabled.Value && ShowIndicator.Value)) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        string text = Enabled.Value ? "⏭ 跳过结算" : "跳过结算 关";
        float y = SpeedHack.Active && SpeedHack.ShowIndicator.Value ? 30 : 8;
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(Screen.width - 121, y + 1, 120, 24), text, style);
        GUI.color = Color.white;
        GUI.Label(new Rect(Screen.width - 122, y, 120, 24), text, style);
    }

    private const int PrisonList = 38, PrisonModeDungeon = 1, StepImprisonAll = 3;

    /// <summary>
    /// Dungeon prisoners: QuestClear queues them (Player.AddEvent), Player.ProEvent pushes
    /// SubForm.CreateQuestClearPrizon(list) = type 38 (TYPE_PRIZON_CREW_LIST), mode_ 1, ary1_ = the list.
    /// ProPrizonCrewList's "imprison all" soft label opens MakeDialog(yes/no) into dialog_ and sets processStep_ 3.
    /// Step 3 reads dialog_.result_ (0 = yes), clears dialog_, then puts every crew in ary1_ into empty prisons
    /// (ShipInBuild.IsPrizonEmpty), sends the overflow to the deck as captured crew (DeckFighter.CreateCaptureCrew)
    /// and Pops. We jump straight there with an unpushed dummy dialog whose result_ is 0.
    /// </summary>
    private static void ImprisonAll(SubForm f)
    {
        if (f.type_ != PrisonList || f.mode_ != PrisonModeDungeon || f.dialog_ != null) return;
        if (f.processStep_ == StepImprisonAll) return;
        var yes = SubForm.MakeDialog("");
        yes.result_ = 0;
        f.dialog_ = yes;
        f.processStep_ = StepImprisonAll;
        Plugin.L.LogInfo($"[dungeonskip] imprisoning all {f.ary1_?.Size() ?? 0} prisoner(s)");
    }

    private const int Talk = 0;   // SubForm.TYPE_TALK: ProTalk = OK flushes the text, OK again next page / Pop; no choices

    /// <summary>Inside a dungeon: Player.searchCourse_ is the running SearchCourse.</summary>
    private static bool InDungeon() => main.AppData.GetInstance()?.player_?.searchCourse_ != null;

    /// <summary>
    /// Boss (and other) dungeon dialogue: SearchCourse.Script's SCR_EVENT calls AppData.StartEvent, which queues a
    /// DelayEvent; FixEvent.ExeEvent then pushes SubForm.MakeTalkForm windows (type 0).
    /// </summary>
    private static bool Eligible(SubForm f) =>
        On && (_types.Contains(f.type_) || (f.type_ == Talk && SkipTalk.Value && InDungeon()));

    private static bool IsTop(SubForm f)
    {
        var top = FormManagerBase.GetInstance()?.Top;
        return top != null && top.Pointer == f.Pointer;
    }

    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPrefix]
    private static void UpdatePre(SubForm __instance, out bool __state)
    {
        if (On && AutoImprison.Value && !_looping)
        {
            try { ImprisonAll(__instance); }
            catch (Exception e) { Plugin.L.LogError($"[dungeonskip] imprison: {e}"); AutoImprison.Value = false; }
        }
        __state = Eligible(__instance);
        if (!__state)
        {
            // Lists other windows seen in dungeons, to find any dialogue that still needs handling.
            if (On && !_looping && __instance.type_ != _lastType && InDungeon())
            {
                _lastType = __instance.type_;
                Plugin.L.LogInfo($"[dungeonskip] seen in dungeon (not skipped): SubForm type {_lastType}");
            }
            return;
        }
        _forcing++;
        _updates++;
        if (__instance.type_ != _lastType && !_looping)
        {
            _lastType = __instance.type_;
            Plugin.L.LogInfo($"[dungeonskip] fast-forwarding SubForm type {_lastType} (step {__instance.processStep_})");
        }
    }

    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPostfix]
    private static void UpdatePost(SubForm __instance, bool flag, ref bool __result, bool __state)
    {
        if (!__state) return;
        try
        {
            if (_looping || !__result) return;
            _looping = true;
            for (int i = 0; i < ExtraUpdates.Value; i++)
            {
                if (!__instance.IsAlive() || !IsTop(__instance) || !Eligible(__instance)) break;
                if (!__instance.Update(flag)) { __result = false; break; }
            }
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"[dungeonskip] {e}");
            Enabled.Value = false;
        }
        finally
        {
            if (_looping && _forcing == 1) _looping = false;
            _forcing--;
        }
    }

    [HarmonyPatch(typeof(Canvas), nameof(Canvas.CheckKeyPulse), typeof(int))]
    [HarmonyPrefix]
    private static bool KeyPulse(int key, ref bool __result) => (_updates & 1) == 0 || Force(key, ref __result);

    [HarmonyPatch(typeof(Canvas), nameof(Canvas.CheckKeyState), typeof(int))]
    [HarmonyPrefix]
    private static bool KeyState(int key, ref bool __result) => Force(key, ref __result);

    private static bool Force(int key, ref bool __result)
    {
        if (_forcing <= 0 || (key & KeyOk) == 0) return true;
        __result = true;
        return false;
    }
}
