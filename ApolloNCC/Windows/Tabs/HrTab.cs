using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class HrTab(ApolloState state)
{
    private bool pfPartyFinder;
    private bool pfShouts;

    public void Draw(Profile profile)
    {
        var now = DateTimeOffset.UtcNow;
        var me = state.MyStatus;
        if (me == null)
        {
            Theme.Muted("Loading your shift...");
            return;
        }

        this.DrawMyShift(profile, me, now);
        this.DrawMyBookings(now);
        if (profile.CanSeeStaff())
            this.DrawStaff(profile, now);
    }

    private void DrawMyShift(Profile profile, EmployeeStatus me, DateTimeOffset now)
    {
        Theme.Section(profile.Name);

        using (Disabled(me.ClockedIn || state.Busy))
        {
            if (ImGui.Button("Clock In", ButtonSize()))
                state.ClockIn();
        }

        ImGui.SameLine();
        using (Disabled(!me.ClockedIn || state.Busy))
        {
            if (ImGui.Button("Clock Out", ButtonSize()))
                state.ClockOut();
        }

        ImGui.SameLine();
        if (me.ClockInPendingApproval)
            ImGui.TextColored(Theme.Warn, "Early clock-in: waiting for manager approval");
        else if (!me.ClockedIn && VenueClock.ClockInNeedsApproval(now))
            Theme.Muted("Clocking in before 8:53 PM Eastern needs manager approval.");
        else
            ImGui.NewLine();

        // Party Finder / shout duty is clocked separately.
        using (Disabled(me.PfClockedIn))
        {
            ImGui.Checkbox("Party Finder", ref this.pfPartyFinder);
            ImGui.SameLine();
            ImGui.Checkbox("Shouts", ref this.pfShouts);
        }

        ImGui.SameLine();
        var nothingTicked = !this.pfPartyFinder && !this.pfShouts;
        using (Disabled(me.PfClockedIn || nothingTicked || state.Busy))
        {
            if (ImGui.Button("PF Clock In", ButtonSize()))
                state.PfClockIn(this.pfPartyFinder, this.pfShouts);
        }

        ImGui.SameLine();
        using (Disabled(!me.PfClockedIn || state.Busy))
        {
            if (ImGui.Button("PF Clock Out", ButtonSize()))
                state.PfClockOut();
        }

        if (!me.PfClockedIn && nothingTicked)
        {
            ImGui.SameLine();
            Theme.Muted("Tick what you did to clock in for PF.");
        }

        ImGui.Spacing();
        Theme.Stat("Pay per hour", Theme.Gil(me.HourlyPay));
        Theme.Stat("This shift", me.ClockedIn ? Theme.Clock(TimeSpan.FromHours(me.ShiftHours(now))) : "-",
            me.ClockedIn ? Theme.Good : Theme.TextMuted);
        if (me.PfClockedIn)
            Theme.Stat("PF time", Theme.Clock(TimeSpan.FromHours(me.PfShiftHours(now))), Theme.Good);
        Theme.Stat("Earned this shift", Theme.Gil(me.ShiftEarned(now)));
        Theme.Stat("Hours worked", Theme.Hours(me.HoursWorked + me.ShiftHours(now)));
        Theme.Stat("PF hours", Theme.Hours(me.PfHoursWorked + me.PfShiftHours(now)));
        Theme.Stat("Owed to you", Theme.Gil(me.Owed + me.ShiftEarned(now)));
        Theme.EndStats();

        if (me.Collab.Active)
            ImGui.TextColored(Theme.Gold, $"Collab pay active ({me.Collab.Job}).");
    }

    private void DrawMyBookings(DateTimeOffset now)
    {
        var bookings = state.MyBookings;
        if (bookings.Count == 0)
            return;

        Theme.Section("Your bookings");
        BookingTable.Draw("mine", bookings, now, BookingTable.Columns.Client | BookingTable.Columns.BookedBy | BookingTable.Columns.Amount);
    }

    private void DrawStaff(Profile profile, DateTimeOffset now)
    {
        Theme.Section(profile.IsOwner() ? "All staff" : $"{profile.Department} staff");
        var staff = state.Staff;
        if (staff.Count == 0)
        {
            Theme.Muted("Loading staff...");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("staff", 7, flags))
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2.2f);
        ImGui.TableSetupColumn("Role", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableSetupColumn("Shift", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Pay / hour", ImGuiTableColumnFlags.None, 1.5f);
        ImGui.TableSetupColumn("Hours", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("Owed", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Collab", ImGuiTableColumnFlags.None, 3.2f);
        ImGui.TableHeadersRow();

        foreach (var s in staff.OrderBy(s => s.Department).ThenBy(s => s.Name))
        {
            ImGui.PushID(s.EmployeeId);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(s.Name);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Theme.Muted(profile.IsOwner() ? $"{s.Department} - {s.Job}" : s.Job);

            ImGui.TableNextColumn();
            this.DrawShiftCell(s, now);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Theme.Gil(s.HourlyPay));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Theme.Hours(s.HoursWorked + s.ShiftHours(now)));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(Theme.Gil(s.Owed + s.ShiftEarned(now)));

            ImGui.TableNextColumn();
            this.DrawCollabCell(profile, s);

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawShiftCell(EmployeeStatus s, DateTimeOffset now)
    {
        ImGui.AlignTextToFramePadding();
        if (!s.ClockedIn)
        {
            Theme.Muted(s.PfClockedIn ? "PF only" : "Off");
            return;
        }

        ImGui.TextColored(Theme.Good, Theme.Clock(TimeSpan.FromHours(s.ShiftHours(now))));
        if (!s.ClockInPendingApproval)
            return;

        ImGui.SameLine();
        using (Disabled(state.Busy))
        {
            if (ImGui.SmallButton("Approve"))
                state.ApproveClockIn(s);
        }

        if (ImGui.IsItemHovered())
        {
            var at = s.ClockInAt is { } t ? VenueClock.EasternNow(t).ToString("ddd h:mm tt") + " Eastern" : "early";
            ImGui.SetTooltip($"Clocked in {at}, before opening. Check the time, then approve it so pay can go out.");
        }
    }

    private void DrawCollabCell(Profile profile, EmployeeStatus s)
    {
        var active = s.Collab.Active;
        using (Disabled(state.Busy))
        {
            if (ImGui.Checkbox("##collab", ref active))
                state.SetCollab(s, active, Names.JobDancer);
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Collab pay: 1.5x base pay for the job worked at the collab.");

        if (!s.Collab.Active)
            return;

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * Theme.Scale);
        using (Disabled(state.Busy))
        {
            if (ImGui.BeginCombo("##job", s.Collab.PendingJob ?? s.Collab.Job))
            {
                foreach (var job in state.Meta.Jobs)
                {
                    var rate = Theme.Gil((long)(job.BasePay * state.Meta.CollabMultiplier));
                    if (ImGui.Selectable($"{job.Name}  ({rate})", job.Name == s.Collab.Job) && job.Name != s.Collab.Job)
                        state.SetCollab(s, true, job.Name);
                }

                ImGui.EndCombo();
            }
        }

        if (s.Collab.PendingJob == null)
        {
            if (!profile.IsOwner() && ImGui.IsItemHovered())
                ImGui.SetTooltip("Anything other than Dancer needs an owner or co-owner to approve it.");
            return;
        }

        ImGui.SameLine();
        if (!profile.IsOwner())
        {
            ImGui.TextColored(Theme.Warn, "needs owner");
            return;
        }

        using (Disabled(state.Busy))
        {
            if (ImGui.SmallButton("Approve##job"))
                state.DecideCollabJob(s, true);
            ImGui.SameLine();
            if (ImGui.SmallButton("Decline##job"))
                state.DecideCollabJob(s, false);
        }
    }

    private static System.Numerics.Vector2 ButtonSize() => new(120 * Theme.Scale, 0);

    private static Dalamud.Interface.Utility.Raii.ImRaii.DisabledDisposable Disabled(bool disabled) =>
        Dalamud.Interface.Utility.Raii.ImRaii.Disabled(disabled);
}
