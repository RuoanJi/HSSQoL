using System;
using BepInEx.Configuration;
using data;
using form;
using game;
using HarmonyLib;
using kairo.unity.form;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// 连续探索: a button on the dungeon party picker that keeps re-running the same dungeon until stamina runs out.
///
/// Game flow (ISIL of SubForm.ProWharfQuest): the dungeon entrance list (type 51, TYPE_WHARF_QUEST, course list in
/// ary1_, cursor select_, entrance wharfArea_) pushes CreateEditQuestForce(...) (type 90, the party picker) into its
/// dialog_. The picker's "decide" row does AppData.RemoveAllPopup, ForceEditData.Init, result_ = 0, Pop. Back in
/// ProWharfQuest, dialog_ with result_ 0 starts the run: stamina / medal check, "no live crew" check, RemoveBattleGroup
/// per party, UseStamina(GetCost), Player.GoSearchArea(course), Pop. Player.ProSearch nulls player_.searchCourse_
/// when the run ends.
///
/// Our button does exactly what "decide" does. Each next run re-opens the entrance list on the game form
/// (CreateWharfQuest, as GameForm.Key does), puts the cursor on the same course and answers it with a dummy dialog
/// whose result_ is 0, so the game's own start code runs (and its own checks). The loop stops when stamina is below
/// the cost, when a run ends without the clear screen (type 41), when a run doesn't start, or on a click on the label.
/// </summary>
[HarmonyPatch]
internal static class ContinuousExplore
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> ButtonX, ButtonY;
    internal static ConfigEntry<int> IdleFrames, MaxRuns;

    private const int WharfQuest = 51, EditQuestForce = 90, QuestClear = 41, Departure = 23;
    private const int CostStamina = 1;   // CourseData.costType_: 1 stamina (UseStamina), 0 medals (AddResearchPoint(0, -cost))

    private enum Stage { Off, Starting, Running, WaitIdle, Reopening }

    private static Stage _stage;
    private static WharfArea _area;
    private static CourseData _course;
    private static int _runs, _idle, _wait;
    private static bool _sawClear, _clickPending, _stopRequested;
    private static SubForm _lastWharf, _reopened;
    private static string _stopReason = "";
    private static float _msgUntil;

    internal static bool Active => _stage != Stage.Off;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("ContinuousExplore", "Enabled", true, "Show the 连续探索 button on the dungeon party picker.");
        ButtonX = cfg.Bind("ContinuousExplore", "ButtonX3", 0.0644f, "Button centre, fraction of the window width (default: the empty slot bottom-left, opposite 返回).");
        ButtonY = cfg.Bind("ContinuousExplore", "ButtonY3", 0.9504f, "Button centre, fraction of the window height.");
        IdleFrames = cfg.Bind("ContinuousExplore", "IdleFrames", 30,
            "Frames with no window open after a run before the next one starts (lets the game's own end-of-run events show first).");
        MaxRuns = cfg.Bind("ContinuousExplore", "MaxRuns", 0, "Stop after this many runs (0 = until stamina runs out).");
    }

    private static Player P => main.AppData.GetInstance()?.player_;

    private static SubForm TopSub()
    {
        var top = FormManagerBase.GetInstance()?.Top;
        return top?.TryCast<SubForm>();
    }

    private static bool Same(Il2CppSystem.Object a, Il2CppSystem.Object b) => a != null && b != null && a.Pointer == b.Pointer;

    /// <summary>The party picker on top, opened from a dungeon entrance list for a stamina-cost course.</summary>
    private static bool PickerOpen(out SubForm picker, out CourseData course, out int index)
    {
        picker = null; course = null; index = -1;
        var top = TopSub();
        if (top == null || top.type_ != EditQuestForce || _lastWharf == null) return false;
        try
        {
            if (!_lastWharf.IsAlive() || !Same(_lastWharf.dialog_, top)) return false;
            index = _lastWharf.select_;
            course = _lastWharf.ary1_?.ElementAt(index)?.TryCast<CourseData>();
            if (course == null || course.costType_ != CostStamina) return false;   // medal-cost courses: no loop
            picker = top;
            return true;
        }
        catch { return false; }
    }

    private static void Stop(string why)
    {
        if (_stage == Stage.Off) return;
        Plugin.L.LogInfo($"[explore] stop after {_runs} run(s): {why}");
        _stopReason = $"连续探索结束 ({_runs} 次): {why}";
        _msgUntil = Time.unscaledTime + 6f;
        _stage = Stage.Off;
        _reopened = null;
        _stopRequested = false;
    }

    /// <summary>Reason the next run can't start, or null.</summary>
    private static string CantRun()
    {
        var p = P;
        if (p == null || _course == null || _area == null) return "状态丢失";
        if (MaxRuns.Value > 0 && _runs >= MaxRuns.Value) return "达到次数上限";
        if (_course.infinity_ == 1 && _course.IsClear()) return "该迷宫只能通关一次";
        int cost = _course.GetCost();
        if (p.GetStamina() < cost) return $"体力不足 ({p.GetStamina()} < {cost})";
        var force = p.GetSearceForce(false);
        if (force == null || force.Size() == 0) return "没有出战队伍";
        return null;
    }

    internal static void Tick()
    {
        if (_stage == Stage.Off) return;
        var p = P;
        if (p == null) { Stop("存档未载入"); return; }
        bool inDungeon = p.searchCourse_ != null;
        switch (_stage)
        {
            case Stage.Starting:
            case Stage.Reopening:
                if (inDungeon)
                {
                    _runs++;
                    _sawClear = false;
                    _stage = Stage.Running;
                    _reopened = null;
                    Plugin.L.LogInfo($"[explore] run {_runs} started: {_course.name_} (stamina left {p.GetStamina()})");
                }
                else if (++_wait > 300) Stop("没有开始探索");   // ~15 s at 20 fps: the game refused (dialog etc.)
                break;
            case Stage.Running:
                if (!inDungeon)
                {
                    if (!_sawClear) { Stop("未通关"); break; }
                    if (_stopRequested) { Stop("手动停止"); break; }
                    _stage = Stage.WaitIdle;
                    _idle = 0;
                }
                break;
            case Stage.WaitIdle:
                var fm = FormManagerBase.GetInstance();
                var cur = fm?.Current;
                bool idle = cur != null && Same(fm.Top, cur) && cur.TryCast<GameForm>() != null;
                _idle = idle ? _idle + 1 : 0;
                if (_idle < IdleFrames.Value) break;
                string why = CantRun();
                if (why != null) { Stop(why); break; }
                _reopened = SubForm.CreateWharfQuest(_area);
                cur.Push(_reopened);
                _stage = Stage.Reopening;
                _wait = 0;
                break;
        }
    }

    private const int Dialog = 1;   // SubForm.TYPE_DIALOG: MakeDialog, e.g. "正在探险 <dungeon>" when the entrance is tapped mid-run

    private static void RequestStop()
    {
        // Mid-run: finish this run (still fast-forwarded) and stop at the port; otherwise stop now.
        if (_stage == Stage.Running) _stopRequested = true;
        else Stop("手动停止");
    }

    internal static void Draw()
    {
        if (!Plugin.LateReady) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        if (_stage != Stage.Off)
        {
            // Status label; a click on it also stops.
            string text = $"⟳ 连续探索 第{Math.Max(_runs, 1)}次 · 体力 {P?.GetStamina()}  " +
                          (_stopRequested ? "(本次结束后停止)" : "(点此停止)");
            var r = new Rect(Screen.width - 330, 54, 320, 24);
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            GUI.color = Color.white;
            if (GUI.Button(r, text, style)) RequestStop();

            // Stop button on the game's dialog (e.g. "正在探险" when the dungeon entrance is tapped mid-run).
            var top = TopSub();
            if (top != null && top.type_ == Dialog && P?.searchCourse_ != null && top.dialog_ == null)
            {
                if (SlotButton(_stopRequested ? "本次结束后\n停止" : "停止\n连续探索", !_stopRequested))
                    RequestStop();
            }
            return;
        }
        if (Time.unscaledTime < _msgUntil)
        {
            GUI.color = new Color(0, 0, 0, 0.6f);
            GUI.Label(new Rect(Screen.width - 469, 55, 460, 24), _stopReason, style);
            GUI.color = Color.white;
            GUI.Label(new Rect(Screen.width - 470, 54, 460, 24), _stopReason, style);
        }
        if (!Enabled.Value || !PickerOpen(out _, out var course, out _)) return;
        int cost = course.GetCost(), sta = P.GetStamina();
        int n = cost > 0 ? sta / cost : 0;
        if (SlotButton($"连续探索\n约 {n} 次", n > 0 && !_clickPending)) _clickPending = true;
    }

    /// <summary>Brown, gold-rimmed box like the game's 返回 button, in the empty slot bottom-left. True when clicked.</summary>
    private static bool SlotButton(string label, bool can)
    {
        // Same size as the game's corner plates (地图 / 返回): 193×96 px in a 1653×1150 window.
        float w = Screen.width * 0.1168f, h = Screen.height * 0.0835f;
        var rect = new Rect(Screen.width * ButtonX.Value - w / 2, Screen.height * ButtonY.Value - h / 2, w, h);
        Fill(rect, new Color(0.35f, 0.17f, 0.05f));
        Fill(new Rect(rect.x + 3, rect.y + 3, rect.width - 6, rect.height - 6), new Color(0.93f, 0.62f, 0.18f));
        var inner = new Rect(rect.x + 7, rect.y + 7, rect.width - 14, rect.height - 14);
        bool hover = can && rect.Contains(Event.current.mousePosition);
        Fill(inner, can ? (hover ? new Color(0.78f, 0.36f, 0.08f) : new Color(0.66f, 0.29f, 0.06f)) : new Color(0.35f, 0.25f, 0.2f));
        var ts = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.RoundToInt(h * 0.26f), fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter, wordWrap = false,
        };
        ts.normal.textColor = can ? Color.white : new Color(0.75f, 0.7f, 0.65f);
        GUI.Label(inner, label, ts);
        return can && GUI.Button(rect, GUIContent.none, GUIStyle.none);
    }

    private static Texture2D _white;
    private static void Fill(Rect r, Color c)
    {
        if (_white == null) { _white = new Texture2D(1, 1); _white.SetPixel(0, 0, Color.white); _white.Apply(); }
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, _white);
        GUI.color = old;
    }

    // Remembers the entrance list the picker belongs to, and answers our re-opened list with "yes".
    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPrefix]
    private static void UpdatePre(SubForm __instance)
    {
        // Departure walk (CreateDepartureEvent, pushed by ProWharfQuest right after GoSearchArea): ProDepartureEvent
        // only animates the crew walking off (OK = 4× speed); processStep_ 1 is its end: Pop + AppData.AutoSave.
        if (__instance.type_ == Departure && _stage != Stage.Off && __instance.processStep_ == 0)
        {
            __instance.processStep_ = 1;
            return;
        }
        if (__instance.type_ != WharfQuest) return;
        _lastWharf = __instance;
        if (_stage != Stage.Reopening || !Same(__instance, _reopened)) return;
        try
        {
            if (__instance.processStep_ != 0 || __instance.dialog_ != null) return;
            var list = __instance.ary1_;
            if (list == null || list.Size() == 0) return;
            int idx = -1;
            for (int i = 0; i < list.Size(); i++)
                if (Same(list.ElementAt(i), _course)) { idx = i; break; }
            if (idx < 0) { Stop("入口列表里找不到该迷宫"); __instance.Pop(); return; }
            __instance.select_ = idx;
            var yes = SubForm.MakeDialog("");
            yes.result_ = 0;
            __instance.dialog_ = yes;
            _reopened = null;   // answered once; the game's own start code runs in this Update
        }
        catch (Exception e) { Plugin.L.LogError($"[explore] reopen: {e}"); Stop("出错"); }
    }

    // Performs the picker's "decide" for our button, on the game's update, after the picker's own update.
    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPostfix]
    private static void UpdatePost(SubForm __instance)
    {
        if (__instance.type_ == QuestClear && _stage == Stage.Running) _sawClear = true;
        if (!_clickPending || __instance.type_ != EditQuestForce) return;
        _clickPending = false;
        try
        {
            if (!PickerOpen(out var picker, out var course, out _) || !Same(picker, __instance)) return;
            _area = _lastWharf.wharfArea_;
            _course = course;
            _runs = 0;
            _stopRequested = false;
            string why = CantRun();
            if (why != null) { _stopReason = why; _msgUntil = Time.unscaledTime + 6f; return; }
            Plugin.L.LogInfo($"[explore] start: {course.name_}, cost {course.GetCost()}, stamina {P.GetStamina()}");
            main.AppData.GetInstance().RemoveAllPopup();
            ForceEditData.GetInstance().Init();
            picker.result_ = 0;
            picker.Pop();
            _stage = Stage.Starting;
            _wait = 0;
        }
        catch (Exception e) { Plugin.L.LogError($"[explore] start: {e}"); _stage = Stage.Off; }
    }
}
