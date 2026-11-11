using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace ApolloNCC.Windows;

/// <summary>Apollo's look, taken from the web app, plus the small drawing helpers every tab shares.</summary>
public static class Theme
{
    // The site's dark theme (public/index.html): black, midnight blue panels, gold.
    public static readonly Vector4 Gold = Rgb(0xE2, 0xB9, 0x5F);
    public static readonly Vector4 GoldBright = Rgb(0xF2, 0xD4, 0x8C);
    public static readonly Vector4 GoldDim = Rgb(0x8A, 0x6E, 0x32);
    public static readonly Vector4 Black = Rgb(0x00, 0x00, 0x00);
    public static readonly Vector4 Panel = Rgb(0x16, 0x1B, 0x36);
    public static readonly Vector4 PanelRaised = Rgb(0x1D, 0x23, 0x45);
    public static readonly Vector4 Line = Rgb(0x2B, 0x32, 0x60);
    public static readonly Vector4 Text = Rgb(0xEC, 0xE9, 0xE0);
    public static readonly Vector4 TextMuted = Rgb(0xA3, 0xA9, 0xC6);
    public static readonly Vector4 Good = Rgb(0x4F, 0xB2, 0x86);
    public static readonly Vector4 Bad = Rgb(0xF2, 0x9A, 0x9A);
    public static readonly Vector4 Warn = Rgb(0xF0, 0xB4, 0x5A);

    private static readonly (ImGuiCol Slot, Vector4 Colour)[] Colours =
    [
        (ImGuiCol.Text, Text),
        (ImGuiCol.TextDisabled, TextMuted),
        (ImGuiCol.WindowBg, Black),
        (ImGuiCol.ChildBg, Panel),
        (ImGuiCol.PopupBg, Panel),
        (ImGuiCol.Border, Line),
        (ImGuiCol.FrameBg, PanelRaised),
        (ImGuiCol.FrameBgHovered, Line),
        (ImGuiCol.FrameBgActive, Rgb(0x36, 0x3F, 0x78)),
        (ImGuiCol.TitleBg, Black),
        (ImGuiCol.TitleBgActive, Panel),
        (ImGuiCol.TitleBgCollapsed, Black),
        (ImGuiCol.ScrollbarBg, Black),
        (ImGuiCol.ScrollbarGrab, Line),
        (ImGuiCol.ScrollbarGrabHovered, GoldDim),
        (ImGuiCol.ScrollbarGrabActive, Gold),
        (ImGuiCol.CheckMark, Gold),
        (ImGuiCol.SliderGrab, Gold),
        (ImGuiCol.SliderGrabActive, GoldBright),
        (ImGuiCol.Button, PanelRaised),
        (ImGuiCol.ButtonHovered, Line),
        (ImGuiCol.ButtonActive, GoldDim),
        (ImGuiCol.Header, PanelRaised),
        (ImGuiCol.HeaderHovered, Line),
        (ImGuiCol.HeaderActive, GoldDim),
        (ImGuiCol.Separator, Line),
        (ImGuiCol.ResizeGrip, Line),
        (ImGuiCol.ResizeGripHovered, GoldDim),
        (ImGuiCol.ResizeGripActive, Gold),
        (ImGuiCol.Tab, Panel),
        (ImGuiCol.TabHovered, Line),
        (ImGuiCol.TabActive, GoldDim),
        (ImGuiCol.TabUnfocused, Panel),
        (ImGuiCol.TabUnfocusedActive, PanelRaised),
        (ImGuiCol.TableHeaderBg, PanelRaised),
        (ImGuiCol.TableBorderStrong, Line),
        (ImGuiCol.TableBorderLight, Line with { W = 0.5f }),
        (ImGuiCol.TableRowBg, Panel),
        (ImGuiCol.TableRowBgAlt, Rgb(0x1A, 0x1F, 0x3D)),
        (ImGuiCol.TextSelectedBg, Gold with { W = 0.35f }),
        (ImGuiCol.NavHighlight, Gold),
    ];

    public static float Scale => ImGuiHelpers.GlobalScale;

    public static void Push()
    {
        foreach (var (slot, colour) in Colours)
            ImGui.PushStyleColor(slot, colour);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f);
    }

    public static void Pop()
    {
        ImGui.PopStyleVar(6);
        ImGui.PopStyleColor(Colours.Length);
    }

    /// <summary>Gold section title with a rule under it.</summary>
    public static void Section(string title)
    {
        ImGui.Spacing();
        ImGui.TextColored(Gold, title.ToUpperInvariant());
        ImGui.Separator();
        ImGui.Spacing();
    }

    /// <summary>A labelled figure, laid out left to right.</summary>
    public static void Stat(string label, string value, Vector4? colour = null)
    {
        ImGui.BeginGroup();
        ImGui.TextColored(TextMuted, label);
        ImGui.TextColored(colour ?? GoldBright, value);
        ImGui.EndGroup();
        ImGui.SameLine(0, 28 * Scale);
    }

    public static void EndStats() => ImGui.NewLine();

    public static void Muted(string text) => ImGui.TextColored(TextMuted, text);

    public static void Wrapped(Vector4 colour, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, colour);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    public static string Clock(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";

    public static string PersonAt(string name, string? world) => world is { Length: > 0 } ? $"{name}@{world}" : name;

    private static Vector4 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
