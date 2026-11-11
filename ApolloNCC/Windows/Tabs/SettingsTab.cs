using System;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class SettingsTab(Configuration config, ApolloState state)
{
    private string email = config.Email;
    private string password = "";

    public void Draw()
    {
        this.DrawAccount();
        this.DrawDiscord();
        this.DrawAlerts();
    }

    private void DrawAccount()
    {
        Theme.Section("Apollo web app account");
        if (state.IsSignedIn)
        {
            var access = state.Access;
            var who = access is { IsStaff: true } ? $"{state.MyName} ({access.RoleLabel})" : state.Email;
            ImGui.TextColored(Theme.Good, $"Signed in as {who}.");
            Theme.Muted(state.Email);
            if (ImGui.Button("Sign out"))
            {
                state.SignOut();
                this.password = "";
            }
        }
        else
        {
            Theme.Muted("Sign in with the email and password you use on apolloncc.org.");
            var width = 320 * Theme.Scale;
            ImGui.SetNextItemWidth(width);
            ImGui.InputText("Email", ref this.email, 120);
            ImGui.SetNextItemWidth(width);
            var enter = ImGui.InputText("Password", ref this.password, 120, ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);

            var ready = !string.IsNullOrWhiteSpace(this.email) && this.password.Length > 0 && !state.Busy;
            ImGui.BeginDisabled(!ready);
            if ((ImGui.Button("Sign in") || enter) && ready)
            {
                state.SignIn(this.email, this.password);
                this.password = "";
            }

            ImGui.EndDisabled();
        }

        ImGui.Spacing();
        Theme.Wrapped(Theme.TextMuted,
            "The sign-in is kept for this PC, not for one character, so every alt you play uses your staff login: " +
            "clocking in on an alt clocks in you. Your password is never saved.");
    }

    private void DrawDiscord()
    {
        Theme.Section("Discord (Hermes)");
        if (state.Access is not { IsStaff: true })
        {
            Theme.Muted("Sign in first.");
            return;
        }

        if (state.DiscordName != null)
        {
            ImGui.TextColored(Theme.Good, $"Linked to Discord{(state.DiscordName.Length > 0 ? $" as {state.DiscordName}" : "")}.");
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Button("Unlink"))
                state.UnlinkDiscord();
            ImGui.EndDisabled();
            return;
        }

        Theme.Muted("Not linked. Linking lets you use Hermes' commands in Discord as well.");
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.Button("Get a link code"))
            state.StartDiscordLink();
        ImGui.EndDisabled();

        if (state.DiscordCode is { } code && code.Expires > DateTimeOffset.UtcNow)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("In the Apollo Discord, type /hrlink with this code:");
            ImGui.SetWindowFontScale(1.5f);
            ImGui.TextColored(Theme.GoldBright, code.Code);
            ImGui.SetWindowFontScale(1f);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(code.Code);
            Theme.Muted($"Works once, for another {Theme.Clock(code.Expires - DateTimeOffset.UtcNow)}.");
        }
    }

    private void DrawAlerts()
    {
        Theme.Section("Alerts");
        var timeUp = config.BookingTimeUpAlerts;
        if (ImGui.Checkbox("Tell me when one of my bookings runs out of time", ref timeUp))
        {
            config.BookingTimeUpAlerts = timeUp;
            config.Save();
        }

        if (state.Access?.IsSecurity != true)
            return;

        var banned = config.BannedPatronAlerts;
        if (ImGui.Checkbox("Alert me when a blacklisted player is nearby or reported by other staff", ref banned))
        {
            config.BannedPatronAlerts = banned;
            config.Save();
        }
    }
}
