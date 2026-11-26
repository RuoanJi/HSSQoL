using System;
using System.Collections.Generic;
using System.Text;
using form;
using game;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// "连续转职" on the job-change page: runs 转职满级 (JobSwitch.Step) again and again until it can't go on.
///
/// One step can leave game windows open: the Job Master window (it grants the job-clear bonus when closed, and
/// may push the All Job Master window), and with AutoFeed Method = Game the team power-up windows. So the chain
/// is driven per frame: it only takes the next step when the job-change page is the top form again and
/// GameFeed is idle. With JobMasterSkip (F4) on, the windows close by themselves; otherwise the player clicks
/// through them and the chain carries on after each one.
///
/// Stops when:
///   - no unmastered job can be switched to (knowledge, mastered count, medals, shield),
///   - the round is complete: the mastered list shrank (the game resets it once every job is mastered) or holds
///     every job,
///   - a feed stopped short of max level (out of food),
///   - the page was closed, or 连续转职 was pressed again.
/// At the end the game's own dialog (SubForm.MakeDialog) lists how many jobs were mastered and which.
/// A job counts as mastered when the mastered list changes at a settle point: it grew (the job master window
/// or the direct job clear added the current job) or shrank (the last job completed the round and the list reset).
/// </summary>
internal static class JobChain
{
    private const float StepTimeout = 600f;   // give up if a step's windows stay open this long

    private static SubForm _page;
    private static Character _char;
    private static Human _human;
    private static int _steps, _masteredBefore, _settleFrames;
    private static float _stepAt;
    private static readonly List<string> _masteredNames = new();
    private const int DialogLineChars = 18;   // CJK characters per dialog line for the job list

    internal static bool Active => _page != null;

    private static void Log(string s) => Plugin.L.LogInfo($"[jobchain] {s}");

    internal static void ButtonPressed(SubForm page, Character c, Human h)
    {
        if (Active) { Stop("stopped by the player", "已手动停止"); return; }
        if (c == null) { AutoFeed.Show("JobChain: couldn't tell which character this page is for."); return; }
        if (GameFeed.Busy) { AutoFeed.Show("JobChain: still feeding, wait for it to finish."); return; }
        _page = page; _char = c; _human = h; _steps = 0;
        _masteredNames.Clear();
        _masteredBefore = Mastered();
        _settleFrames = 0;
        _stepAt = Time.unscaledTime;
        Log($"start for {c.GetName()}: {AutoFeed.Describe(c)}");
    }

    private static int Mastered() => _char?.masterJob_?.Size() ?? 0;
    private static int JobCount() => main.AppData.GetInstance()?.jobList_?.Length ?? 0;

    /// <summary>Called every frame.</summary>
    internal static void Tick()
    {
        if (!Active) return;
        try { TickCore(); }
        catch (Exception e)
        {
            Plugin.L.LogError($"[jobchain] {e}");
            Stop("error", "出错，已停止");
        }
    }

    private static void TickCore()
    {
        if (!_page.IsAlive()) { Stop("the job-change page was closed", null); return; }

        var top = kairo.unity.form.FormManagerBase.GetInstance()?.Top;
        bool pageOnTop = top != null && top.Pointer == _page.Pointer;
        if (GameFeed.Busy || !pageOnTop)
        {
            _settleFrames = 0;
            if (Time.unscaledTime - _stepAt > StepTimeout) Stop("timed out waiting for a window to close", "等待窗口超时，已停止");
            return;
        }
        // Let the page run a couple of frames after a window closes before acting on it.
        if (++_settleFrames < 3) return;
        _settleFrames = 0;

        var c = _char;
        int mastered = Mastered(), jobs = JobCount();

        // Previous step's result: a change in the mastered list means the current job was just mastered.
        if (mastered != _masteredBefore)
        {
            string name = c.GetJob()?.name_ ?? $"#{c.jobID_}";
            _masteredNames.Add(name);
            Log($"mastered {name} (mastered list {_masteredBefore} -> {mastered})");
        }
        if (mastered < _masteredBefore)
        {
            Stop($"every job mastered (mastered list reset {_masteredBefore} -> {mastered})", $"全部 {jobs} 个职业已通关");
            return;
        }
        if ((_steps > 0 || mastered != _masteredBefore) && !c.IsJobLvMax())
        {
            Stop($"feed stopped short of max ({AutoFeed.Describe(c)})", "食物不足，新职业未能升到满级");
            return;
        }
        if (jobs > 0 && mastered >= jobs)
        {
            Stop($"every job mastered ({mastered}/{jobs})", $"全部 {jobs} 个职业已通关");
            return;
        }
        if (_steps > jobs * 2 + 4) { Stop("step cap reached", "达到步数上限"); return; }

        _masteredBefore = mastered;
        _stepAt = Time.unscaledTime;
        var r = JobSwitch.Step(_page, c, _human);
        Log($"step result {r}: {AutoFeed.Describe(c)}");
        switch (r)
        {
            case JobSwitch.StepResult.Switched:
                _steps++;
                break;
            case JobSwitch.StepResult.OpenedClearWindow:
                break;  // wait for the window, then try again
            case JobSwitch.StepResult.NoTarget:
                Stop("no job left to switch to", "没有可以转的未通关职业了");
                break;
            case JobSwitch.StepResult.NotMax:
                Stop("current job isn't at max level", "当前职业未满级，请先强化满级");
                break;
            default:
                Stop($"step failed ({r})", "出错，已停止");
                break;
        }
    }

    /// <summary>Ends the run. reason == null: no dialog (the page is gone).</summary>
    private static void Stop(string why, string reason)
    {
        var page = _page;
        var c = _char;
        Log($"stop after {_steps} switch(es), {_masteredNames.Count} mastered [{string.Join(", ", _masteredNames)}]: {why}");
        AutoFeed.Show($"JobChain: {why}; {_steps} switch(es), {_masteredNames.Count} mastered.");
        _page = null; _char = null; _human = null;

        if (reason == null || page == null) return;
        try
        {
            if (page.IsAlive()) page.Push(SubForm.MakeDialog(Summary(c, reason)));
        }
        catch (Exception e) { Plugin.L.LogError($"[jobchain] summary dialog: {e}"); }
    }

    private static string Summary(Character c, string reason)
    {
        var sb = new StringBuilder();
        sb.Append($"连续转职结束：{reason}\n");
        sb.Append($"{c?.GetName() ?? "?"} 本次通关 {_masteredNames.Count} 个职业");
        if (_masteredNames.Count == 0) return sb.ToString();
        sb.Append('\n');
        // Pack the names into lines so the dialog stays narrow.
        var line = new StringBuilder();
        foreach (var n in _masteredNames)
        {
            string item = line.Length == 0 ? n : "、" + n;
            if (line.Length > 0 && line.Length + item.Length > DialogLineChars)
            {
                sb.Append(line).Append('\n');
                line.Clear();
                item = n;
            }
            line.Append(item);
        }
        sb.Append(line);
        return sb.ToString();
    }
}
