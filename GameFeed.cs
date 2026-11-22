using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using data;
using form;
using game;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// 强化满级 through the game's own feeding: opens the game's team power-up window
/// (SubForm.CreateTeamAddExp, SubForm type 79) for one character, once per planned food, one after another.
///
/// From the ISIL dump: InitTeamGetExp (on open) sums GetAddExp over the count, rolls and applies the stat
/// gains with the tier cap, runs the skill check and uses up the items. ProTeamGetExp (per frame) animates the
/// EXP, waits for a click to close, and opens the Job Master window itself at max level. So EXP, stats and the
/// job clear are all done by the game; this class only plans the counts and chains the windows.
/// The window's character list holds game.Human objects (InitTeamGetExp casts each entry to Human).
/// </summary>
internal static class GameFeed
{
    private const int MaxRounds = 3;          // plan, then up to 2 top-ups if random EXP rolls fell short
    private const float WindowTimeout = 300f; // give up waiting for a window after 5 minutes

    private static Human _human;
    private static Character _char;
    private static SubForm _parent, _current;
    private static bool _seenAlive;
    private static float _openedAt;
    private static int _round, _startLv, _startExp, _windows;
    private static int[] _startStats;
    private static readonly Queue<(ItemData item, int count, string name)> _queue = new();
    private static readonly Dictionary<string, int> _fed = new();

    internal static bool Busy => _char != null;

    private static void Log(string s) => Plugin.L.LogInfo($"[autofeed] {s}");

    private static int StatCount() =>
        main.AppData.GetInstance().itemList_.FirstOrDefault(i => i?.type_ == ItemData.TYPE_POWER_UP)?.addParam_?.Length ?? 0;

    private static int[] Stats(Character c)
    {
        int n = StatCount();
        var s = new int[n];
        for (int i = 0; i < n; i++) s[i] = c.GetAddParam(i);
        return s;
    }

    internal static void Start(Human h, Character c, SubForm parent)
    {
        if (Busy) { AutoFeed.Show("AutoFeed: still feeding, close the current window first."); return; }
        if (AutoFeed.Need(c) == 0) { AutoFeed.Show($"AutoFeed: {c.GetName()} is already at max level for this job."); return; }

        _human = h; _char = c; _parent = parent;
        _round = 0; _windows = 0; _fed.Clear(); _queue.Clear();
        _startLv = c.GetLv(); _startExp = c.exp_; _startStats = Stats(c);
        Log($"GAME FEED START {AutoFeed.Describe(c)} need={AutoFeed.Need(c)} foodStats=[{string.Join(",", _startStats)}]");

        if (!Plan()) { Finish("no usable food"); return; }
        OpenNext();
    }

    /// <summary>Plans per-food counts with the same choice rule as Direct, simulating average EXP and expected stat gains.</summary>
    private static bool Plan()
    {
        _round++;
        var foods = AutoFeed.Foods(sample: true);
        var order = new List<AutoFeed.Food>();
        var counts = new Dictionary<int, int>();
        var simStats = AutoFeed.FoodStats(_char, foods.FirstOrDefault()?.StatMin.Length ?? 0);
        int sim = AutoFeed.Need(_char), bites = 0;
        while (sim > 0 && bites < AutoFeed.MaxBites.Value)
        {
            var f = AutoFeed.Choose(foods, sim, simStats, out _);
            if (f == null) break;
            if (!counts.ContainsKey(f.Id)) { counts[f.Id] = 0; order.Add(f); }
            counts[f.Id]++;
            AutoFeed.AddExpectedStats(f, simStats);
            f.Have--; sim -= (int)Math.Round(f.Avg); bites++;
        }
        foreach (var f in order) _queue.Enqueue((f.Item, counts[f.Id], f.Name));
        Log($"round {_round} plan for need {AutoFeed.Need(_char)}: " +
            (order.Count == 0 ? "nothing usable" : string.Join(", ", order.Select(f => $"'{f.Name}' x{counts[f.Id]} (~{f.Avg:0} exp)"))));
        return order.Count > 0;
    }

    private static void OpenNext()
    {
        while (true)
        {
            if (_queue.Count == 0)
            {
                if (AutoFeed.Need(_char) == 0) { Finish("reached max"); return; }
                if (_round >= MaxRounds || !Plan()) { Finish("stopped short"); return; }
            }
            var (item, count, name) = _queue.Dequeue();
            int allowed = Math.Min(count, item.GetNum() - AutoFeed.KeepAtLeast.Value);
            if (allowed <= 0) continue;

            var list = new java.util.Vector();
            list.AddElement(_human);
            var window = SubForm.CreateTeamAddExp(list, item, allowed);
            bool pushed = _parent.Push(window);
            _current = window; _seenAlive = false; _openedAt = Time.unscaledTime; _windows++;
            _fed[name] = _fed.GetValueOrDefault(name) + allowed;
            Log($"opened team power-up #{_windows}: '{name}' x{allowed} (Push returned {pushed}, have {item.GetNum()})");
            return;
        }
    }

    /// <summary>Called every frame: when the current window closes, opens the next one.</summary>
    internal static void Tick()
    {
        if (_current == null) return;
        try
        {
            bool alive = _current.IsAlive();
            if (alive) { _seenAlive = true; }
            else if (_seenAlive || Time.unscaledTime - _openedAt > 2f)
            {
                Log($"window #{_windows} closed: {AutoFeed.Describe(_char)} foodStats=[{string.Join(",", Stats(_char))}]");
                _current = null;
                OpenNext();
                return;
            }
            if (Time.unscaledTime - _openedAt > WindowTimeout) { Log("window never closed; giving up"); _current = null; Finish("timeout"); }
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"[autofeed] {e}");
            _current = null;
            Finish("error");
        }
    }

    private static void Finish(string why)
    {
        var c = _char;
        var end = Stats(c);
        string statDelta = string.Join(",", end.Select((v, i) => $"{v - (i < _startStats.Length ? _startStats[i] : 0):+0;-0;0}"));
        Log($"GAME FEED END ({why}) {AutoFeed.Describe(c)} windows={_windows} foodStats=[{string.Join(",", end)}] delta=[{statDelta}]");

        var sb = new StringBuilder();
        sb.AppendLine($"AutoFeed (game windows): {c.GetName()}  Lv {_startLv} -> {c.GetLv()}/{c.GetLvMax()}  ({why})");
        sb.AppendLine($"EXP {_startExp:N0} -> {c.exp_:N0}   food stat bonus change [{statDelta}]");
        foreach (var kv in _fed) sb.AppendLine($"  {kv.Key,-10} x{kv.Value,4}");
        AutoFeed.Show(sb.ToString().TrimEnd());

        _human = null; _char = null; _parent = null; _current = null; _queue.Clear();
    }
}
