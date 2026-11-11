using System;
using System.Collections.Generic;
using System.Linq;
using ApolloNCC.Api;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

/// <summary>The bookings list shown on HR, Reception and Security, with whichever columns each needs.</summary>
public static class BookingTable
{
    [Flags]
    public enum Columns
    {
        None = 0,
        Staff = 1,
        Client = 2,
        BookedBy = 4,
        Amount = 8,
    }

    /// <param name="actions">Optional extra cell per row, e.g. reception's buttons.</param>
    public static void Draw(string id, IReadOnlyList<Booking> bookings, DateTimeOffset now, Columns columns, Action<Booking>? actions = null)
    {
        if (bookings.Count == 0)
        {
            Theme.Muted("No bookings right now.");
            return;
        }

        var count = 2 + BitCount((int)columns) + (actions != null ? 1 : 0);
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable(id, count, flags))
            return;

        if (columns.HasFlag(Columns.Staff))
            ImGui.TableSetupColumn("Staff", ImGuiTableColumnFlags.None, 2f);
        if (columns.HasFlag(Columns.Client))
            ImGui.TableSetupColumn("Client", ImGuiTableColumnFlags.None, 2.4f);
        ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.None, 1.4f);
        if (columns.HasFlag(Columns.BookedBy))
            ImGui.TableSetupColumn("Booked by", ImGuiTableColumnFlags.None, 2f);
        if (columns.HasFlag(Columns.Amount))
            ImGui.TableSetupColumn("Client pays", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Time left", ImGuiTableColumnFlags.None, 1.2f);
        if (actions != null)
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.None, 3f);
        ImGui.TableHeadersRow();

        foreach (var b in bookings.OrderBy(b => b.EndsAt))
        {
            ImGui.PushID(b.Id);
            ImGui.TableNextRow();
            if (columns.HasFlag(Columns.Staff))
                Cell(b.StaffName);
            if (columns.HasFlag(Columns.Client))
                Cell(Theme.PersonAt(b.ClientName, b.ClientWorld));
            Cell(b.Room);
            if (columns.HasFlag(Columns.BookedBy))
                Cell(b.BookedByName);
            if (columns.HasFlag(Columns.Amount))
                Cell(Theme.Gil(b.Amount));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var left = b.Remaining(now);
            if (left == TimeSpan.Zero)
                ImGui.TextColored(Theme.Bad, "TIME UP");
            else
                ImGui.TextColored(left < TimeSpan.FromMinutes(5) ? Theme.Warn : Theme.Text, Theme.Clock(left));

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

    private static int BitCount(int value) => System.Numerics.BitOperations.PopCount((uint)value);
}
