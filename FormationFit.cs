using System;
using BepInEx.Configuration;
using game;
using HarmonyLib;

namespace HSSQoL;

/// <summary>
/// Keeps the 4th slot of each formation row visible in dungeon battles.
///
/// FieldBase.SetForcePosition (ISIL) places fighter i at row lineX_ = i / N and slot lineY_ (index within the
/// row). Battle y = baseY + extra − lineY_ × dy, or baseY + extra − dy/2 when the row holds a single fighter.
/// Battle y grows DOWNWARD on screen (SearchFighter.RegistZSort draws at the recorded y + ofsY), so slot 0 is
/// the bottom of the row. baseY/dy are 61/14 normally, 67/17 when SearchBattle.IsTripleLine() (battle-line
/// count ≥ 3, not a survey), and 83/18 when that count is ≥ 4. The 83 baseline pushes slot 0 too low, so with
/// 4 per row the bottom crew member (and enemy) of each full row is cut off. They still act; it's only drawing.
///
/// While SetForcePosition runs for a 4-per-row SearchBattle, this shifts the y given to
/// ScriptObjectBase.SetPosition and SearchFighter.InsertCommandMove (the move=true path used when rows
/// close up) from the 83/18 layout to BottomY/SlotSpacing. x and the game's own extra offset are untouched.
/// Checked in-game 2026-11-25 with 10 crew (rows 4/4/2): all four slots visible at 67/13.
/// </summary>
[HarmonyPatch]
internal static class FormationFit
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> BottomY, SlotSpacing;

    private const int GameBaseY = 83, GameDy = 18;

    private static java.util.Vector _vec; // non-null while a 4-per-row SetForcePosition is running

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("FormationFit", "Enabled", true,
            "Dungeon battles with 4 crew per row: squeeze each row so the bottom slot isn't cut off.");
        BottomY = cfg.Bind("FormationFit", "BottomY", 67,
            new ConfigDescription("Battle y of the bottom slot; smaller is higher on screen (the game uses 83 for 4 per row, 67 for 3 per row).",
                new AcceptableValueRange<int>(40, 90)));
        SlotSpacing = cfg.Bind("FormationFit", "SlotSpacing", 13,
            new ConfigDescription("Vertical gap between slots in a row (the game uses 18 for 4 per row, 17 for 3 per row).",
                new AcceptableValueRange<int>(6, 20)));
    }

    private static bool FourPerRow(FieldBase field)
    {
        var sb = field?.TryCast<SearchBattle>();
        if (sb == null || !sb.IsTripleLine()) return false;
        var player = main.AppData.GetInstance()?.player_;
        return player != null && player.GetBattleForceLineNum() >= 4;
    }

    [HarmonyPatch(typeof(FieldBase), nameof(FieldBase.SetForcePosition),
        new[] { typeof(int), typeof(java.util.Vector), typeof(bool), typeof(bool) })]
    [HarmonyPrefix]
    private static void Begin(FieldBase __instance, java.util.Vector vec)
    {
        _vec = null;
        try { if (Enabled.Value && vec != null && FourPerRow(__instance)) _vec = vec; }
        catch (Exception e) { Plugin.L.LogError($"[formation] {e}"); }
    }

    [HarmonyPatch(typeof(FieldBase), nameof(FieldBase.SetForcePosition),
        new[] { typeof(int), typeof(java.util.Vector), typeof(bool), typeof(bool) })]
    [HarmonyFinalizer]
    private static Exception End(Exception __exception)
    {
        _vec = null;
        return __exception;
    }

    /// <summary>How far to move this fighter's y, or 0 when not adjusting.</summary>
    private static int Shift(SearchFighter f)
    {
        if (f == null || f.lineX_ < 0 || f.lineY_ < 0) return 0;
        int inRow = 0;
        for (int i = 0; i < _vec.Size(); i++)
        {
            var o = _vec.ElementAt(i)?.TryCast<SearchFighter>();
            if (o != null && !o.IsCannon() && o.lineX_ == f.lineX_) inRow++;
        }
        int dy = SlotSpacing.Value;
        int oldOff = inRow == 1 ? GameDy / 2 : f.lineY_ * GameDy;
        int newOff = inRow == 1 ? dy / 2 : f.lineY_ * dy;
        return (BottomY.Value - newOff) - (GameBaseY - oldOff);
    }

    [HarmonyPatch(typeof(ScriptObjectBase), nameof(ScriptObjectBase.SetPosition),
        new[] { typeof(float), typeof(float), typeof(int) })]
    [HarmonyPrefix]
    private static void OnSetPosition(ScriptObjectBase __instance, ref float y)
    {
        if (_vec == null) return;
        try { y += Shift(__instance.TryCast<SearchFighter>()); }
        catch (Exception e) { Plugin.L.LogError($"[formation] {e}"); }
    }

    [HarmonyPatch(typeof(SearchFighter), nameof(SearchFighter.InsertCommandMove),
        new[] { typeof(int), typeof(int), typeof(int) })]
    [HarmonyPrefix]
    private static void OnInsertMove(SearchFighter __instance, ref int y)
    {
        if (_vec == null) return;
        try { y += Shift(__instance); }
        catch (Exception e) { Plugin.L.LogError($"[formation] {e}"); }
    }
}
