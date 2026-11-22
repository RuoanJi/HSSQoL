using System.Collections.Generic;
using BepInEx.Configuration;
using kairo.unity.ui;
using UnityEngine;

namespace HSSQoL;

/// <summary>
/// Turns the mouse wheel, which the game ignores, into Up/Down arrow keys it already understands.
/// Keys are injected through Kairo's Canvas.OnKeyDown/OnKeyUp, the same entry real keys use.
/// Right-click is left alone: the game has its own right-click behavior.
/// </summary>
internal static class InputRemap
{
    internal static ConfigEntry<bool> WheelScroll;
    internal static ConfigEntry<bool> WheelInvert;
    internal static ConfigEntry<int> WheelStepsPerNotch;

    private const int ActionDown = 0, ActionUp = 1;

    // Keys pressed this frame, released on the next one so the game sees a normal tap.
    private static readonly List<KeyCode> _pendingUp = new();

    internal static void Bind(ConfigFile cfg)
    {
        WheelScroll = cfg.Bind("Input", "WheelScroll", true, "Mouse wheel sends Up/Down arrow keys.");
        WheelInvert = cfg.Bind("Input", "WheelInvert", false, "Reverse the wheel direction.");
        WheelStepsPerNotch = cfg.Bind("Input", "WheelStepsPerNotch", 1,
            new ConfigDescription("Arrow presses per wheel notch.", new AcceptableValueRange<int>(1, 10)));
    }

    /// <summary>Call once per frame from the plugin MonoBehaviour.</summary>
    internal static void Tick()
    {
        var canvas = Canvas.GetInstance();
        if (canvas == null) return;

        _releasedThisFrame = _pendingUp.Count > 0;
        foreach (var k in _pendingUp)
            canvas.OnKeyUp(k, new KeyEvent(ActionUp, k), null);
        _pendingUp.Clear();

        if (!Application.isFocused) return;

        if (WheelScroll.Value)
        {
            float dy = Input.mouseScrollDelta.y;
            if (dy != 0f)
            {
                bool up = (dy > 0f) ^ WheelInvert.Value;
                var key = up ? KeyCode.UpArrow : KeyCode.DownArrow;
                // Reversing direction drops any presses still queued the other way.
                if (_queued.Count > 0 && _queued.Peek() != key) _queued.Clear();
                for (int i = 0; i < WheelStepsPerNotch.Value && _queued.Count < 20; i++)
                    _queued.Enqueue(key);
            }
        }

        // One tap per two frames (down, then up next frame) so the game registers every press.
        if (_queued.Count > 0 && _pendingUp.Count == 0 && !_releasedThisFrame)
            Press(canvas, _queued.Dequeue());
    }

    private static readonly Queue<KeyCode> _queued = new();
    private static bool _releasedThisFrame;

    private static void Press(Canvas canvas, KeyCode key)
    {
        canvas.OnKeyDown(key, new KeyEvent(ActionDown, key), null);
        _pendingUp.Add(key);
    }
}
