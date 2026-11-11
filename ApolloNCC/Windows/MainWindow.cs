using System;
using System.Numerics;
using ApolloNCC.Api;
using ApolloNCC.Services;
using ApolloNCC.Windows.Tabs;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace ApolloNCC.Windows;

public sealed class MainWindow : Window
{
    private readonly ApolloState state;
    private readonly HrTab hr;
    private readonly ReceptionTab reception;
    private readonly SecurityTab security;
    private readonly SettingsTab settings;

    private bool jumpToSettings;

    public MainWindow(Configuration config, ApolloState state, Alerts alerts)
        : base("Apollo Night Club & Casino###ApolloNCC")
    {
        this.state = state;
        this.hr = new HrTab(state);
        this.reception = new ReceptionTab(state);
        this.security = new SecurityTab(state, alerts);
        this.settings = new SettingsTab(config, state, alerts);

        this.SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 480),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
        this.Size = new Vector2(900, 620);
        this.SizeCondition = ImGuiCond.FirstUseEver;
    }

    /// <summary>The tab on screen, so only its data is kept fresh.</summary>
    public View CurrentView { get; private set; } = View.Hr;

    public void ShowSettings() => this.jumpToSettings = true;

    public override void PreDraw() => Theme.Push();

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        this.DrawHeader();

        var footer = ImGui.GetFrameHeightWithSpacing();
        if (ImGui.BeginChild("body", new Vector2(0, -footer), false, ImGuiWindowFlags.NoBackground))
        {
            if (ImGui.BeginTabBar("tabs"))
            {
                var profile = this.state.Profile;
                this.Tab("HR", View.Hr, () => this.Gate(profile, this.hr.Draw));
                if (profile?.CanUseReception() == true)
                    this.Tab("Reception", View.Reception, () => this.reception.Draw(profile));
                this.Tab("Security", View.Security, () => this.Gate(profile, this.security.Draw));
                this.Tab("Bar", View.Other, () => ComingSoon("Bar", "Bar buyouts and tab tracking"));
                this.Tab("Casino", View.Other, () => ComingSoon("Casino", "Casino tracking"));
                this.Tab("Raffle", View.Other, () => ComingSoon("Raffle", "Raffle ticket sales and draws"));

                var flags = this.jumpToSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                this.jumpToSettings = false;
                this.Tab("Settings", View.Other, this.settings.Draw, flags);
                ImGui.EndTabBar();
            }
        }

        ImGui.EndChild();
        this.DrawFooter();
    }

    private static void ComingSoon(string name, string what)
    {
        Theme.Section(name);
        Theme.Muted($"{what} will live here. This section has not been built yet.");
    }

    private void Tab(string label, View view, Action draw, ImGuiTabItemFlags flags = ImGuiTabItemFlags.None)
    {
        if (!ImGui.BeginTabItem(label, flags))
            return;

        if (this.CurrentView != view)
        {
            this.CurrentView = view;
            this.state.RefreshSoon();
        }

        if (ImGui.BeginChild("content", Vector2.Zero, false, ImGuiWindowFlags.NoBackground))
            draw();
        ImGui.EndChild();
        ImGui.EndTabItem();
    }

    private void Gate(Profile? profile, Action<Profile> draw)
    {
        if (profile != null)
        {
            draw(profile);
            return;
        }

        ImGui.Spacing();
        if (!this.state.IsConfigured)
        {
            ImGui.TextColored(Theme.Gold, "Welcome to Apollo.");
            ImGui.TextWrapped("This plugin is for Apollo staff. Enter the plugin key an owner gave you to get started.");
            if (ImGui.Button("Open Settings"))
                this.ShowSettings();
        }
        else if (this.state.Error != null)
        {
            ImGui.TextWrapped("Could not sign in to the Apollo web app. Check your key under Settings; the plugin keeps retrying.");
        }
        else
        {
            Theme.Muted("Signing in to the Apollo web app...");
        }
    }

    private void DrawHeader()
    {
        var scale = Theme.Scale;
        ImGui.SetWindowFontScale(1.7f);
        ImGui.TextColored(Theme.Gold, "APOLLO");
        ImGui.SetWindowFontScale(1f);
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (9 * scale));
        Theme.Muted("NIGHT CLUB & CASINO");

        var profile = this.state.Profile;
        if (profile != null)
        {
            var status = this.state.MyStatus;
            var onShift = status?.ClockedIn == true;
            var right = $"{profile.Name}  |  {profile.Title()}  |  ";
            var shift = onShift ? "On the clock" : "Off the clock";
            var width = ImGui.CalcTextSize(right + shift).X;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - width);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (9 * scale));
            ImGui.TextUnformatted(right);
            ImGui.SameLine(0, 0);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (9 * scale));
            ImGui.TextColored(onShift ? Theme.Good : Theme.TextMuted, shift);
        }

        // Gold rule under the masthead.
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.GetContentRegionAvail().X, 2 * scale);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            min, max,
            ImGui.GetColorU32(Theme.Gold), ImGui.GetColorU32(Theme.GoldDim with { W = 0.1f }),
            ImGui.GetColorU32(Theme.GoldDim with { W = 0.1f }), ImGui.GetColorU32(Theme.Gold));
        ImGui.Dummy(new Vector2(0, 6 * scale));
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        if (this.state.Error is { } error)
            ImGui.TextColored(Theme.Bad, error);
        else if (this.state.Busy)
            Theme.Muted("Working...");
        else if (this.state.Notice is { } notice)
            ImGui.TextColored(Theme.Gold, notice);
        else
            Theme.Muted(this.state.IsLinked ? "Connected to the Apollo web app." : " ");
    }
}
