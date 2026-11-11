using System;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class SettingsTab
{
    private readonly Configuration config;
    private readonly ApolloState state;
    private readonly Alerts alerts;

    private string baseUrl;
    private string secretKey;

    public SettingsTab(Configuration config, ApolloState state, Alerts alerts)
    {
        this.config = config;
        this.state = state;
        this.alerts = alerts;
        this.baseUrl = config.BaseUrl;
        this.secretKey = config.SecretKey;
    }

    public void Draw()
    {
        this.DrawAccount();
        this.DrawDiscord();
        this.DrawAlerts();
        this.DrawDemo();
    }

    private void DrawAccount()
    {
        Theme.Section("Apollo web app account");
        var profile = this.state.Profile;
        if (this.config.DemoMode)
            ImGui.TextColored(Theme.Warn, "Demo mode is on. Nothing is sent to the web app.");
        else if (profile != null)
            ImGui.TextColored(Theme.Good, $"Linked as {profile.Name} ({profile.Title()}).");
        else
            Theme.Muted(this.state.IsConfigured ? "Not linked yet." : "Not linked. Ask an owner for your plugin key.");

        var width = 360 * Theme.Scale;
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("Plugin key", ref this.secretKey, 200, ImGuiInputTextFlags.Password);
        ImGui.SetNextItemWidth(width);
        ImGui.InputText("Web app address", ref this.baseUrl, 200);

        var changed = this.secretKey.Trim() != this.config.SecretKey || this.baseUrl.Trim() != this.config.BaseUrl;
        ImGui.BeginDisabled(string.IsNullOrWhiteSpace(this.secretKey) || string.IsNullOrWhiteSpace(this.baseUrl));
        if (ImGui.Button(changed ? "Save and link" : "Link again"))
        {
            this.config.SecretKey = this.secretKey.Trim();
            this.config.BaseUrl = this.baseUrl.Trim();
            this.config.Save();
            this.Reconnect();
        }

        ImGui.EndDisabled();
        if (this.config.SecretKey.Length > 0)
        {
            ImGui.SameLine();
            if (ImGui.Button("Unlink"))
            {
                this.config.SecretKey = this.secretKey = "";
                this.config.Save();
                this.Reconnect();
            }
        }

        ImGui.Spacing();
        Theme.Wrapped(Theme.TextMuted,
            "Your key is your staff login: it is tied to your main character, and every character on this PC " +
            "uses it, so clocking in on an alt clocks in your main. Never share it. If it leaks, ask an owner for a new one.");
    }

    private void DrawDiscord()
    {
        Theme.Section("Discord");
        var profile = this.state.Profile;
        if (profile == null)
        {
            Theme.Muted("Link your web app account first.");
            return;
        }

        if (profile.DiscordLinked)
            ImGui.TextColored(Theme.Good, $"Linked to Discord{(profile.DiscordName is { } n ? $" as {n}" : "")}.");
        else
            Theme.Muted("Not linked to Discord.");

        ImGui.BeginDisabled(this.state.Busy);
        if (ImGui.Button(profile.DiscordLinked ? "Link a different Discord account" : "Get a Discord link code"))
            this.state.StartDiscordLink();
        ImGui.EndDisabled();

        if (this.state.DiscordCode is { } code && code.ExpiresAt > DateTimeOffset.UtcNow)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Send this code to the Apollo bot in Discord:");
            ImGui.SetWindowFontScale(1.5f);
            ImGui.TextColored(Theme.GoldBright, code.Code);
            ImGui.SetWindowFontScale(1f);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(code.Code);
            Theme.Muted($"Expires in {Theme.Clock(code.ExpiresAt - DateTimeOffset.UtcNow)}.");
        }
    }

    private void DrawAlerts()
    {
        Theme.Section("Alerts");
        var timeUp = this.config.BookingTimeUpAlerts;
        if (ImGui.Checkbox("Tell me when one of my bookings runs out of time", ref timeUp))
        {
            this.config.BookingTimeUpAlerts = timeUp;
            this.config.Save();
        }

        if (this.state.Profile?.CanUseSecurityTools() != true)
            return;

        var banned = this.config.BannedPatronAlerts;
        if (ImGui.Checkbox("Alert me when a banned patron is nearby or reported by other staff", ref banned))
        {
            this.config.BannedPatronAlerts = banned;
            this.config.Save();
        }
    }

    private void DrawDemo()
    {
        Theme.Section("Demo mode");
        var demo = this.config.DemoMode;
        if (ImGui.Checkbox("Use made-up data instead of the web app", ref demo))
        {
            this.config.DemoMode = demo;
            this.config.Save();
            this.Reconnect();
        }

        if (!demo)
        {
            Theme.Muted("For looking around the plugin without a login. Nothing you do in demo mode is saved.");
            return;
        }

        ImGui.SetNextItemWidth(220 * Theme.Scale);
        if (ImGui.BeginCombo("View as", this.config.DemoRank.ToString()))
        {
            foreach (var rank in Enum.GetValues<Rank>())
            {
                if (ImGui.Selectable(rank.ToString(), rank == this.config.DemoRank))
                {
                    this.config.DemoRank = rank;
                    this.config.Save();
                    this.state.RefreshSoon();
                }
            }

            ImGui.EndCombo();
        }
    }

    private void Reconnect()
    {
        this.state.Reconnect();
        this.alerts.Reset();
    }
}
