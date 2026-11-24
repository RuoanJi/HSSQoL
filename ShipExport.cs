using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using BepInEx;
using BepInEx.Configuration;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Writes the current ship (size level, both floors tile by tile, placed facilities with the game's own computed
/// values), every facility type with its level, job bedrooms/favourites/requirements, the crew and the base
/// floor layouts to BepInEx\hssqol-ship.json, for the standalone layout designer (designer/designer.html).
/// </summary>
internal static class ShipExport
{
    internal static ConfigEntry<KeyCode> Key;
    internal static ConfigEntry<bool> OnLoad;
    internal const int FormatVersion = 1;
    private static bool _loadDone;

    private static readonly JsonSerializerOptions Opt = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    internal static void Bind(ConfigFile cfg)
    {
        Key = cfg.Bind("Export", "ShipExportKey", KeyCode.F9,
            "Writes the ship layout, facility levels and related data to BepInEx\\hssqol-ship.json for the layout designer.");
        OnLoad = cfg.Bind("Export", "ExportOnLoad", false,
            "Also export once automatically after a save is loaded (when the crew are loaded).");
    }

    private static void Log(string s) => Plugin.L.LogInfo($"[export] {s}");

    internal static void Tick()
    {
        bool now = Input.GetKeyDown(Key.Value);
        if (!now && OnLoad.Value && !_loadDone)
        {
            var app = main.AppData.GetInstance();
            if (app?.humans_ != null && app.humans_.Length > 0 && (app.humans_[0]?.Size() ?? 0) > 0) { _loadDone = true; now = true; }
        }
        if (!now) return;
        try
        {
            var path = Path.Combine(Paths.BepInExRootPath, "hssqol-ship.json");
            File.WriteAllText(path, JsonSerializer.Serialize(Build(), Opt));
            AutoFeed.Show($"Ship exported to {path}");
        }
        catch (Exception e) { Plugin.L.LogError($"[export] failed: {e}"); AutoFeed.Show($"Ship export failed: {e.Message}"); }
    }

    private static T Try<T>(Func<T> f, T fallback = default) { try { return f(); } catch { return fallback; } }
    private static int[] A(Il2CppStructArray<int> a) { if (a == null) return null; var r = new int[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i]; return r; }
    private static int[][] AA(Il2CppReferenceArray<Il2CppStructArray<int>> a) { if (a == null) return null; var r = new int[a.Length][]; for (int i = 0; i < a.Length; i++) r[i] = A(a[i]); return r; }
    private static string[] S(Il2CppStringArray a) { if (a == null) return null; var r = new string[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i]; return r; }

    private static Dictionary<string, object> Build()
    {
        var app = main.AppData.GetInstance();
        var p = app.player_;

        // Facility types (static data + this save's level per type).
        var models = new List<Dictionary<string, object>>();
        var ms = app.shipInChipModel_;
        for (int i = 0; i < ms.Length; i++)
        {
            var m = ms[i];
            if (m == null) continue;
            models.Add(new Dictionary<string, object>
            {
                ["id"] = m.id_, ["name"] = m.name_, ["type"] = m.type_, ["category"] = Try(() => m.Category(), -1),
                ["w"] = Try(() => m.GetWidthNum(), 1), ["h"] = Try(() => m.GetHeightNum(), 1),
                ["selectable"] = m.selectNone_ == 1, ["visit"] = m.visitNone_ == 1, ["canLevel"] = Try(() => m.IsLvup()),
                ["paramType"] = m.paramType_, ["paramValue"] = m.paramValue_,
                ["areaEffect"] = AA(m.areaEffect_), ["exEffect"] = A(m.exEffect_), ["getKnow"] = A(m.getKnow_),
                ["getKnowTime"] = m.getKnowTime_, ["sortID"] = m.sortID_,
                ["lv"] = Try(() => m.GetLv(), 1), ["owned"] = Try(() => m.GetNum(), 0), ["found"] = m.find_ == 0,
            });
        }

        // Current floors.
        var floors = new List<Dictionary<string, object>>();
        var maps = p.currentShip_?.ship_?.inMap_;
        for (int f = 0; maps != null && f < maps.Length; f++)
        {
            var map = maps[f];
            var grid = map?.mapchips_;
            if (grid == null) continue;
            var tiles = new List<int[]>();       // [ax, ay, tile modelID, building modelID or -1, noRoad]
            var pieces = new List<Dictionary<string, object>>();
            for (int i = 0; i < grid.Length; i++)
            {
                var row = grid[i];
                for (int j = 0; row != null && j < row.Length; j++)
                {
                    var c = row[j];
                    if (c == null) continue;
                    var b = c.building_;
                    tiles.Add(new[] { (int)c.GetAX(), (int)c.GetAY(), c.modelID_, b?.modelID_ ?? -1, c.noRoad_ });
                    if (b != null && Try(() => c.IsBuildParentChip()))
                        pieces.Add(new Dictionary<string, object>
                        {
                            ["x"] = (int)c.GetAX(), ["y"] = (int)c.GetAY(), ["id"] = b.modelID_,
                            ["building"] = Try(() => b.IsBuild()),
                            // The game's own values, so the designer can check its model against them.
                            ["game"] = new Dictionary<string, object>
                            {
                                ["paramAdd"] = b.paramAdd_, ["exAdd"] = A(b.exAddParam_),
                                ["paramValue"] = Try(() => b.GetParamValue(true), -1), ["know"] = Try(() => A(b.GetKnowValue())),
                                ["noRoad"] = c.noRoad_,
                            },
                        });
                }
            }
            floors.Add(new Dictionary<string, object>
            {
                ["rows"] = grid.Length, ["cols"] = grid.Length > 0 && grid[0] != null ? grid[0].Length : 0,
                ["tiles"] = tiles, ["pieces"] = pieces,
            });
        }

        // Base (extension 0) floor layouts from the map data, for the other ship levels.
        var templates = new List<Dictionary<string, object>>();
        var sd = app.shipList_ != null && app.shipList_.Length > 0 ? app.shipList_[0] : null;
        var sim = sd?.shipInMap_;
        for (int f = 0; sim != null && f < sim.Length; f++)
        {
            int fl = f, mapId = sim[f];
            var m0 = Try(() => game.map.ShipInMap.Create(mapId, fl));
            var g0 = m0?.mapchips_;
            if (g0 == null) continue;
            var cells = new List<int[]>();
            for (int i = 0; i < g0.Length; i++)
            {
                var r = g0[i];
                for (int j = 0; r != null && j < r.Length; j++)
                    if (r[j] != null) cells.Add(new[] { (int)r[j].GetAX(), (int)r[j].GetAY(), r[j].modelID_, r[j].building_?.modelID_ ?? -1 });
            }
            templates.Add(new Dictionary<string, object> { ["rows"] = g0.Length, ["cols"] = g0.Length > 0 && g0[0] != null ? g0[0].Length : 0, ["cells"] = cells });
        }

        // Jobs: bedroom (index into facility types; -1 = base room), favourite facilities, knowledge requirements.
        var jobs = new List<Dictionary<string, object>>();
        var jl = app.jobList_;
        for (int i = 0; i < jl.Length; i++)
        {
            var j = jl[i];
            if (j == null) continue;
            jobs.Add(new Dictionary<string, object>
            {
                ["id"] = j.id_, ["name"] = j.name_, ["bedRoom"] = Try(() => j.GetBedRoomModel()?.id_ ?? -1, -1),
                ["goodChips"] = A(j.goodChips_), ["require"] = AA(j.jobChange_), ["medal"] = j.medalNum_, ["masterNum"] = j.masterNum_,
            });
        }

        // Crew (AppData.humans_[0] is the crew list the game's own crew screen uses).
        var crew = new List<Dictionary<string, object>>();
        var list = app.humans_ != null && app.humans_.Length > 0 ? app.humans_[0] : null;
        for (int i = 0; list != null && i < list.Size(); i++)
        {
            var h = list.Get(i)?.TryCast<game.Human>();
            var c = h?.crew_?.TryCast<game.Character>();
            if (c == null) continue;
            var house = Try(() => h.GetHouseChip());
            crew.Add(new Dictionary<string, object>
            {
                ["name"] = Try(() => c.GetName()), ["job"] = c.jobID_,
                ["house"] = house == null ? null : new[] { (int)house.level_, (int)house.GetAX(), (int)house.GetAY() },
                ["knowLv"] = A(c.knowLv_), ["wisdom"] = Try(() => c.GetParam(5), 0),
                ["skill26"] = Try(() => c.GetSetSkill(26)?.value_ ?? 0, 0),
            });
        }

        return new Dictionary<string, object>
        {
            ["format"] = "hssqol-ship", ["version"] = FormatVersion, ["exported"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            ["mod"] = Plugin.Version,
            ["shipLevel"] = Try(() => p.shipExtensionNum_.Get(), -1),
            ["shipLevels"] = sd == null ? null : new Dictionary<string, object> { ["maxCrew"] = A(sd.maxCrew_), ["maxParties"] = A(sd.maxForceNum_) },
            ["baseRoom"] = Try(() => data.ShipInChipModel.BASE_ROOM_ID, 4),
            ["paramNames"] = Try(() => S(data.ShipInChipModel.PARAM_LIST)),
            ["knowNames"] = Try(() => S(main.AppData.KNOW_STR_LIST)),
            ["models"] = models, ["floors"] = floors, ["baseFloors"] = templates, ["jobs"] = jobs, ["crew"] = crew,
        };
    }
}
