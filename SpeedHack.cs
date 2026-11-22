using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Game speed-up, toggled with F2: the Cheat Engine method. The game's timer functions (QueryPerformanceCounter,
/// GetTickCount, timeGetTime and the wall clock GetSystemTimeAsFileTime) report time passing faster.
///
/// Why clocks (findings, 2026-11-21):
///  - Kairo paces its loop by the WALL CLOCK (JSystem.CurrentTimeMillis etc.), so the wall clock must be scaled.
///  - Visible motion follows elapsed game time. Running the game's step routine GameForm.ProgressTime 5x per
///    frame (log: 5.0 steps/frame) changed nothing visible, and the game's own "fps boost" (extra ProgressTime
///    steps, GetFpsSkipNum, inlined) is capped at 3 steps. So step/boost approaches are capped or ineffective;
///    scaling the clocks is not capped, so any multiplier applies.
///  - It takes a few seconds to settle: GameForm averages its frame rate over a 5-entry buffer (fpsAve_/aveFps_).
///  - Side effect, same as Cheat Engine: while on, the in-game date/time runs ahead, and it stays ahead after
///    turning off (time never jumps back).
///
/// Cheat Engine hooks these functions process-wide. Here the process also hosts the .NET runtime that runs this
/// mod, which calls QueryPerformanceCounter itself (also during GC), so a process-wide managed hook could deadlock.
/// Instead only the import tables (IAT) of UnityPlayer.dll, GameAssembly.dll, baselib.dll and KairoGames.exe are
/// patched. fake = fakeBase + (real - realBase) * speed, re-anchored at "now" on every change so time never jumps
/// or runs backwards. Hooks are installed on first use and removed on quit.
/// </summary>
internal static unsafe class SpeedHack
{
    internal static ConfigEntry<KeyCode> ToggleKey;
    internal static ConfigEntry<float> Speed;
    internal static ConfigEntry<bool> ShowIndicator;

    internal static bool Active { get; private set; }

    private sealed class Clock
    {
        public readonly long RealBase, FakeBase;
        public readonly double Speed;
        public Clock(long realBase, long fakeBase, double speed) { RealBase = realBase; FakeBase = fakeBase; Speed = speed; }
        public long Fake(long real) => FakeBase + (long)((real - RealBase) * Speed);
    }

    // Replaced atomically on each speed change; hooks only read them.
    private static Clock _qpc, _tick, _tgt, _wall;

    private static readonly string[] Modules = { "UnityPlayer.dll", "GameAssembly.dll", "baselib.dll", "KairoGames.exe" };
    private static readonly List<(IntPtr slot, IntPtr original)> _patched = new();
    private static bool _installed;

    internal static void Bind(ConfigFile cfg)
    {
        ToggleKey = cfg.Bind("Speed", "ToggleKey", KeyCode.F2, "Toggles the game speed-up.");
        Speed = cfg.Bind("Speed", "Multiplier", 5f,
            new ConfigDescription("Game speed while the speed-up is on (takes a few seconds to settle).",
                new AcceptableValueRange<float>(1f, 10f)));
        ShowIndicator = cfg.Bind("Speed", "ShowIndicator", true, "Show a small speed label while the speed-up is on.");
    }

    internal static void Tick()
    {
        if (!Input.GetKeyDown(ToggleKey.Value)) return;
        try
        {
            if (!_installed) Install();
            Active = !Active;
            SetSpeed(Active ? Speed.Value : 1.0);
            string detail = $"x{Speed.Value:0.##}";
            if (!Active && _wall != null)
            {
                long real; GetSystemTimeAsFileTime(&real);
                detail += $", in-game clock now {(_wall.Fake(real) - real) / 1e7:0} s ahead of real time";
            }
            Plugin.L.LogInfo($"[speed] {(Active ? "on" : "off")} ({detail})");
        }
        catch (Exception e)
        {
            Active = false;
            Plugin.L.LogError($"[speed] toggle failed: {e}");
        }
    }

    internal static void Draw()
    {
        if (!Active || !ShowIndicator.Value) return;
        var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        string text = $"▶▶ x{Speed.Value:0.##}";
        GUI.color = new Color(0, 0, 0, 0.6f);
        GUI.Label(new Rect(Screen.width - 101, 9, 100, 24), text, style);
        GUI.color = Color.white;
        GUI.Label(new Rect(Screen.width - 102, 8, 100, 24), text, style);
    }

    // ---------- clocks ----------

    private static void SetSpeed(double speed)
    {
        long q; QueryPerformanceCounter(&q);
        long t = (long)GetTickCount64();
        long g = timeGetTime();
        _qpc = new Clock(q, _qpc?.Fake(q) ?? q, speed);
        _tick = new Clock(t, _tick?.Fake(t) ?? t, speed);
        // timeGetTime wraps every ~49 days: keep its base in the same 32-bit space and unwrap the delta.
        var old = _tgt;
        long fakeG = old == null ? g : old.FakeBase + (long)((uint)(g - old.RealBase) * old.Speed);
        _tgt = new Clock(g, fakeG, speed);
        long w; GetSystemTimeAsFileTime(&w);
        _wall = new Clock(w, _wall?.Fake(w) ?? w, speed);
        Thread.MemoryBarrier();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static void HookGetSystemTimeAsFileTime(long* fileTime)
    {
        long real;
        GetSystemTimeAsFileTime(&real);
        if (fileTime == null) return;
        var c = Volatile.Read(ref _wall);
        *fileTime = c == null ? real : c.Fake(real);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static int HookQueryPerformanceCounter(long* count)
    {
        long real;
        int ok = QueryPerformanceCounter(&real);
        if (ok != 0 && count != null)
        {
            var c = Volatile.Read(ref _qpc);
            *count = c == null ? real : c.Fake(real);
        }
        return ok;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint HookGetTickCount()
    {
        long real = (long)GetTickCount64();
        var c = Volatile.Read(ref _tick);
        return (uint)(c == null ? real : c.Fake(real));
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static uint HookTimeGetTime()
    {
        uint real = timeGetTime();
        var c = Volatile.Read(ref _tgt);
        if (c == null) return real;
        return (uint)(c.FakeBase + (long)((uint)(real - (uint)c.RealBase) * c.Speed));
    }

    // ---------- import table patching ----------

    private static void Install()
    {
        SetSpeed(1.0);
        var qpc = (IntPtr)(delegate* unmanaged[Stdcall]<long*, int>)&HookQueryPerformanceCounter;
        var tick = (IntPtr)(delegate* unmanaged[Stdcall]<uint>)&HookGetTickCount;
        var tgt = (IntPtr)(delegate* unmanaged[Stdcall]<uint>)&HookTimeGetTime;
        var wall = (IntPtr)(delegate* unmanaged[Stdcall]<long*, void>)&HookGetSystemTimeAsFileTime;

        foreach (var m in Modules)
        {
            int n = Patch(m, "kernel32.dll", "QueryPerformanceCounter", qpc)
                  + Patch(m, "kernel32.dll", "GetTickCount", tick)
                  + Patch(m, "winmm.dll", "timeGetTime", tgt)
                  + Patch(m, "kernel32.dll", "GetSystemTimeAsFileTime", wall);
            Plugin.L.LogInfo($"[speed] {m}: patched {n} import(s)");
        }
        _installed = true;

        // Restore the original imports on quit so no game thread calls into the mod while things shut down.
        Application.add_quitting((Il2CppSystem.Action)(Action)Uninstall);
    }

    private static void Uninstall()
    {
        foreach (var (slot, original) in _patched) WriteSlot(slot, original);
        _patched.Clear();
        Plugin.L.LogInfo("[speed] imports restored");
    }

    private static int Patch(string module, string dll, string func, IntPtr hook)
    {
        byte* b = (byte*)GetModuleHandleW(module);
        if (b == null) return 0;
        int pe = *(int*)(b + 0x3C);
        byte* opt = b + pe + 24;
        int dataDirs = *(ushort*)opt == 0x10b ? 96 : 112;
        int importRva = *(int*)(opt + dataDirs + 8);
        if (importRva == 0) return 0;

        int count = 0;
        for (byte* d = b + importRva; *(int*)(d + 12) != 0; d += 20)
        {
            int nameTable = *(int*)d, nameRva = *(int*)(d + 12), iatRva = *(int*)(d + 16);
            if (nameTable == 0) continue; // no name table: can't match by name safely
            if (!string.Equals(Marshal.PtrToStringAnsi((IntPtr)(b + nameRva)), dll, StringComparison.OrdinalIgnoreCase)) continue;

            uint* names = (uint*)(b + nameTable);
            IntPtr* iat = (IntPtr*)(b + iatRva);
            for (int i = 0; names[i] != 0; i++)
            {
                if ((names[i] & 0x80000000) != 0) continue; // imported by ordinal
                if (Marshal.PtrToStringAnsi((IntPtr)(b + names[i] + 2)) != func) continue;
                _patched.Add(((IntPtr)(&iat[i]), iat[i]));
                WriteSlot((IntPtr)(&iat[i]), hook);
                count++;
            }
        }
        return count;
    }

    private static void WriteSlot(IntPtr slot, IntPtr value)
    {
        const uint PAGE_READWRITE = 0x04;
        if (!VirtualProtect(slot, (UIntPtr)IntPtr.Size, PAGE_READWRITE, out uint old))
            throw new InvalidOperationException($"VirtualProtect failed ({Marshal.GetLastWin32Error()})");
        *(IntPtr*)slot = value;
        VirtualProtect(slot, (UIntPtr)IntPtr.Size, old, out _);
    }

    // The mod's own P/Invokes resolve straight to the real functions; only game-module imports are patched.
    [DllImport("kernel32.dll")] private static extern int QueryPerformanceCounter(long* count);
    [DllImport("kernel32.dll")] private static extern ulong GetTickCount64();
    [DllImport("kernel32.dll")] private static extern void GetSystemTimeAsFileTime(long* fileTime);
    [DllImport("winmm.dll")] private static extern uint timeGetTime();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);
}
