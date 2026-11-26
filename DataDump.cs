using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Data-mining helper: on a key press, writes the game's master data tables (AppData.*List_) to
/// BepInEx\hssqol-data.json by reflecting every game field (names ending in '_') of each record.
/// </summary>
internal static class DataDump
{
    internal static ConfigEntry<KeyCode> Key;
    internal static ConfigEntry<bool> OnBoot;
    private static bool _autoDone;

    private static readonly string[] Tables =
    {
        "itemElementList_", "itemList_", "equipList_", "enemyModelList_", "enemyGroupList_", "courseDataList_",
        "landAreaList_", "landList_", "dropItemList_", "treasureList_", "searchEventList_", "wharfAreaList_",
        "cityList_", "jobList_", "charList_", "skillList_", "prizeList_", "merchantList_", "medalList_",
        "sugorokuList_", "sugorokuItem_", "cannonList_", "wharfBlockList_", "wharfHideList_", "wharfChipModelList_",
    };

    internal static void Bind(ConfigFile cfg)
    {
        Key = cfg.Bind("Export", "DataDumpKey", KeyCode.F10,
            "Writes the game's master data tables to BepInEx\\hssqol-data.json (data mining).");
        OnBoot = cfg.Bind("Export", "DataDumpOnBoot", false,
            "Also write the data dump once automatically as soon as the game data has loaded (no save needed).");
    }

    internal static void Tick()
    {
        bool now = Input.GetKeyDown(Key.Value);
        main.AppData app = null;
        if (!now && OnBoot.Value && !_autoDone && Time.frameCount % 20 == 0)
        {
            try { app = main.AppData.GetInstance(); } catch { }
            if (app?.courseDataList_ != null && app.courseDataList_.Length > 0 && app.itemElementList_ != null
                && app.enemyModelList_ != null && app.enemyModelList_.Length > 0) { _autoDone = true; now = true; }
        }
        if (!now) return;
        try
        {
            app ??= main.AppData.GetInstance();
            var root = new Dictionary<string, object>();
            foreach (var t in Tables)
            {
                try { root[t] = Conv(Get(app, t), 0); }
                catch (Exception e) { root[t] = "ERR " + e.Message; }
            }
            var extra = new Dictionary<string, object>();
            T(extra, "RANK_LIST", () => Conv(data.CourseData.RANK_LIST, 0));
            T(extra, "EGG_RANK_LIST", () => Conv(data.CourseData.EGG_RANK_LIST, 0));
            T(extra, "rankByLv", () => { var r = new List<int>(); for (int lv = 0; lv <= 300; lv++) r.Add(data.CourseData.GetRank(lv)); return r; });
            T(extra, "landAreaNames", () => { var r = new List<string>(); foreach (var a in app.landAreaList_) r.Add(Try(() => a.GetNameA(true))); return r; });
            T(extra, "landAreaNamesNoLv", () => { var r = new List<string>(); foreach (var a in app.landAreaList_) r.Add(Try(() => a.GetNameA(false))); return r; });
            T(extra, "courseRank", () => { var r = new List<int>(); foreach (var c in app.courseDataList_) r.Add(Try(() => c.GetRank(), -99)); return r; });
            T(extra, "courseNightmare", () => { var r = new List<bool>(); foreach (var c in app.courseDataList_) r.Add(Try(() => c.IsNightmare())); return r; });
            T(extra, "itemElementLength", () => { var r = new List<int>(); foreach (var c in app.itemElementList_) r.Add(c.GetLength()); return r; });
            root["extra"] = extra;
            root["freeBattle"] = FreeBattle(app);
            var path = Path.Combine(Paths.BepInExRootPath, "hssqol-data.json");
            File.WriteAllText(path, JsonSerializer.Serialize(root,
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            AutoFeed.Show($"Data dumped to {path}");
            Plugin.L.LogInfo($"[dump] wrote {path}");
        }
        catch (Exception e) { Plugin.L.LogError($"[dump] failed: {e}"); }
    }

    /// <summary>
    /// 自由对战 (Net対戦) opponent pool: the bundled fleet table loaded at boot (file / rank tier / name),
    /// the shuffled queue of pool indexes still to fight, the last-fought index (save_.ints[27]),
    /// the player's Net pirate rank, and the currently loaded opponent's force.
    /// </summary>
    private static Dictionary<string, object> FreeBattle(main.AppData app)
    {
        var d = new Dictionary<string, object>();
        T(d, "fileName", () => Conv(app.freeBattleDataFileName_, 0));
        T(d, "rank", () => Conv(app.freeBattleDataRank_, 0));
        T(d, "userName", () => Conv(app.freeBattleDataUserName_, 0));
        T(d, "queue", () =>
        {
            var q = app.freeBattleList_; var r = new List<int>();
            if (q != null) for (int i = 0; i < q.Size(); i++) r.Add(q.ElementAt(i));
            return r;
        });
        T(d, "lastIndex", () => app.save_.ints[27]);
        T(d, "netManager", () =>
        {
            var m = app.player_?.netManager_;
            if (m == null) return null;
            return new Dictionary<string, object>
            {
                ["netBattleRank_"] = m.netBattleRank_, ["netBattlePrevRank_"] = m.netBattlePrevRank_,
                ["netBattleExp_"] = m.netBattleExp_, ["netBattleState_"] = m.netBattleState_,
                ["netBattleStep_"] = m.netBattleStep_, ["localBattleWin_"] = m.localBattleWin_,
                ["localBattleLose_"] = m.localBattleLose_, ["freeRankTier"] = game.NetBattleManager.GetFreeBattleRank(m.netBattleRank_),
            };
        });
        T(d, "netShipChips", () =>
        {
            var v = app.player_?.landMap_?.netBattlePos_; var r = new List<object>();
            if (v != null) for (int i = 0; i < v.Size(); i++) { var c = v.ElementAt(i); r.Add(new[] { c.GetAX(), c.GetAY() }); }
            return r;
        });
        T(d, "current", () =>
        {
            var nb = app.netBattleData_;
            if (nb == null) return null;
            return new Dictionary<string, object>
            {
                ["name_"] = Try(() => (string)nb.name_), ["rank_"] = nb.rank_,
                ["effective"] = Try(() => nb.IsEffectiveData()),
            };
        });
        T(d, "fleets", () => Fleets(app));
        return d;
    }

    /// <summary>
    /// Every bundled fleet, read the way SubForm.NetBattleThreadProcess does: resource record 33 is a jar;
    /// entry (prefix + fileName) = 1 unsigned byte, then a length-prefixed block that NetBattleData.Decompress
    /// turns into forceData_; CreateNetForce builds the crew and monsters.
    /// </summary>
    private static List<object> Fleets(main.AppData app)
    {
        var jar = new kairo.unity.util.JarInflater(kairo.unity.io.RecordStore.ReadRecord(1, 33));
        var entries = new List<string>();
        foreach (var s in jar.GetFiles()) entries.Add(s);
        var fleets = new List<object>();
        for (int i = 0; i < app.freeBattleDataFileName_.Length; i++)
        {
            string file = app.freeBattleDataFileName_[i];
            var r = new Dictionary<string, object>
            {
                ["index"] = i, ["file"] = file, ["rank"] = app.freeBattleDataRank_[i], ["userName"] = (string)app.freeBattleDataUserName_[i],
            };
            try
            {
                string entry = entries.Find(e => e == file || e.EndsWith("/" + file) || e.EndsWith(file))
                               ?? entries.Find(e => e.Contains(file));
                if (entry == null) { r["error"] = "no jar entry"; fleets.Add(r); continue; }
                r["entry"] = entry;
                var s = new java.io.ByteArrayInputStream(jar.GetData(entry));
                r["header"] = (int)kairo.unity.util.StreamUtil.ReadUnsignedByte(s);
                var nb = new game.NetBattleData();
                nb.forceData_ = nb.Decompress(kairo.unity.util.StreamUtil.Read(s));
                var f = nb.CreateNetForce();
                r["cannonID"] = f.cannonID_;
                r["lineIDs"] = Conv(f.freeForceLineIDs_, 0);
                var hs = new List<object>(); var ms = new List<object>();
                if (f.human_ != null) for (int k = 0; k < f.human_.Size(); k++) hs.Add(Member(f.human_.ElementAt(k)));
                if (f.monster_ != null) for (int k = 0; k < f.monster_.Size(); k++) ms.Add(Member(f.monster_.ElementAt(k)));
                r["humans"] = hs; r["monsters"] = ms;
            }
            catch (Exception e) { r["error"] = e.GetType().Name + ": " + e.Message; }
            fleets.Add(r);
        }
        return fleets;
    }

    private static Dictionary<string, object> Member(game.Crew c)
    {
        var m = new Dictionary<string, object>
        {
            ["name"] = Try(() => c.GetName()), ["modelID"] = c.modelID_, ["lv"] = c.lv_, ["racialLv"] = c.racialLv_,
            ["hpMax"] = Try(() => c.GetHPMax(), -1), ["spMax"] = Try(() => c.GetMPMax(), -1),
            ["param"] = Try(() => { var p = new List<int>(); for (int t = 0; t < 6; t++) p.Add(c.GetParam(t)); return p; }),
            ["addParam"] = Conv(c.addParam_, 0), ["netAddParam"] = Conv(c.netBattleAddParam_, 0),
        };
        var ch = c.TryCast<game.Character>();
        if (ch != null)
        {
            m["jobID"] = ch.jobID_; m["birthJobID"] = ch.birthJobID_; m["gender"] = ch.gender_;
            m["jobName"] = Try(() => ch.GetJobName()); m["readerLv"] = Try(() => ch.GetReaderLv(), -1);
            m["jobMasterAllCount"] = ch.jobMasterAllCount_; m["equip"] = Conv(ch.equip_, 0);
            m["skills"] = Try(() => { var r = new List<int>(); for (int k = 0; k < ch.GetSkillSize(); k++) r.Add(ch.GetSkill(k)?.id_ ?? -1); return r; });
        }
        var mo = c.TryCast<game.Monster>();
        if (mo != null)
        {
            m["evolutionCount"] = mo.evolutionCount_; m["finalEvo"] = mo.finalEvoFlag_; m["nightmare"] = mo.nightmareFlag_;
            m["skills"] = Try(() => { var r = new List<int>(); foreach (var s in mo.GetSkillList()) r.Add(s?.id_ ?? -1); return r; });
        }
        return m;
    }

    private static void T(Dictionary<string, object> d, string k, Func<object> f)
    { try { d[k] = f(); } catch (Exception e) { d[k] = "ERR " + e.Message; } }
    private static R Try<R>(Func<R> f, R fb = default) { try { return f(); } catch { return fb; } }

    private const BindingFlags BF = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static object Get(object o, string name)
    {
        var p = o.GetType().GetProperty(name, BF);
        if (p != null) return p.GetValue(o);
        var f = o.GetType().GetField(name, BF);
        return f?.GetValue(o);
    }

    private static object Conv(object v, int depth)
    {
        if (v == null) return null;
        if (depth > 8) return "<deep>";
        switch (v)
        {
            case string s: return s;
            case int or long or short or sbyte or byte or bool or float or double: return v;
            case Il2CppSystem.String s2: return (string)s2;
            case ext.util.ExInt ei: return ei.Get();
            case ext.util.ExLong el: return el.Get();
        }
        var t = v.GetType();
        // Il2CppStructArray<T> / Il2CppReferenceArray<T> / Il2CppStringArray all implement IEnumerable.
        if (t.Name.StartsWith("Il2Cpp") && t.Name.Contains("Array") && v is IEnumerable en)
        {
            var list = new List<object>();
            foreach (var e in en) list.Add(Conv(e, depth + 1));
            return list;
        }
        var ns = t.Namespace ?? "";
        if (ns.StartsWith("data") || ns.StartsWith("game"))
        {
            var d = new Dictionary<string, object>();
            for (var tt = t; tt != null && tt != typeof(Il2CppSystem.Object) && tt != typeof(object); tt = tt.BaseType)
            {
                foreach (var p in tt.GetProperties(BF | BindingFlags.DeclaredOnly))
                {
                    if (!p.Name.EndsWith("_") || p.GetIndexParameters().Length > 0 || d.ContainsKey(p.Name)) continue;
                    try { d[p.Name] = Conv(p.GetValue(v), depth + 1); } catch (Exception e) { d[p.Name] = "ERR " + e.GetType().Name; }
                }
            }
            return d;
        }
        return "<" + t.Name + ">";
    }
}
