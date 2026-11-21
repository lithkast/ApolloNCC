using System;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class SettingsTab(Configuration config, ApolloState state, Greeter greeter)
{
    private string email = config.Email;
    private string password = "";

    public void Draw()
    {
        this.DrawAccount();
        this.DrawDiscord();
        this.DrawGreeter();
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

    /// <summary>Greeter mode: a welcome tell for each arrival, run by one manager or owner at a time.</summary>
    private void DrawGreeter()
    {
        if (state.Access?.CanGreet != true)
            return;

        Theme.Section("Greeter mode");
        var now = DateTimeOffset.UtcNow;
        var blocked = greeter.Blocked(now);
        var on = greeter.Enabled;
        ImGui.BeginDisabled((blocked != null && !on) || state.Busy);
        if (ImGui.Checkbox("Send a welcome tell to everyone who comes into the venue", ref on))
        {
            if (on)
                greeter.TurnOn(now);
            else
                greeter.TurnOff();
        }

        ImGui.EndDisabled();
        if (blocked != null && !greeter.Enabled)
            ImGui.TextColored(Theme.Warn, blocked);
        else
            ImGui.TextColored(greeter.Enabled ? Theme.Good : Theme.TextMuted, greeter.Status);

        if (!greeter.Enabled && state.GreeterUid.Length > 0 && state.GreeterUid != state.Uid)
            Theme.Muted($"{(state.GreeterName.Length > 0 ? state.GreeterName : "Someone")} is greeting tonight. Switching yours on takes over from them.");
        if (greeter.GreetedTonight > 0)
            Theme.Muted($"Greeted {greeter.GreetedTonight} tonight.");

        ImGui.Spacing();
        Theme.Muted("The welcome message. {name} becomes their first name.");
        var message = config.GreeterMessage;
        if (ImGui.InputTextMultiline("##greeting", ref message, 400, new System.Numerics.Vector2(-1, 64 * Theme.Scale)))
            config.GreeterMessage = message;
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();

        Theme.Muted("They will see:");
        Theme.Wrapped(Theme.Text, greeter.MessageFor("Eris Descordia"));
        if (config.GreeterMessage != Configuration.DefaultGreeterMessage)
        {
            if (ImGui.SmallButton("Use the standard message"))
            {
                config.GreeterMessage = Configuration.DefaultGreeterMessage;
                config.Save();
            }
        }

        ImGui.Spacing();
        Theme.Wrapped(Theme.TextMuted,
            "It only runs inside the venue while you're on the clock, and switches off when you clock out. Each guest is " +
            "greeted once a night; staff and anyone on the blacklist are skipped, and so is everyone already inside when you switch it on.");
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
