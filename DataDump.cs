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
            var path = Path.Combine(Paths.BepInExRootPath, "hssqol-data.json");
            File.WriteAllText(path, JsonSerializer.Serialize(root,
                new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            AutoFeed.Show($"Data dumped to {path}");
            Plugin.L.LogInfo($"[dump] wrote {path}");
        }
        catch (Exception e) { Plugin.L.LogError($"[dump] failed: {e}"); }
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
