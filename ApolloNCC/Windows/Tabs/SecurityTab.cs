using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class SecurityTab(ApolloState state, Alerts alerts)
{
    private const ImGuiTableFlags TableFlags =
        ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;

    private string search = "";
    private string warnName = "";
    private string warnWorld = "";
    private string warnReason = "";

    public void Draw(Access access)
    {
        // Every staff member gets the blacklist. The rest is for security and the owners.
        if (!access.IsSecurity)
        {
            this.DrawBlacklist();
            return;
        }

        if (!ImGui.BeginTabBar("security"))
            return;

        if (ImGui.BeginTabItem("Blacklist"))
        {
            this.DrawBlacklist();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Bookings"))
        {
            var now = DateTimeOffset.UtcNow;
            Theme.Section("Current bookings");
            BookingTable.Draw("security", state.Bookings.Where(b => b.IsRunning(now)), now,
                BookingTable.Columns.Employee | BookingTable.Columns.Patron | BookingTable.Columns.Type);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Warnings"))
        {
            this.DrawWarnings();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Banned sightings"))
        {
            this.DrawSightings();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawBlacklist()
    {
        Theme.Section("Blacklist");
        ImGui.SetNextItemWidth(280 * Theme.Scale);
        ImGui.InputTextWithHint("##search", "Search name, reason or notes", ref this.search, 64);
        ImGui.SameLine();
        Theme.Muted($"{state.Bans.Count} banned");

        var needle = this.search.Trim();
        bool Has(string s) => s.Contains(needle, StringComparison.OrdinalIgnoreCase);
        var rows = state.Bans
            .Where(b => needle.Length == 0 || Has(b.Name) || Has(b.Reason) || Has(b.Notes) || Has(b.By))
            .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (rows.Count == 0)
        {
            Theme.Muted(needle.Length > 0 ? "No match. They are not on the blacklist." : "Loading the blacklist...");
            return;
        }

        if (!ImGui.BeginTable("blacklist", 5, TableFlags | ImGuiTableFlags.ScrollY))
            return;

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Banned player", ImGuiTableColumnFlags.None, 2.2f);
        ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.None, 1.1f);
        ImGui.TableSetupColumn("Authorized by", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 2.4f);
        ImGui.TableSetupColumn("Notes", ImGuiTableColumnFlags.None, 3f);
        ImGui.TableHeadersRow();
        foreach (var b in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.Bad, b.Name);
            ImGui.TableNextColumn();
            Theme.Muted(b.Date.Length >= 10 ? b.Date[..10] : b.Date);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(b.By);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(b.Reason);
            ImGui.TableNextColumn();
            ImGui.TextWrapped(b.Notes);
        }

        ImGui.EndTable();
    }

    private void DrawWarnings()
    {
        Theme.Section("Log a warning");
        if (state.SecurityStoreMissing)
        {
            Theme.Wrapped(Theme.Warn, "The web app can't store warnings yet: its plugin update needs deploying first. Until then nothing here is saved.");
            return;
        }

        TargetPicker.Draw("warn", ref this.warnName, ref this.warnWorld);

        var name = this.warnName.Trim();
        var world = this.warnWorld.Trim();
        if (name.Length > 0 && alerts.Match(name, world) != null)
            ImGui.TextColored(Theme.Bad, "This player is on the blacklist.");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##reason", "Brief reason for the warning", ref this.warnReason, 300);

        var ready = name.Length > 0 && !string.IsNullOrWhiteSpace(this.warnReason);
        ImGui.BeginDisabled(!ready || state.Busy);
        if (ImGui.Button("Submit warning"))
        {
            state.AddWarning(name, world, this.warnReason.Trim(), () =>
            {
                this.warnName = this.warnWorld = this.warnReason = "";
            });
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        Theme.Muted("Post proof in the Discord security channel once submitted.");

        Theme.Section("Warnings on record");
        var warnings = state.Warnings;
        if (warnings.Count == 0)
        {
            Theme.Muted("No warnings logged.");
            return;
        }

        if (!ImGui.BeginTable("warnings", 4, TableFlags))
            return;

        ImGui.TableSetupColumn("Patron", ImGuiTableColumnFlags.None, 2.4f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 4f);
        ImGui.TableSetupColumn("Given by", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableHeadersRow();
        foreach (var w in warnings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Theme.PersonAt(w.Name, w.World));
            var count = warnings.Count(o => SamePatron(o, w));
            if (count > 1)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Warn, $"x{count}");
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(w.Reason);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(w.By);
            ImGui.TableNextColumn();
            Theme.Muted($"{w.At.LocalDateTime:g}");
        }

        ImGui.EndTable();
    }

    private void DrawSightings()
    {
        Theme.Section("Banned players seen in the venue");
        Theme.Wrapped(Theme.TextMuted,
            "While alerts are on, the plugin checks players near you against the blacklist. A match alerts you, and the rest of security within half a minute.");
        ImGui.Spacing();
        if (state.SecurityStoreMissing)
            Theme.Wrapped(Theme.Warn, "Telling the rest of security needs the web app's plugin update deployed. Until then only you are alerted.");

        var sightings = state.Sightings;
        if (sightings.Count == 0)
        {
            Theme.Muted("No banned players have been seen.");
            return;
        }

        if (!ImGui.BeginTable("sightings", 4, TableFlags))
            return;

        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.None, 2.4f);
        ImGui.TableSetupColumn("Banned for", ImGuiTableColumnFlags.None, 4f);
        ImGui.TableSetupColumn("Seen by", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableSetupColumn("When", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableHeadersRow();
        foreach (var s in sightings)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.Bad, Theme.PersonAt(s.Name, s.World));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(s.Ban);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.By);
            ImGui.TableNextColumn();
            Theme.Muted($"{s.At.LocalDateTime:g}");
        }

        ImGui.EndTable();
    }

    private static bool SamePatron(PatronWarning a, PatronWarning b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.World, b.World, StringComparison.OrdinalIgnoreCase);
}
