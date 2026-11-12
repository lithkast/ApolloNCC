using System;
using System.Numerics;
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
    private readonly BarTab bar;
    private readonly CasinoTab casino;
    private readonly RaffleTab raffle;

    private bool jumpToSettings;

    public MainWindow(Configuration config, ApolloState state, Alerts alerts, MacroRunner macros)
        : base("Apollo Night Club & Casino###ApolloNCC")
    {
        this.state = state;
        this.hr = new HrTab(state);
        this.reception = new ReceptionTab(state);
        this.security = new SecurityTab(state, alerts);
        this.settings = new SettingsTab(config, state);
        this.bar = new BarTab(state, macros);
        this.casino = new CasinoTab(state);
        this.raffle = new RaffleTab(state, macros);

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

    public void Dispose() => this.bar.Dispose();

    public override void PreDraw() => Theme.Push();

    public override void PostDraw() => Theme.Pop();

    public override void Draw()
    {
        this.DrawHeader();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetTextLineHeight();
        if (ImGui.BeginChild("body", new Vector2(0, -footer), false, ImGuiWindowFlags.NoBackground))
        {
            if (ImGui.BeginTabBar("tabs"))
            {
                var access = this.state.Access is { IsStaff: true } a ? a : null;
                this.Tab("HR", View.Hr, () => this.Gate(this.hr.Draw));
                if (access?.CanUseReception == true)
                    this.Tab("Reception", View.Reception, () => this.reception.Draw(access));
                this.Tab("Security", View.Security, () => this.Gate(this.security.Draw));
                this.Tab("Bar", View.Bar, () => this.Gate(this.bar.Draw));
                if (access?.CanViewCasino == true)
                    this.Tab("Casino", View.Casino, () => this.casino.Draw(access));

                // the raffle is the owners' and co-owners'
                if (access?.IsOwner == true)
                    this.Tab("Raffle", View.Raffle, () => this.raffle.Draw(access));

                var flags = this.jumpToSettings ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
                this.jumpToSettings = false;
                this.Tab("Settings", View.Other, this.settings.Draw, flags);
                ImGui.EndTabBar();
            }
        }

        ImGui.EndChild();
        this.DrawFooter();
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

    private void Gate(Action<Access> draw)
    {
        var access = this.state.Access;
        if (access is { IsStaff: true })
        {
            draw(access);
            return;
        }

        ImGui.Spacing();
        if (!this.state.IsSignedIn)
        {
            ImGui.TextColored(Theme.Gold, "Welcome to Apollo.");
            ImGui.TextWrapped("This plugin is for Apollo staff. Sign in with the email and password you use on apolloncc.org.");
            if (ImGui.Button("Open Settings"))
                this.ShowSettings();
        }
        else if (access != null)
        {
            ImGui.TextWrapped("This account isn't on the staff yet, so there is nothing to show. An owner makes an account staff on the site.");
        }
        else if (this.state.Error != null)
        {
            ImGui.TextWrapped("Could not load your login from the Apollo web app. The plugin keeps trying.");
        }
        else
        {
            Theme.Muted("Loading your login...");
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

        if (this.state.Access is { IsStaff: true } access)
        {
            var row = this.state.MyRow;
            var now = DateTimeOffset.UtcNow;
            var onShift = row != null && Venue.OnClock(row, now);
            var right = $"{this.state.MyName}  |  {access.RoleLabel}  |  ";
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
            Theme.Wrapped(Theme.Bad, error);
        else if (this.state.Busy)
            Theme.Muted("Working...");
        else if (this.state.Notice is { } notice)
            Theme.Wrapped(Theme.Gold, notice);
        else
            Theme.Muted(this.state.Access != null ? "Connected to the Apollo web app." : " ");
    }
}
