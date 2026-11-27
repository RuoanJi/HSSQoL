using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using game;
using HarmonyLib;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Knowledge gain statistics for combat crews (知识获取, toggle the panel with F8).
///
/// Knowledge is Crew.knowLv_[7] (max 20) and knowExp_[7]; a level needs (lv + 1) × 1000 EXP, so the lifetime
/// points are 1000 × lv × (lv + 1) / 2 + exp. Every gain goes through Crew.AddKnowExp(type, value) (ISIL: returns at
/// once if IsKnowLvMax(type), else adds 1 EXP value times, levelling up at (lv + 1) × 1000 and clamping at 20).
/// Its callers (ISIL) are Human.VisitAction (using a ship facility), DeckFighter.AddKonwExp and
/// SearchFighter.AddKonwExp (battles). Those get a prefix that sets the source, and AddKnowExp's postfix records the
/// real gain (points after − before; nothing is gained for a type already at 20).
///
/// A facility visit drips its knowledge over 20 frames (Graph.Easing(0, total, 20, frame) − the previous frame's
/// value, one AddKnowExp per frame), so a "use" is counted only when a crew's gain from a source isn't a direct
/// continuation of the previous frames. Battles: DeckFighter/SearchFighter.AddKonwExp(exp) loop over the party and
/// give every living member with a weapon 远程 (weapon GetAttackRange 2 or 4) or 近战 (anything else) knowledge.
///
/// Only crews currently in a combat party (player_.force_: a Vector of parties, each a Vector of Human → crew_) are
/// counted. A per-frame-ish check also catches gains that bypass AddKnowExp (shown as 其他); drops (switching job
/// spends knowledge) only move the baseline.
///
/// In memory only, fixed size: totals per crew × type and per source, nothing per event.
/// </summary>
internal static class KnowLog
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<KeyCode> PanelKey;

    internal static readonly string[] KnowNames = { "近战", "远程", "海员", "探究心", "工作心", "勇敢战士", "秘传" };
    private const int Types = 7, MaxLv = 20;

    private class CrewStat
    {
        public string Name = "";
        public int Party, Slot;
        public bool InParty;
        public Crew Crew;
        public readonly long[] Gain = new long[Types];
        public readonly long[] Last = new long[Types];   // lifetime points at the last check
        public DateTime Since;                            // tracked since (session start or joining a party)
        public int LastFrame = -1000;                     // frame and source of the last gain, to count uses
        public string LastSource;
    }

    private class SourceStat { public long Gain; public int Count; }

    private const int UseGapFrames = 10; // a facility's 20-frame drip arrives every frame; a new use comes later

    private static readonly Dictionary<IntPtr, CrewStat> _crews = new();
    private static readonly Dictionary<string, SourceStat> _sources = new();
    private static string _context;          // source set by a caller prefix while it runs
    private static int _depth;
    private static long _before;
    private static IntPtr _player;
    private static int _frame;
    private static DateTime _start = DateTime.Now;
    private static bool _show;
    private static Vector2 _scroll;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("KnowLog", "Enabled", true,
            "Records knowledge gained by crews in a combat party, with its source (facility, battle), for the F8 panel.");
        PanelKey = cfg.Bind("KnowLog", "PanelKey", KeyCode.F8, "Shows or hides the knowledge gain panel.");
    }

    /// <summary>Called from Plugin.PatchLate.</summary>
    internal static void Patch(Harmony h)
    {
        if (!Enabled.Value) return;
        void P(Type t, string name, string prefix, string postfix)
        {
            var ms = AccessTools.GetDeclaredMethods(t).Where(m => m.Name == name).ToList();
            if (ms.Count == 0) { Plugin.L.LogWarning($"[know] {t.Name}.{name} not found"); return; }
            foreach (var m in ms)
            {
                try
                {
                    h.Patch(m, prefix: prefix == null ? null : new HarmonyMethod(typeof(KnowLog), prefix),
                        postfix: postfix == null ? null : new HarmonyMethod(typeof(KnowLog), postfix));
                }
                catch (Exception e) { Plugin.L.LogError($"[know] patching {t.Name}.{name} failed: {e.Message}"); }
            }
        }
        P(typeof(Crew), nameof(Crew.AddKnowExp), nameof(AddPrefix), nameof(AddPostfix));
        P(typeof(Human), "VisitAction", nameof(VisitPrefix), nameof(PopContext));
        P(typeof(DeckFighter), "AddKonwExp", nameof(DeckPrefix), nameof(PopContext));
        P(typeof(SearchFighter), "AddKonwExp", nameof(SearchPrefix), nameof(PopContext));
    }

    private static void VisitPrefix(Human __instance)
    {
        string fac = "?";
        try
        {
            int id = __instance.visitModelID_;
            if (!MoneyLog.FacilityNames.TryGetValue(id, out fac))
            {
                var models = main.AppData.GetInstance()?.shipInChipModel_;
                fac = models != null && id >= 0 && id < models.Length ? models[id]?.name_ ?? $"#{id}" : $"#{id}";
            }
        }
        catch { }
        Push($"设施·{fac}");
    }

    private static void DeckPrefix() => Push("战斗·甲板战");
    private static void SearchPrefix() => Push("战斗·迷宫");

    private static void Push(string s) { if (_depth++ == 0) _context = s; }
    private static void PopContext() { if (_depth > 0 && --_depth == 0) _context = null; }

    private static long Points(Crew c, int type)
    {
        int lv = c.knowLv_[type], exp = c.knowExp_[type];
        return 1000L * lv * (lv + 1) / 2 + exp;
    }

    private static void AddPrefix(Crew __instance, int type)
    {
        try { _before = type >= 0 && type < Types ? Points(__instance, type) : -1; } catch { _before = -1; }
    }

    private static void AddPostfix(Crew __instance, int type, int value)
    {
        try
        {
            if (_before < 0 || value <= 0) return;
            if (!_crews.TryGetValue(__instance.Pointer, out var cs) || !cs.InParty) return;
            long after = Points(__instance, type);
            cs.Last[type] = after;
            string src = _context ?? "其他";
            int f = Time.frameCount;
            bool newUse = src != cs.LastSource || f - cs.LastFrame > UseGapFrames;
            cs.LastSource = src;
            cs.LastFrame = f;
            Record(cs, type, Math.Max(0, after - _before), src, newUse);
        }
        catch (Exception e) { Plugin.L.LogError($"[know] {e.Message}"); }
    }

    private static void Record(CrewStat cs, int type, long gain, string source, bool newUse)
    {
        cs.Gain[type] += gain;
        if (!_sources.TryGetValue(source, out var s)) _sources[source] = s = new SourceStat();
        s.Gain += gain;
        if (newUse) s.Count++;
    }

    internal static void Tick()
    {
        if (Input.GetKeyDown(PanelKey.Value)) _show = !_show;
        _depth = 0; _context = null; // no caller is mid-call between frames
        if (!Enabled.Value || !Plugin.LateReady) return;
        if (++_frame % 10 != 0) return;
        var p = main.AppData.GetInstance()?.player_;
        if (p == null) return;
        if (p.Pointer != _player)
        {
            // Another save: its crews are different objects.
            if (_player != IntPtr.Zero) Reset();
            _player = p.Pointer;
        }
        try { Refresh(p); }
        catch (Exception e) { Plugin.L.LogError($"[know] refresh: {e.Message}"); }
    }

    /// <summary>Finds the current party members and catches gains made outside AddKnowExp.</summary>
    private static void Refresh(Player p)
    {
        foreach (var cs in _crews.Values) cs.InParty = false;
        var parties = p.force_;
        int n = parties?.Size() ?? 0;
        for (int pi = 0; pi < n; pi++)
        {
            var members = parties.Get(pi)?.TryCast<java.util.Vector>();
            int m = members?.Size() ?? 0;
            for (int si = 0; si < m; si++)
            {
                var crew = members.Get(si)?.TryCast<Human>()?.crew_;
                if (crew == null) continue;
                bool isNew = !_crews.TryGetValue(crew.Pointer, out var cs);
                if (isNew) _crews[crew.Pointer] = cs = new CrewStat { Since = DateTime.Now };
                bool joined = isNew || cs.Crew == null;
                cs.Crew = crew;
                cs.InParty = true;
                cs.Party = pi;
                cs.Slot = si;
                try { cs.Name = crew.GetName() ?? ""; } catch { }
                for (int t = 0; t < Types; t++)
                {
                    long now = Points(crew, t);
                    if (!joined && now > cs.Last[t]) Record(cs, t, now - cs.Last[t], "其他", true);
                    cs.Last[t] = now;
                }
            }
        }
        // Left the party: stop tracking (re-joining starts a fresh baseline but keeps the totals).
        foreach (var cs in _crews.Values) if (!cs.InParty) cs.Crew = null;
    }

    private static void Reset()
    {
        _crews.Clear();
        _sources.Clear();
        _start = DateTime.Now;
    }

    // ---------------------------------------------------------------- panel

    private static GUIStyle _box, _head, _cell, _num, _pos, _dim, _warn;
    private static Texture2D _bg;

    private static void Styles()
    {
        if (_box != null) return;
        _bg = new Texture2D(1, 1);
        _bg.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.12f, 0.92f));
        _bg.Apply();
        _box = new GUIStyle(GUI.skin.box);
        _box.normal.background = _bg;
        _head = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        _head.normal.textColor = Color.white;
        _cell = new GUIStyle(GUI.skin.label) { fontSize = 14, clipping = TextClipping.Clip };
        _cell.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        _num = new GUIStyle(_cell) { alignment = TextAnchor.UpperRight };
        _pos = new GUIStyle(_num);
        _pos.normal.textColor = new Color(0.45f, 0.9f, 0.5f);
        _dim = new GUIStyle(_num) { fontSize = 12 };
        _dim.normal.textColor = new Color(0.6f, 0.65f, 0.7f);
        _warn = new GUIStyle(_num);
        _warn.normal.textColor = new Color(1f, 0.75f, 0.35f);
    }

    internal static void Draw()
    {
        if (!_show) return;
        Styles();
        float w = Math.Min(1060, Screen.width - 20), h = Math.Min(700, Screen.height - 20);
        var r = new Rect(Screen.width - w - 10, 10, w, h);
        GUI.Box(r, GUIContent.none, _box);
        float x = r.x + 12, y = r.y + 8, iw = w - 24;

        GUI.Label(new Rect(x, y, iw - 100, 24), $"战斗队员知识获取 ({PanelKey.Value} 关闭)", _head);
        if (GUI.Button(new Rect(x + iw - 80, y, 80, 24), "重置")) Reset();
        y += 28;
        var span = DateTime.Now - _start;
        GUI.Label(new Rect(x, y, iw, 22), $"自 {_start:MM-dd HH:mm}，{FormatSpan(span)}。每格：本次获得，下行为当前等级 · 每小时获得（按加入队伍后的时间算）。", _cell);
        y += 26;

        // Crew table: name | 7 types | total
        const float nameW = 130, colW = 112, totW = 120;
        float cx(int i) => nameW + i * colW;
        float totX = cx(Types);
        GUI.Label(new Rect(x, y, nameW, 22), "队员", _head);
        for (int t = 0; t < Types; t++) GUI.Label(new Rect(x + cx(t), y, colW, 22), KnowNames[t], _num);
        GUI.Label(new Rect(x + totX, y, totW, 22), "合计", _num);
        y += 24;

        var members = _crews.Values.Where(c => c.InParty && c.Crew != null).OrderBy(c => c.Party).ThenBy(c => c.Slot).ToList();
        double Hours(CrewStat c) => Math.Max((DateTime.Now - (c.Since > _start ? c.Since : _start)).TotalHours, 1.0 / 60);

        // All members together: per-type sum of each member's rate (members can join at different times).
        if (members.Count > 0)
        {
            var sum = new long[Types];
            var rate = new double[Types];
            foreach (var c in members)
                for (int t = 0; t < Types; t++) { sum[t] += c.Gain[t]; rate[t] += c.Gain[t] / Hours(c); }
            DrawRow(x, y, "全部队员", sum, rate, null, _head, cx, totX, colW, totW);
            y += 42;
        }

        float tableBottom = r.y + h * 0.66f;
        int lines = members.Count * 2 + members.Select(c => c.Party).Distinct().Count();
        _scroll = GUI.BeginScrollView(new Rect(x, y, iw, tableBottom - y), _scroll, new Rect(0, 0, iw - 20, Math.Max(lines, 1) * 21));
        float ty = 0;
        int lastParty = -1;
        foreach (var c in members)
        {
            if (c.Party != lastParty)
            {
                lastParty = c.Party;
                GUI.Label(new Rect(0, ty, 200, 20), $"第 {c.Party + 1} 队", _head);
                ty += 21;
            }
            double hrs = Hours(c);
            var rate = new double[Types];
            for (int t = 0; t < Types; t++) rate[t] = c.Gain[t] / hrs;
            int[] lv = null;
            try { var kl = c.Crew.knowLv_; lv = new int[Types]; for (int t = 0; t < Types; t++) lv[t] = kl[t]; } catch { lv = null; }
            DrawRow(0, ty, c.Name, c.Gain, rate, lv, _cell, cx, totX, colW, totW);
            ty += 42;
        }
        if (members.Count == 0) GUI.Label(new Rect(0, 0, iw, 20), "没有战斗队伍成员（或还没读档）。", _cell);
        GUI.EndScrollView();

        // Sources (only those that actually gave knowledge).
        y = tableBottom + 6;
        GUI.Label(new Rect(x, y, 200, 22), "来源", _head);
        GUI.Label(new Rect(x + 220, y, 110, 22), "获得", _num);
        GUI.Label(new Rect(x + 340, y, 80, 22), "占比", _num);
        GUI.Label(new Rect(x + 430, y, 100, 22), "每小时", _num);
        GUI.Label(new Rect(x + 540, y, 80, 22), "次数", _num);
        GUI.Label(new Rect(x + 630, y, 100, 22), "每次", _num);
        y += 24;
        var shown = _sources.Where(kv => kv.Value.Gain > 0).OrderByDescending(kv => kv.Value.Gain).ToList();
        long all = shown.Sum(kv => kv.Value.Gain);
        double sh = Math.Max(span.TotalHours, 1.0 / 60);
        foreach (var (name, s) in shown)
        {
            if (y > r.yMax - 28) { GUI.Label(new Rect(x, y, 300, 20), "…还有更多来源", _cell); break; }
            GUI.Label(new Rect(x, y, 210, 20), name, _cell);
            GUI.Label(new Rect(x + 220, y, 110, 20), $"+{s.Gain:N0}", _pos);
            GUI.Label(new Rect(x + 340, y, 80, 20), all > 0 ? $"{100.0 * s.Gain / all:0.#}%" : "", _num);
            GUI.Label(new Rect(x + 430, y, 100, 20), $"{s.Gain / sh:N0}", _num);
            GUI.Label(new Rect(x + 540, y, 80, 20), s.Count.ToString(), _num);
            GUI.Label(new Rect(x + 630, y, 100, 20), s.Count > 0 ? $"{(double)s.Gain / s.Count:N0}" : "", _num);
            y += 20;
        }
        if (shown.Count == 0) GUI.Label(new Rect(x, y, iw, 20), "还没有记录。", _cell);
    }

    /// <summary>Two lines: gain per type and total; then Lv (if given) and per-hour rate per type and in total.</summary>
    private static void DrawRow(float x, float y, string name, long[] gain, double[] rate, int[] lv, GUIStyle nameStyle,
        Func<int, float> cx, float totX, float colW, float totW)
    {
        GUI.Label(new Rect(x, y, 130, 20), name, nameStyle);
        for (int t = 0; t < Types; t++)
        {
            GUI.Label(new Rect(x + cx(t), y, colW, 20), gain[t] > 0 ? $"+{gain[t]:N0}" : "·", gain[t] > 0 ? _pos : _dim);
            string lvs = lv == null ? "" : lv[t] >= MaxLv ? "满 · " : $"Lv{lv[t]} · ";
            string rs = gain[t] > 0 ? $"{rate[t]:N0}/时" : lv == null ? "" : "—";
            GUI.Label(new Rect(x + cx(t), y + 20, colW, 18), lvs + rs, lv != null && lv[t] >= MaxLv ? _warn : _dim);
        }
        long total = gain.Sum();
        GUI.Label(new Rect(x + totX, y, totW, 20), total > 0 ? $"+{total:N0}" : "0", total > 0 ? _pos : _num);
        GUI.Label(new Rect(x + totX, y + 20, totW, 18), $"{rate.Sum():N0}/时", _dim);
    }

    private static string FormatSpan(TimeSpan s) =>
        s.TotalHours >= 1 ? $"{(int)s.TotalHours} 小时 {s.Minutes} 分" : $"{Math.Max(s.Minutes, 0)} 分 {s.Seconds} 秒";
}
