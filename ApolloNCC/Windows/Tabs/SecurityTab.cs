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

    public void Draw(Profile profile)
    {
        // Every staff member gets the blacklist. The rest is for security and owners.
        if (!profile.CanUseSecurityTools())
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
            Theme.Section("Current bookings");
            BookingTable.Draw("security", state.SecurityBookings, DateTimeOffset.UtcNow,
                BookingTable.Columns.Staff | BookingTable.Columns.Client);
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
        ImGui.InputTextWithHint("##search", "Search name, world or reason", ref this.search, 64);
        ImGui.SameLine();
        Theme.Muted($"{state.Blacklist.Count} banned");

        var needle = this.search.Trim();
        var rows = state.Blacklist
            .Where(e => needle.Length == 0 ||
                        e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                        (e.World?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        e.Reason.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.Name)
            .ToList();

        if (rows.Count == 0)
        {
            Theme.Muted(needle.Length == 0 ? "Nobody is on the blacklist." : "No match. They are not on the blacklist.");
            return;
        }

        if (!ImGui.BeginTable("blacklist", 4, TableFlags))
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2f);
        ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.None, 4f);
        ImGui.TableSetupColumn("Added", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableHeadersRow();
        foreach (var e in rows)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(Theme.Bad, e.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.World ?? "Any");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(e.Reason);
            ImGui.TableNextColumn();
            Theme.Muted(e.AddedAt is { } at ? $"{at.LocalDateTime:d}" : "");
            if (e.AddedBy != null && ImGui.IsItemHovered())
                ImGui.SetTooltip($"Added by {e.AddedBy}");
        }

        ImGui.EndTable();
    }

    private void DrawWarnings()
    {
        Theme.Section("Log a warning");
        TargetPicker.Draw("warn", ref this.warnName, ref this.warnWorld);

        var name = this.warnName.Trim();
        var world = this.warnWorld.Trim();
        if (name.Length > 0 && alerts.Match(name, world.Length > 0 ? world : null) != null)
            ImGui.TextColored(Theme.Bad, "This patron is on the blacklist.");

        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##reason", "Brief reason for the warning", ref this.warnReason, 300);

        var ready = name.Length > 0 && !string.IsNullOrWhiteSpace(this.warnReason);
        ImGui.BeginDisabled(!ready || state.Busy);
        if (ImGui.Button("Submit warning"))
        {
            state.AddWarning(name, world.Length > 0 ? world : null, this.warnReason.Trim(), () =>
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
            var prior = warnings.Count(o => SamePatron(o, w));
            ImGui.TextUnformatted(Theme.PersonAt(w.Name, w.World));
            if (prior > 1)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Warn, $"x{prior}");
            }

            ImGui.TableNextColumn();
            ImGui.TextWrapped(w.Reason);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(w.IssuedBy);
            ImGui.TableNextColumn();
            Theme.Muted($"{w.IssuedAt.LocalDateTime:g}");
        }

        ImGui.EndTable();
    }

    private void DrawSightings()
    {
        Theme.Section("Banned patrons seen in the venue");
        Theme.Wrapped(Theme.TextMuted,
            "While alerts are on, the plugin checks players near you against the blacklist and tells all security staff about a match.");
        ImGui.Spacing();

        var sightings = state.Sightings;
        if (sightings.Count == 0)
        {
            Theme.Muted("No banned patrons have been seen.");
            return;
        }

        if (!ImGui.BeginTable("sightings", 4, TableFlags))
            return;

        ImGui.TableSetupColumn("Patron", ImGuiTableColumnFlags.None, 2.4f);
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
            ImGui.TextWrapped(s.Reason);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.ReportedBy);
            ImGui.TableNextColumn();
            Theme.Muted($"{s.SeenAt.LocalDateTime:g}");
        }

        ImGui.EndTable();
    }

    private static bool SamePatron(PatronWarning a, PatronWarning b) =>
        string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(a.World, b.World, StringComparison.OrdinalIgnoreCase);
}
