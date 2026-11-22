using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using form;
using HarmonyLib;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// One-frame snapshot of how SubForm menus are drawn. Press F6 with a menu open:
/// logs every DrawSubMenuBox / DrawCommand / DrawSubCommand call in the next frame,
/// the open SubForms' type_/mode_/focus_, and the static friendCommand table.
/// </summary>
[HarmonyPatch]
internal static class MenuProbe
{
    private static int _captureFrame = -1;
    private static bool Capturing => Time.frameCount == _captureFrame;
    private static void Log(string s) => Plugin.L.LogInfo($"[menu f{Time.frameCount}] {s}");

    internal static void Tick()
    {
        if (!Input.GetKeyDown(KeyCode.F6)) return;
        _captureFrame = Time.frameCount + 1;
        try { DumpState(); } catch (Exception e) { Log($"state error: {e}"); }
    }

    internal static string Describe(Il2CppSystem.Object o, int depth = 0)
    {
        if (o == null) return "null";
        var t = o.GetIl2CppType();
        if (t.IsArray && depth < 3)
        {
            var arr = o.Cast<Il2CppSystem.Array>();
            var parts = new List<string>();
            for (int i = 0; i < arr.Length; i++) parts.Add(Describe(arr.GetValue(i), depth + 1));
            return "[" + string.Join(", ", parts) + "]";
        }
        if (t.FullName == "System.String") return "\"" + o.ToString() + "\"";
        if (t.FullName.StartsWith("System.Int") || t.FullName == "System.Boolean") return o.ToString();
        return $"<{t.FullName}>";
    }

    private static void DumpState()
    {
        var mgr = kairo.unity.form.FormManagerBase.GetInstance();
        Log($"forms: current={mgr?.Current?.GetIl2CppType().FullName} top={mgr?.Top?.GetIl2CppType().FullName} front={mgr?.Front?.GetIl2CppType().FullName}");
        foreach (var f in new[] { mgr?.Current, mgr?.Top, mgr?.Front })
        {
            var s = f?.TryCast<SubForm>();
            if (s == null) continue;
            Log($"  SubForm type_={s.type_} mode_={s.mode_} focus_={s.focus_} menuCurrent_={(s.menuCurrent_ == null ? "null" : $"type_={s.menuCurrent_.type_} mode_={s.menuCurrent_.mode_} focus_={s.menuCurrent_.focus_}")}");
            Log($"    crew_={s.crew_?.GetName()} sampleHuman_={s.sampleHuman_?.crew_?.GetName()} itemUseHuman_={s.itemUseHuman_?.crew_?.GetName()} " +
                $"left={s.leftHuman_?.crew_?.GetName()} right={s.rightHuman_?.crew_?.GetName()} humanCharaIndex_={s.humanCharaIndex_} select_={s.select_} " +
                $"useItemHuman_={SubForm.useItemHuman_?.crew_?.GetName()}");
        }
        var top = mgr?.Top?.TryCast<SubForm>();
        if (top != null) DumpAllFields(top);

        var p = main.AppData.GetInstance()?.player_;
        Log($"player_.force_ = {DescribeValue(p?.force_) ?? "empty"}");
        var fe = game.ForceEditData.GetInstance();
        var force = fe?.GetForce();
        if (force != null)
        {
            var parts = new List<string>();
            for (int i = 0; i < force.Size(); i++) parts.Add($"{i}:{force.Get(i)?.crew_?.GetName()}");
            Log($"ForceEditData.GetForce() ({force.Size()}) = [{string.Join(", ", parts)}]");
        }
        Log($"ForceEditData.questForce_ = {DescribeValue(fe?.questForce_) ?? "empty"}");
        if (fe?.pirateForce_ != null)
            for (int i = 0; i < fe.pirateForce_.Length; i++) Log($"ForceEditData.pirateForce_[{i}] = {DescribeValue(fe.pirateForce_[i]) ?? "empty"}");
        Log($"FeedButton roster target = {FeedButton.RosterCandidates()}");

        var table = SubForm.friendCommand;
        if (table != null)
        {
            Log($"friendCommand ({table.Length} rows):");
            for (int i = 0; i < table.Length; i++)
            {
                var row = table[i];
                var parts = new List<string>();
                if (row != null) foreach (var cell in row) parts.Add(Describe(cell));
                Log($"  [{i}] [{string.Join(", ", parts)}]");
            }
        }
    }

    // ---- draw hooks (only log in the capture frame) ----

    [HarmonyTargetMethods]
    internal static IEnumerable<MethodBase> Targets()
    {
        const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        foreach (var m in typeof(SubForm).GetMethods(all))
            if (m.Name is "DrawSubMenuBox" or "DrawCommand" or "DrawSubCommand" or "DrawSubCommandMenuIcon") yield return m;
        foreach (var m in typeof(MyFormBase).GetMethods(all))
            if (m.Name is "AddTouchShortcut" or "DrawTouchButton" or "AddTouch" or "Touch_up" or "Click" or "FillCommandArea" or "AddFillTouch") yield return m;
        foreach (var m in typeof(kairo.unity.ui.Graphics).GetMethods(all))
            if (m.Name is "DrawString" or "DrawString2" or "DrawStringBold" or "FillRoundRect" or "FillRect" or "DrawRect" or "SetFont") yield return m;
        foreach (var m in typeof(main.AppData).GetMethods(all))
            if (m.Name is "DrawShortcutIcon") yield return m;
    }

    [HarmonyPrefix]
    private static void Pre(MethodBase __originalMethod, object[] __args, object __instance)
    {
        if (!Capturing) return;
        var sb = new StringBuilder($"{__originalMethod.Name}(");
        var ps = __originalMethod.GetParameters();
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].ParameterType.Name == "Graphics") continue;
            object a = __args[i];
            string v = a switch
            {
                null => "null",
                Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> ia => "int[" + string.Join(",", ia) + "]",
                int iv when ps[i].Name.Contains("olor") => $"0x{iv:X8}",
                Il2CppSystem.Object o when ps[i].ParameterType.Name == "Vector" => DescribeVector(o),
                Il2CppSystem.Object o => Describe(o),
                _ => a.ToString(),
            };
            sb.Append($"{ps[i].Name}={v} ");
        }
        var sf = (__instance as Il2CppSystem.Object)?.TryCast<SubForm>();
        sb.Append(sf != null ? $") on SubForm type_={sf.type_} mode_={sf.mode_} focus_={sf.focus_}"
                             : $") on {(__instance as Il2CppSystem.Object)?.GetIl2CppType().FullName ?? "static"}");
        Log(sb.ToString());
    }

    /// <summary>Logs int[] return values. Separate class: a __result postfix can't attach to void methods.</summary>
    [HarmonyPatch]
    internal static class Results
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> Targets()
        {
            foreach (var m in MenuProbe.Targets())
                if (m is MethodInfo mi && mi.ReturnType != typeof(void)) yield return m;
        }

        [HarmonyPostfix]
        private static void Post(MethodBase __originalMethod, object __result)
        {
            if (!Capturing || __result is not Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> r) return;
            Log($"  {__originalMethod.Name} returned int[{string.Join(",", r)}]");
        }
    }

    /// <summary>Logs every non-empty field of a SubForm (interop exposes IL2CPP fields as properties).</summary>
    private static void DumpAllFields(SubForm s)
    {
        Log($"all fields of SubForm type_={s.type_}:");
        foreach (var p in typeof(SubForm).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
            object v;
            try { v = p.GetValue(s); } catch { continue; }
            string d = DescribeValue(v);
            if (d != null) Log($"  {p.Name} = {d}");
        }
    }

    private static string DescribeValue(object v)
    {
        switch (v)
        {
            case null: return null;
            case int i: return i == 0 ? null : i.ToString();
            case bool b: return b ? "true" : null;
            case Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int> ia: return ia.Length == 0 ? null : "int[" + string.Join(",", ia) + "]";
            case Il2CppSystem.Object o:
                if (o.TryCast<game.Crew>() is { } c) return $"Crew '{c.GetName()}'";
                if (o.TryCast<game.Human>() is { } h) return $"Human '{h.crew_?.GetName()}'";
                if (o.TryCast<game.FighterBase>() is { } f) return $"Fighter '{f.crew_?.GetName()}'";
                if (o.TryCast<java.util.Vector>() is { } vec)
                {
                    if (vec.Size() == 0) return null;
                    var parts = new List<string>();
                    for (int k = 0; k < Math.Min(vec.Size(), 12); k++) parts.Add(DescribeValue(vec.Get(k)) ?? Describe(vec.Get(k)));
                    return $"Vector({vec.Size()})[{string.Join(", ", parts)}]";
                }
                return null; // other objects: too noisy
            default: return null;
        }
    }

    private static string DescribeVector(Il2CppSystem.Object o)
    {
        var v = o.TryCast<java.util.Vector>();
        if (v == null) return Describe(o);
        var parts = new List<string>();
        for (int i = 0; i < v.Size(); i++) parts.Add(Describe(v.Get(i)));
        return $"Vector({v.Size()})[{string.Join(", ", parts)}]";
    }
}
