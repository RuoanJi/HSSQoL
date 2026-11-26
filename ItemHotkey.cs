using System;
using BepInEx.Configuration;
using data;
using form;
using HarmonyLib;
using kairo.unity.form;
using UnityEngine;
using Canvas = kairo.unity.ui.Canvas;

namespace HSSQoL;

/// <summary>
/// 道具快捷键: one key press uses one party-wide drug while a party is exploring a dungeon.
///
/// Game flow (ISIL of SubForm.ProItemList): in the item list, a drug (ItemData.type_ 2) whose allType_ is 0 HP, 1 SP,
/// 2 aura or 3 speed asks "use?" (MakeDialog) and on yes pushes CreateUseDrugItem(item) (SubForm type 64,
/// TYPE_USE_DRUG_ITEM, item in useItem_). Star-up (allType_ 4) and knowledge books open a crew picker instead.
/// So the drugs that need no target are exactly ids 25-32 (迷你回复药 … 速度之药).
///
/// InitUseDrugItem builds the message from Player.GetSearceForce (the exploring party). ProUseDrugItem counts frame_ up,
/// and once it is past 17 waits for OK (key bit 0x100000) or soft label 3. Then it does item.Add(-1) and applies the effect:
/// HP/SP: each live crew gains max(5, max × allValue_ / 100), capped at max; aura: FighterBase.ForceStartAuraTime;
/// speed: a timed speed buff. Then it Pops.
///
/// The hotkey pushes that same window on the game form and confirms it on its first update (frame_ past 17 plus a
/// forced OK), so the game's own code consumes the item and applies the effect.
/// </summary>
[HarmonyPatch]
internal static class ItemHotkey
{
    internal static ConfigEntry<bool> Enabled, ShowToast;

    private struct Slot { public int Id; public string Key, Name; public KeyCode Default; public ConfigEntry<KeyCode> Entry; }

    private static readonly Slot[] Slots =
    {
        new() { Id = 25, Key = "HpSmall",  Name = "迷你回复药",   Default = KeyCode.Alpha3 },
        new() { Id = 26, Key = "HpMedium", Name = "回复药剂",     Default = KeyCode.Alpha4 },
        new() { Id = 27, Key = "HpLarge",  Name = "回复秘药",     Default = KeyCode.Alpha5 },
        new() { Id = 28, Key = "SpSmall",  Name = "SP回复水",     Default = KeyCode.None },
        new() { Id = 29, Key = "SpMedium", Name = "SP回复药",     Default = KeyCode.None },
        new() { Id = 30, Key = "SpLarge",  Name = "SP能量饮料",   Default = KeyCode.None },
        new() { Id = 31, Key = "Aura",     Name = "光环之药",     Default = KeyCode.None },
        new() { Id = 32, Key = "Speed",    Name = "速度之药",     Default = KeyCode.None },
    };

    private const int UseDrugItem = 64, KeyOk = 0x100000, WaitFrames = 17;
    private static IntPtr _pending;     // the window we pushed, until it closes
    private static bool _forcing;       // inside its Update
    private static int _pendingAge;     // frames since the push
    private static string _toast = "";
    private static float _toastUntil;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("ItemHotkey", "Enabled", true,
            "道具快捷键: while a party is exploring a dungeon and no window is open, a key uses one of that drug on the whole party.");
        ShowToast = cfg.Bind("ItemHotkey", "ShowToast", true, "Show a short on-screen note after each hotkey use.");
        for (int i = 0; i < Slots.Length; i++)
            Slots[i].Entry = cfg.Bind("ItemHotkey", Slots[i].Key, Slots[i].Default,
                $"Key that uses one {Slots[i].Name} (item id {Slots[i].Id}). None = no key.");
    }

    private static void Toast(string s)
    {
        Plugin.L.LogInfo($"[itemkey] {s}");
        _toast = s;
        _toastUntil = Time.unscaledTime + 2.5f;
    }

    internal static void Tick()
    {
        if (!Enabled.Value) return;
        for (int i = 0; i < Slots.Length; i++)
        {
            var k = Slots[i].Entry.Value;
            if (k != KeyCode.None && Input.GetKeyDown(k)) { Use(Slots[i]); return; }
        }
    }

    private static void Use(Slot s)
    {
        var app = main.AppData.GetInstance();
        var p = app?.player_;
        if (p?.searchCourse_ == null) return;                     // only while exploring a dungeon
        var fm = FormManagerBase.GetInstance();
        var cur = fm?.Current;
        // Only from the plain dungeon view: no window, menu or dialog on top.
        if (cur == null || fm.Top == null || fm.Top.Pointer != cur.Pointer || cur.TryCast<GameForm>() == null) return;
        if (_pending != IntPtr.Zero) return;
        var force = p.GetSearceForce(false);
        if (force == null || force.Size() == 0) return;

        ItemData item = null;
        var items = app.itemList_;
        for (int i = 0; i < items.Length; i++)
            if (items[i] != null && items[i].id_ == s.Id) { item = items[i]; break; }
        // Same rule the item list uses for "no target needed": a drug with allType_ 0-3.
        if (item == null || item.type_ != ItemData.TYPE_DRUG || item.allType_ < 0 || item.allType_ > 3)
        {
            Toast($"道具 {s.Id} 不是可直接使用的药品");
            return;
        }
        int have = item.GetNum();
        if (have <= 0) { Toast($"没有{item.GetName()}"); return; }

        var window = SubForm.CreateUseDrugItem(item);
        if (!cur.Push(window)) { Toast($"无法使用{item.GetName()}"); return; }
        _pending = window.Pointer;
        _pendingAge = 0;
        Toast($"使用 {item.GetName()} (剩 {have - 1})");
    }

    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void UpdatePre(SubForm __instance, out bool __state)
    {
        __state = false;
        if (_pending == IntPtr.Zero || __instance.Pointer != _pending) return;
        if (__instance.type_ != UseDrugItem) { _pending = IntPtr.Zero; return; }
        if (__instance.frame_ < WaitFrames) __instance.frame_ = WaitFrames;   // ProUseDrugItem adds 1, then needs > 17
        _forcing = true;
        __state = true;
    }

    [HarmonyPatch(typeof(SubForm), nameof(SubForm.Update), typeof(bool))]
    [HarmonyPostfix]
    private static void UpdatePost(SubForm __instance, bool __state)
    {
        if (!__state) return;
        _forcing = false;
        if (!__instance.IsAlive()) _pending = IntPtr.Zero;
    }

    internal static void Watchdog()
    {
        // Our window closed some other way (or never updated): forget it.
        if (_pending == IntPtr.Zero) return;
        // A push can take a frame to land on top, so allow a few frames first.
        var top = FormManagerBase.GetInstance()?.Top;
        if (++_pendingAge > 10 && (top == null || top.Pointer != _pending)) _pending = IntPtr.Zero;
    }

    [HarmonyPatch(typeof(Canvas), nameof(Canvas.CheckKeyPulse), typeof(int))]
    [HarmonyPrefix]
    private static bool KeyPulse(int key, ref bool __result)
    {
        if (!_forcing || (key & KeyOk) == 0) return true;
        __result = true;
        return false;
    }

    internal static void Draw()
    {
        if (!ShowToast.Value || Time.unscaledTime >= _toastUntil) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        var r = new Rect(Screen.width - 330, 78, 320, 24);
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), _toast, style);
        GUI.color = Color.white;
        GUI.Label(r, _toast, style);
    }
}
