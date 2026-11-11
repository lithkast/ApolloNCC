using System;
using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace ApolloNCC.Windows;

/// <summary>Apollo's black and gold look, plus the small drawing helpers every tab shares.</summary>
public static class Theme
{
    public static readonly Vector4 Gold = Rgb(0xD4, 0xAF, 0x37);
    public static readonly Vector4 GoldBright = Rgb(0xF1, 0xD0, 0x6B);
    public static readonly Vector4 GoldDim = Rgb(0x8C, 0x73, 0x26);
    public static readonly Vector4 Black = Rgb(0x0A, 0x0A, 0x0C);
    public static readonly Vector4 Panel = Rgb(0x14, 0x14, 0x17);
    public static readonly Vector4 PanelRaised = Rgb(0x1E, 0x1D, 0x21);
    public static readonly Vector4 Text = Rgb(0xEF, 0xE9, 0xD8);
    public static readonly Vector4 TextMuted = Rgb(0x9A, 0x94, 0x84);
    public static readonly Vector4 Good = Rgb(0x6F, 0xCF, 0x7F);
    public static readonly Vector4 Bad = Rgb(0xE5, 0x5B, 0x5B);
    public static readonly Vector4 Warn = Rgb(0xF0, 0xA0, 0x3C);

    private static readonly (ImGuiCol Slot, Vector4 Colour)[] Colours =
    [
        (ImGuiCol.Text, Text),
        (ImGuiCol.TextDisabled, TextMuted),
        (ImGuiCol.WindowBg, Black),
        (ImGuiCol.ChildBg, Panel),
        (ImGuiCol.PopupBg, Panel),
        (ImGuiCol.Border, GoldDim with { W = 0.55f }),
        (ImGuiCol.FrameBg, PanelRaised),
        (ImGuiCol.FrameBgHovered, Rgb(0x2A, 0x28, 0x2C)),
        (ImGuiCol.FrameBgActive, Rgb(0x33, 0x30, 0x2A)),
        (ImGuiCol.TitleBg, Black),
        (ImGuiCol.TitleBgActive, Rgb(0x1A, 0x16, 0x0A)),
        (ImGuiCol.TitleBgCollapsed, Black),
        (ImGuiCol.ScrollbarBg, Black),
        (ImGuiCol.ScrollbarGrab, GoldDim),
        (ImGuiCol.ScrollbarGrabHovered, Gold),
        (ImGuiCol.ScrollbarGrabActive, GoldBright),
        (ImGuiCol.CheckMark, Gold),
        (ImGuiCol.SliderGrab, Gold),
        (ImGuiCol.SliderGrabActive, GoldBright),
        (ImGuiCol.Button, Rgb(0x2B, 0x24, 0x0F)),
        (ImGuiCol.ButtonHovered, Rgb(0x4A, 0x3D, 0x14)),
        (ImGuiCol.ButtonActive, Rgb(0x6B, 0x58, 0x1C)),
        (ImGuiCol.Header, Rgb(0x2B, 0x24, 0x0F)),
        (ImGuiCol.HeaderHovered, Rgb(0x4A, 0x3D, 0x14)),
        (ImGuiCol.HeaderActive, Rgb(0x6B, 0x58, 0x1C)),
        (ImGuiCol.Separator, GoldDim with { W = 0.6f }),
        (ImGuiCol.ResizeGrip, GoldDim),
        (ImGuiCol.ResizeGripHovered, Gold),
        (ImGuiCol.ResizeGripActive, GoldBright),
        (ImGuiCol.Tab, Panel),
        (ImGuiCol.TabHovered, Rgb(0x4A, 0x3D, 0x14)),
        (ImGuiCol.TabActive, Rgb(0x3A, 0x30, 0x10)),
        (ImGuiCol.TabUnfocused, Panel),
        (ImGuiCol.TabUnfocusedActive, Rgb(0x2B, 0x24, 0x0F)),
        (ImGuiCol.TableHeaderBg, Rgb(0x1F, 0x1A, 0x0B)),
        (ImGuiCol.TableBorderStrong, GoldDim with { W = 0.6f }),
        (ImGuiCol.TableBorderLight, GoldDim with { W = 0.25f }),
        (ImGuiCol.TableRowBg, Panel),
        (ImGuiCol.TableRowBgAlt, Rgb(0x19, 0x18, 0x1C)),
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

    public static string Gil(long amount) => amount.ToString("N0", CultureInfo.InvariantCulture) + " gil";

    public static string Hours(double hours) => $"{hours:0.00} h";

    public static string Clock(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";

    public static string PersonAt(string name, string? world) => world is { Length: > 0 } ? $"{name}@{world}" : name;

    private static Vector4 Rgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1f);
}
