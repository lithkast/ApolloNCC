using System;
using System.Collections.Generic;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

/// <summary>The bookings list shown on HR, Reception and Security, with whichever columns each needs.</summary>
public static class BookingTable
{
    [Flags]
    public enum Columns
    {
        None = 0,
        Employee = 1,
        Patron = 2,
        Type = 4,
        Total = 8,
    }

    /// <param name="actions">Optional extra cell per row, e.g. reception's buttons.</param>
    public static void Draw(string id, IEnumerable<Booking> bookings, DateTimeOffset now, Columns columns, Action<Booking>? actions = null)
    {
        // running ones first, soonest to end at the top; finished ones after
        var rows = bookings
            .OrderBy(b => b.IsRunning(now) ? 0 : 1)
            .ThenBy(b => b.EndsAt ?? DateTimeOffset.MaxValue)
            .ToList();
        if (rows.Count == 0)
        {
            Theme.Muted("No bookings right now.");
            return;
        }

        var count = 2 + System.Numerics.BitOperations.PopCount((uint)columns) + (actions != null ? 1 : 0);
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable(id, count, flags))
            return;

        if (columns.HasFlag(Columns.Employee))
            ImGui.TableSetupColumn("Staff", ImGuiTableColumnFlags.None, 2f);
        if (columns.HasFlag(Columns.Patron))
            ImGui.TableSetupColumn("Patron", ImGuiTableColumnFlags.None, 2.2f);
        if (columns.HasFlag(Columns.Type))
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.None, 1.1f);
        ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.None, 1.6f);
        if (columns.HasFlag(Columns.Total))
            ImGui.TableSetupColumn("Patron pays", ImGuiTableColumnFlags.None, 1.4f);
        ImGui.TableSetupColumn("Time left", ImGuiTableColumnFlags.None, 1.2f);
        if (actions != null)
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableHeadersRow();

        foreach (var b in rows)
        {
            ImGui.PushID(b.Id);
            ImGui.TableNextRow();
            if (columns.HasFlag(Columns.Employee))
                Cell(b.Employee.Length > 0 ? b.Employee : "-");
            if (columns.HasFlag(Columns.Patron))
                Cell(b.Patron.Length > 0 ? b.Patron + (b.Vip ? "  (VIP)" : "") : "-");
            if (columns.HasFlag(Columns.Type))
                Cell(b.RpType);
            Cell(b.Location);
            if (columns.HasFlag(Columns.Total))
                Cell(Venue.Gil(b.Total));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (b.EndsAt is not { } end)
            {
                Theme.Muted("not started");
            }
            else if (end <= now)
            {
                ImGui.TextColored(Theme.Bad, "TIME UP");
            }
            else
            {
                var left = end - now;
                ImGui.TextColored(left < TimeSpan.FromMinutes(5) ? Theme.Warn : Theme.Text, Theme.Clock(left));
            }

            if (actions != null)
            {
                ImGui.TableNextColumn();
                actions(b);
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private static void Cell(string text)
    {
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(text);
    }
}
