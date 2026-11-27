using BepInEx.Configuration;
using form;
using game;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using kairo.unity.surface;
using UnityEngine;
using Font = kairo.unity.ui.Font;
using Graphics = kairo.unity.ui.Graphics;

namespace HSSQoL;

/// <summary>
/// Adds a round "强化到满级" button in the crew command menu (SubForm type_ 15), in the slot right of
/// the 強化 row where "power up multiple characters" (shortcut type 13) sits, and shifts that icon right.
///
/// From the menu probe:
///   DrawSubCommand(command, focusType, focus, human, humanIndex, teamPowerup) draws the rows;
///   AddTouchShortcut(g, 16, -20, 66, 1966080)  single power-up (left of 強化)
///   AddTouchShortcut(g, 13, 152, 66, 1966081)  team power-up (right of 強化)
/// Taps arrive as SubForm.OnTouchEvent(e) with e.component.value = the shortcut's value.
///
/// The button is registered with the game's own AddTouchShortcut, so it gets the same size, position and
/// touch behaviour as the real icons (no selection marker). DrawShortcutIcon returns the icon's area; for
/// our button a postfix paints an opaque circle with the label over it.
/// </summary>
[HarmonyPatch]
internal static class FeedButton
{
    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<int> Gap, OffsetY, Line2Indent, PixelSize, RingWidth, DiameterInset, CenterDarkenPercent, JobChangeOffsetX;
    internal static ConfigEntry<string> Line1, Line2, SwitchLine1, SwitchLine2, TextColor, OutlineColor;
    internal static ConfigEntry<int> OutlineWidth, FontScalePercent;
    internal static ConfigEntry<bool> NativeOutline;

    private const int ShortcutTeamPowerUp = 13;
    private const int CrewCommandMenu = 15;   // SubForm.TYPE_FRIEND_LIST: crew list → character
    private const int RosterMenu = 17;        // SubForm.TYPE_EDIT_FORCE: combat roster → character
    private const int JobChangePage = 63;     // SubForm.TYPE_JOB_CHANGE: character in jobChangeChar_
    // DrawJobChange (ISIL): AddTouchShortcut(g, 32, SHORTCUT_DECIDE 7, 57, 222, 20) = 确定,
    //                       AddTouchShortcut(g, 32, SHORTCUT_ITEM 4, 171, 222, 0)   = 道具.
    private const int ShortcutItem = 4, JobChangeDecideX = 57;

    private static bool IsCharacterMenu(SubForm sf) => sf != null && (sf.type_ == CrewCommandMenu || sf.type_ == RosterMenu);

    /// <summary>The Human a menu is for. The crew menu passes it to DrawSubCommand; the roster menu indexes player_.force_.</summary>
    private static Human HumanFor(SubForm sf) =>
        sf.type_ == RosterMenu ? RosterHuman(sf, out _) : MenuHuman;

    // Roster menu: player_.force_ is a Vector of parties, each a Vector of Human.
    // The menu holds the party in select_ and the slot in value1_ (user-verified for both parties).
    // No fallback: if the lookup fails, return null so nothing is fed.
    private static Human RosterHuman(SubForm sf, out string source)
    {
        source = "none";
        if (sf == null) return null;
        int party = sf.select_, slot = sf.value1_;
        var parties = main.AppData.GetInstance()?.player_?.force_;
        if (parties == null || party < 0 || party >= parties.Size()) return null;
        var members = parties.Get(party)?.TryCast<java.util.Vector>();
        if (members == null || slot < 0 || slot >= members.Size()) return null;
        source = $"player_.force_[{party}][{slot}]";
        return members.Get(slot)?.TryCast<Human>();
    }

    internal static string RosterCandidates()
    {
        var sf = kairo.unity.form.FormManagerBase.GetInstance()?.Top?.TryCast<SubForm>();
        if (sf == null || sf.type_ != RosterMenu) return "not in roster menu";
        var h = RosterHuman(sf, out var src);
        return $"select_={sf.select_} value1_={sf.value1_} -> {h?.crew_?.GetName() ?? "null"} via {src}";
    }
    internal const int OurValue = 0x48535100; // arbitrary, far from the game's VALUE_* ranges
    internal const int SwitchValue = OurValue + 1; // 转职满级 (job-change page only)
    internal const int SellValue = OurValue + 2;   // 批量出售 (port shop sell screen)
    internal const int SmithValue = OurValue + 3;  // 投入所有相性相符 (smith material page)

    private static Human _menuHuman;
    private static int _menuHumanFrame = -100;
    private static bool _drawingOurs;
    private static string _label1 = "", _label2 = "";   // text for the button being drawn
    private static float _discScale = 1f;               // size of the button being drawn, relative to the icon
    private static int _fontPercent, _indent;           // 0 / -1 = use the [FeedButton] settings
    private static int _iconWidth = 32;   // learned from DrawShortcutIcon's returned area
    private static bool _warnedArea;

    internal static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("FeedButton", "Enabled", true, "Show the auto-feed button next to 強化 in the character menus.");
        Line1 = cfg.Bind("FeedButton", "Line1", "强化", "First line of the button text.");
        Line2 = cfg.Bind("FeedButton", "Line2", "满级", "Second line of the button text.");
        Line2Indent = cfg.Bind("FeedButton", "Line2Indent", 3, "Shifts the second line right (game units).");
        OffsetY = cfg.Bind("FeedButton", "OffsetY", 0, "Moves the text up (negative) or down (positive).");
        JobChangeOffsetX = cfg.Bind("FeedButton", "JobChangeOffsetX", 0,
            "Job-change page: moves the button pair left (negative) or right (positive) from the midpoint between 确定 and 道具.");
        SwitchLine1 = cfg.Bind("FeedButton", "SwitchLine1", "转职", "Job-change page, 转职满级 button: first line.");
        SwitchLine2 = cfg.Bind("FeedButton", "SwitchLine2", "满级", "Job-change page, 转职满级 button: second line.");
        Gap = cfg.Bind("FeedButton", "Gap", 4, "Space between the button and the moved team power-up icon.");
        PixelSize = cfg.Bind("FeedButton", "PixelSize", 1, "Size of one circle 'pixel' in game units (1 ≈ the game's own pixel art).");
        RingWidth = cfg.Bind("FeedButton", "RingWidth", 3, "Width of the brighter outer ring, in circle pixels.");
        DiameterInset = cfg.Bind("FeedButton", "DiameterInset", 2, "How much smaller than the 34-unit icon area the circle is.");
        CenterDarkenPercent = cfg.Bind("FeedButton", "CenterDarkenPercent", 15, "How much darker the centre is than the ring.");
        TextColor = cfg.Bind("FeedButton", "TextColor", "FFFFFFFF", "Text colour, AARRGGBB hex.");
        OutlineColor = cfg.Bind("FeedButton", "OutlineColor", "FF1A2A3A", "Text outline colour, AARRGGBB hex.");
        NativeOutline = cfg.Bind("FeedButton", "NativeOutline", true,
            "Use the game's own outlined text (DrawStringBold). If false, the outline is drawn at 8 offsets of OutlineWidth.");
        OutlineWidth = cfg.Bind("FeedButton", "OutlineWidth", 1, "Outline thickness in game units when NativeOutline is false (0 = none).");
        FontScalePercent = cfg.Bind("FeedButton", "FontScalePercent", 85, "Text size as a percentage of the menu font.");
    }

    private static int Darken(int argb, int percent)
    {
        float k = 1f - percent / 100f;
        int a = (int)((uint)argb >> 24);
        int r = (int)(((argb >> 16) & 0xFF) * k), gC = (int)(((argb >> 8) & 0xFF) * k), b = (int)((argb & 0xFF) * k);
        return (a << 24) | (r << 16) | (gC << 8) | b;
    }

    /// <summary>Fills a pixel-art disc: one FillRect per row of 'pixels' of size px.</summary>
    private static void FillPixelDisc(Graphics g, float cx, float cy, int radiusPx, int px, int color)
    {
        float r = radiusPx;
        for (int row = -radiusPx; row < radiusPx; row++)
        {
            float yc = row + 0.5f;                       // pixel row centre, in pixel units
            float half = Mathf.Sqrt(Mathf.Max(0, r * r - yc * yc));
            int cols = Mathf.RoundToInt(half);          // pixels on each side of the centre
            if (cols <= 0) continue;
            g.FillRect(cx - cols * px, cy + row * px, cols * 2 * px, px, color);
        }
    }

    /// <summary>The character whose command menu is on screen, if drawn in the last few frames.</summary>
    internal static Human MenuHuman => Time.frameCount - _menuHumanFrame <= 3 ? _menuHuman : null;

    private static void Log(string s) => Plugin.L.LogInfo($"[feedbtn f{Time.frameCount}] {s}");

    [HarmonyPatch(typeof(SubForm), "DrawSubCommand")]
    [HarmonyPrefix]
    private static void RememberCharacter(java.util.Vector human, int humanIndex)
    {
        if (human == null || humanIndex < 0 || humanIndex >= human.Size()) return;
        _menuHuman = human.Get(humanIndex)?.TryCast<Human>();
        _menuHumanFrame = Time.frameCount;
    }

    [HarmonyPatch(typeof(MyFormBase), nameof(MyFormBase.AddTouchShortcut),
        new[] { typeof(Graphics), typeof(int), typeof(int), typeof(int), typeof(int) })]
    [HarmonyPrefix]
    private static void InsertButton(MyFormBase __instance, Graphics g, int type, ref int x, int y)
    {
        if (!Enabled.Value || _drawingOurs || type != ShortcutTeamPowerUp) return;
        if (!IsCharacterMenu(__instance.TryCast<SubForm>())) return;

        int ourX = x;
        x += _iconWidth + Gap.Value;
        DrawOurs(__instance, g, ourX, y, OurValue, Line1.Value, Line2.Value);
    }

    /// <summary>Registers and draws one of our round buttons through the game's own AddTouchShortcut.</summary>
    internal static void DrawOurs(MyFormBase form, Graphics g, int x, int y, int value, string line1, string line2,
        float discScale = 1f, int fontPercent = 0, int line2Indent = -1)
    {
        _label1 = line1;
        _label2 = line2;
        _discScale = discScale;
        _fontPercent = fontPercent;
        _indent = line2Indent;
        _drawingOurs = true;
        try { form.AddTouchShortcut(g, ShortcutTeamPowerUp, x, y, value); }
        finally { _drawingOurs = false; }
    }

    /// <summary>Job-change page: after the 道具 icon, add our button midway between 确定 and 道具.</summary>
    [HarmonyPatch(typeof(MyFormBase), nameof(MyFormBase.AddTouchShortcut),
        new[] { typeof(Graphics), typeof(int), typeof(int), typeof(int), typeof(int), typeof(int) })]
    [HarmonyPostfix]
    private static void InsertJobChangeButton(MyFormBase __instance, Graphics g, int touchID, int type, int x, int y)
    {
        if (!Enabled.Value || _drawingOurs || type != ShortcutItem) return;
        var sf = __instance.TryCast<SubForm>();
        if (sf == null || sf.type_ != JobChangePage) return;

        // Use the 5-arg overload (touch ID 4), NOT the page's touch ID 32: ID-32 shortcuts turn a tap into a
        // simulated key press derived from the value, and ours mapped to key bit 1 = the page's 道具 key
        // (ProJobChange: CheckKeyPulse(1) → CreateDirectUseJobChangeItem). ID 4 only raises touch events.
        // Two buttons side by side, centred between 确定 and 道具: 强化满级 left, 转职满级 right.
        int centre = (JobChangeDecideX + x) / 2 + JobChangeOffsetX.Value;
        if (!JobSwitch.Enabled.Value)
        {
            DrawOurs(__instance, g, centre, y, OurValue, Line1.Value, Line2.Value);
            return;
        }
        int half = (_iconWidth + Gap.Value) / 2;
        DrawOurs(__instance, g, centre - half, y, OurValue, Line1.Value, Line2.Value);
        DrawOurs(__instance, g, centre + half, y, SwitchValue, SwitchLine1.Value, SwitchLine2.Value);
    }

    [HarmonyPatch(typeof(main.AppData), nameof(main.AppData.DrawShortcutIcon))]
    [HarmonyPostfix]
    private static void PaintOverIcon(Graphics g, int type, int x, int y, Il2CppStructArray<int> __result)
    {
        if (!_drawingOurs) return;
        if (__result == null || __result.Length < 4)
        {
            if (!_warnedArea) { _warnedArea = true; Log("unexpected DrawShortcutIcon result; label not drawn"); }
            return;
        }

        int ax = __result[0], ay = __result[1], aw = __result[2], ah = __result[3];
        if (aw > 0) _iconWidth = aw;

        // Snap the centre to the pixel grid so the blocks line up with the game's pixel art.
        int px = Mathf.Max(1, PixelSize.Value);
        float cx = Mathf.Round((ax + aw / 2f) / px) * px, cy = Mathf.Round((ay + ah / 2f) / px) * px;
        int radiusPx = Mathf.Max(2, Mathf.RoundToInt((Mathf.Min(aw, ah) * _discScale - DiameterInset.Value) / (2 * px)));

        int ring = main.AppData.COLOR_SELECT_BG;
        FillPixelDisc(g, cx, cy, radiusPx, px, ring);
        FillPixelDisc(g, cx, cy, Mathf.Max(1, radiusPx - RingWidth.Value), px, Darken(ring, CenterDarkenPercent.Value));

        // GetSize() is a preset index here (0), so scale from the font's pixel height instead.
        int pct = _fontPercent > 0 ? _fontPercent : FontScalePercent.Value;
        int size = Mathf.Max(6, Mathf.RoundToInt(g.GetFont().GetHeight() * pct / 100f));

        g.PushFont(size);
        int savedColor = g.GetColor();
        try
        {
            float lh = g.GetFont().GetHeight();
            float y1 = cy - lh / 2f + OffsetY.Value, y2 = cy + lh / 2f + OffsetY.Value;
            DrawOutlined(g, _label1, cx, y1);
            DrawOutlined(g, _label2, cx + (_indent >= 0 ? _indent : Line2Indent.Value), y2);
        }
        finally
        {
            g.SetColor(savedColor);
            g.PopFont();
        }
    }

    /// <summary>
    /// White text with a dark border. The game's DrawStringBold draws outlined text: its colour argument is
    /// the border and the fill is the current colour (SetColor). The fallback stamps the outline at 8 offsets.
    /// </summary>
    private static void DrawOutlined(Graphics g, string s, float x, float y)
    {
        int anchor = Graphics.ANCHOR_CENTER | Graphics.ANCHOR_MIDDLE;
        int outline = ParseColor(OutlineColor.Value, unchecked((int)0xFF1A2A3A));
        int fill = ParseColor(TextColor.Value, unchecked((int)0xFFFFFFFF));

        if (NativeOutline.Value)
        {
            g.SetColor(fill);
            g.DrawStringBold(s, x, y, outline, anchor);
            return;
        }
        int o = OutlineWidth.Value;
        if (o > 0)
            for (int dx = -o; dx <= o; dx += o)
                for (int dy = -o; dy <= o; dy += o)
                    if (dx != 0 || dy != 0) g.DrawString(s, x + dx, y + dy, anchor, outline);
        g.DrawString(s, x, y, anchor, fill);
    }

    private static int ParseColor(string hex, int fallback) =>
        uint.TryParse(hex.TrimStart('#').Replace("0x", ""), System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? unchecked((int)v) : fallback;

    [HarmonyPatch(typeof(SubForm), nameof(SubForm.OnTouchEvent))]
    [HarmonyPrefix]
    private static bool OnTouch(SubForm __instance, TouchEvent e, ref bool __result)
    {
        // Each tap arrives as ENTER(2), ON(13), DOWN(0), CLICK(5), UP(1), LEAVE(3), LOST(6); act on CLICK only.
        int value = e?.component?.value ?? int.MinValue;
        if (value != OurValue && value != SwitchValue && value != SellValue && value != SmithValue) return true;

        if (e.type == TouchEvent.TYPE_CLICK && value == SmithValue)
        {
            Log("投入所有相性相符 button clicked");
            SmithFeed.ButtonPressed(__instance);
        }
        else if (e.type == TouchEvent.TYPE_CLICK && value == SellValue)
        {
            Log("批量出售 button clicked");
            AutoSell.ButtonPressed(__instance);
        }
        else if (e.type == TouchEvent.TYPE_CLICK && value == SwitchValue)
        {
            if (__instance.type_ == JobChangePage)
            {
                var jc = __instance.jobChangeChar_;
                Log($"转职满级 button clicked for {jc?.GetName() ?? "null"}");
                JobSwitch.ButtonPressed(__instance, jc, FindHuman(jc));
            }
        }
        else if (e.type == TouchEvent.TYPE_CLICK)
        {
            Human h;
            Character c;
            if (__instance.type_ == JobChangePage)
            {
                c = __instance.jobChangeChar_;
                h = FindHuman(c);
            }
            else
            {
                h = HumanFor(__instance);
                c = h?.crew_?.TryCast<Character>();
            }
            string via = __instance.type_ == RosterMenu ? $" [{RosterCandidates()}]" : "";
            Log($"auto-feed button clicked in menu type {__instance.type_} for {c?.GetName() ?? "null"}{via}");
            AutoFeed.ButtonPressed(h, c, __instance);
        }
        __result = true;
        return false; // the game never sees our button's events
    }

    /// <summary>The Human whose crew_ is this character (needed only by Method = Game).</summary>
    private static Human FindHuman(Character c)
    {
        var all = main.AppData.GetInstance()?.allHumans_;
        if (c == null || all == null) return null;
        for (int i = 0; i < all.Size(); i++)
        {
            var h = all.Get(i)?.TryCast<Human>();
            if (h?.crew_ != null && h.crew_.Pointer == c.Pointer) return h;
        }
        return null;
    }
}
