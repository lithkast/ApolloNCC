using System;
using System.Linq;
using System.Numerics;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class HrTab(ApolloState state)
{
    private bool pfPartyFinder;
    private bool pfShouts;

    public void Draw(Access access)
    {
        var now = DateTimeOffset.UtcNow;
        this.DrawMyShift(access, now);
        this.DrawMyBookings(now);
        if (access.IsOwner)
            this.DrawReviews();
        if (access.SeesStaff)
            this.DrawStaff(access, now);
    }

    private void DrawMyShift(Access access, DateTimeOffset now)
    {
        var cfg = state.Config;
        var row = state.MyRow;
        Theme.Section(state.MyName.Length > 0 ? state.MyName : "Your shift");
        this.DrawCollab(access);

        if (row == null)
        {
            ImGui.TextWrapped("Your login isn't on the HR roster yet, so there is nothing to clock in on. An owner ties logins to the roster in Account management on the site.");
            return;
        }

        var summary = state.MySummary(now)!;
        var onClock = Venue.OnClock(row, now);

        ImGui.BeginDisabled(summary.OnShiftTonight || state.Busy);
        if (ImGui.Button("Clock In", ButtonSize()))
            state.Clock("clockin");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(!onClock || state.Busy);
        if (ImGui.Button("Clock Out", ButtonSize()))
            state.Clock("clockout");
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (row.Status == "LOA")
            ImGui.TextColored(Theme.Warn, "You're on leave (LOA) on the roster.");
        else if (summary.ClockedOut)
            Theme.Muted("You've clocked out for tonight. If you're back on, ask an owner.");
        else if (!summary.OnShiftTonight && this.IsEarly(now))
            Theme.Muted($"Doors open at {Venue.FormatSeconds(ParseTime(cfg.StartTime))}. Clocking in this early is listed for the owners to review.");
        else
            ImGui.NewLine();

        // Party finders and shouts are clocked on their own, and paid on top of the shift.
        if (summary.Promoting)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Good, $"Promoting: {Venue.PromoLabel(summary.PromoKind).ToLowerInvariant()}, since {Venue.FormatSeconds(summary.PromoIn ?? 0)}");
            ImGui.SameLine();
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Button("PF Clock Out", ButtonSize()))
                state.Clock("pfclockout");
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.Checkbox($"Party Finder ({Venue.Gil(cfg.PromoPf)}/hr)", ref this.pfPartyFinder);
            ImGui.SameLine();
            ImGui.Checkbox($"Shouts ({Venue.Gil(cfg.PromoShout)}/hr)", ref this.pfShouts);
            ImGui.SameLine();
            var nothingTicked = !this.pfPartyFinder && !this.pfShouts;
            ImGui.BeginDisabled(nothingTicked || state.Busy);
            if (ImGui.Button("PF Clock In", ButtonSize()))
                state.Clock("pfclockin", this.pfPartyFinder && this.pfShouts ? "both" : this.pfPartyFinder ? "pf" : "shout");
            ImGui.EndDisabled();
            if (nothingTicked)
            {
                ImGui.SameLine();
                Theme.Muted("Tick what you're doing first.");
            }
        }

        ImGui.Spacing();
        Theme.Stat(summary.FlatPay ? "Pay per night" : "Pay per hour", Venue.Gil(summary.BaseRate));
        Theme.Stat("Clocked in", summary.TimeIn is { } tin ? Venue.FormatSeconds(tin) : "-", summary.OnShiftTonight ? Theme.Good : Theme.TextMuted);
        if (summary.TimeOut is { } tout)
            Theme.Stat("Clocked out", Venue.FormatSeconds(tout));
        Theme.Stat("Hours", summary.Hours is { } h ? Venue.HoursText(h) : "-");
        Theme.Stat("Shift pay", Venue.Gil(summary.ShiftPay));
        Theme.EndStats();

        var extras = false;
        void Extra(string label, double amount, string? detail = null)
        {
            if (amount <= 0)
                return;
            Theme.Stat(label, Venue.Gil(amount) + (detail != null ? $"  ({detail})" : ""));
            extras = true;
        }

        Extra("Party finders", Math.Round(summary.PfHours * cfg.PromoPf), Venue.HoursText(summary.PfHours));
        Extra("Shouts", Math.Round(summary.ShoutHours * cfg.PromoShout), Venue.HoursText(summary.ShoutHours));
        Extra("Booking fees", summary.Bookings);
        Extra("Reception bonus", summary.ReceptionBonus);
        Extra("Bar buyout share", summary.BarShare);
        if (extras)
            Theme.EndStats();

        var done = (!summary.OnShiftTonight || summary.ClockedOut) && !summary.Promoting;
        Theme.Stat(done ? "Total tonight" : "Total so far tonight", Venue.Gil(summary.Total), Theme.Gold);
        if (row.Paid)
            Theme.Stat("Status", "Paid", Theme.Good);
        Theme.EndStats();
        Theme.Muted("Times are rounded to the quarter hour, as on the HR page.");
    }

    /// <summary>Collab nights are for the whole venue: owners and co-owners switch them, everyone sees them.</summary>
    private void DrawCollab(Access access)
    {
        var rate = state.Config.CollabRate.ToString("0.##");
        if (state.CollabNight)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(Theme.Gold, $"Collab night: hourly pay x{rate}.");
        }

        if (!access.CanChangeNight)
        {
            return;
        }

        if (state.CollabNight)
            ImGui.SameLine();
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.Button(state.CollabNight ? "End collab night" : "Start collab night"))
            state.SetCollabNight(!state.CollabNight);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"While a collab night is on, everyone's hourly pay is x{rate} and Reception takes no bookings.");
        ImGui.Spacing();
    }

    private void DrawMyBookings(DateTimeOffset now)
    {
        var bookings = state.MyBookings.ToList();
        if (bookings.Count == 0)
            return;

        Theme.Section("Your bookings");
        BookingTable.Draw("mine", bookings, now, BookingTable.Columns.Patron | BookingTable.Columns.Type | BookingTable.Columns.Total);
    }

    /// <summary>Clock-ins and clock-outs flagged for the owners to look over, as on the site's HR page.</summary>
    private void DrawReviews()
    {
        var reviews = state.Reviews;
        if (reviews.Count == 0)
            return;

        Theme.Section($"To review ({reviews.Count})");
        foreach (var v in reviews)
        {
            ImGui.PushID(v.Id);
            ImGui.BeginDisabled(state.Busy || state.Access?.CanChangeNight != true);
            if (ImGui.SmallButton("Reviewed"))
                state.MarkReviewed(v);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextColored(Theme.Gold, v.Name.Length > 0 ? v.Name : "Someone");
            ImGui.SameLine();
            ImGui.TextWrapped(ReviewText(v));
            ImGui.PopID();
        }
    }

    private void DrawStaff(Access access, DateTimeOffset now)
    {
        Theme.Section(access.IsOwner ? "All staff" : "Your department");
        var staff = state.Roster.Where(r => access.Sees(r) && r.Id != state.MyRow?.Id).ToList();
        if (staff.Count == 0)
        {
            Theme.Muted(state.Roster.Count == 0 ? "Loading staff..." : "Nobody else to show.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("staff", 7, flags))
            return;

        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.None, 2.2f);
        ImGui.TableSetupColumn("Job", ImGuiTableColumnFlags.None, 1.5f);
        ImGui.TableSetupColumn("In", ImGuiTableColumnFlags.None, 1.1f);
        ImGui.TableSetupColumn("Out", ImGuiTableColumnFlags.None, 1.1f);
        ImGui.TableSetupColumn("Hours", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("Shift pay", ImGuiTableColumnFlags.None, 1.4f);
        ImGui.TableSetupColumn("Promotion", ImGuiTableColumnFlags.None, 2f);
        ImGui.TableHeadersRow();

        foreach (var r in staff)
        {
            var s = Venue.Summarise(state.Config, r, state.CollabNight, now);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(s.OnShiftTonight && !s.ClockedOut ? Theme.Good : Theme.Text, r.Name);
            if (r.Status == "LOA")
            {
                ImGui.SameLine();
                Theme.Muted("LOA");
            }

            ImGui.TableNextColumn();
            Theme.Muted(r.Job);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.TimeIn is { } tin ? Venue.FormatSeconds(tin) : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.TimeOut is { } tout ? Venue.FormatSeconds(tout) : s.OnShiftTonight ? "on shift" : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.Hours is { } h ? h.ToString("0.##") : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(s.OnShiftTonight ? Venue.Gil(s.ShiftPay) : "-");
            if (r.Paid)
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.Good, "paid");
            }

            ImGui.TableNextColumn();
            if (s.Promoting)
                ImGui.TextColored(Theme.Good, $"{Venue.PromoLabel(s.PromoKind)} since {Venue.FormatSeconds(s.PromoIn ?? 0)}");
            else if (s.PromoPay > 0)
                ImGui.TextUnformatted(Venue.Gil(s.PromoPay));
            else
                ImGui.TextUnformatted("-");
        }

        ImGui.EndTable();
    }

    private static string ReviewText(ClockReview v)
    {
        var t = v.Stamp is { } stamp ? Venue.FormatSeconds(stamp) : "";
        var m = (int)Math.Round(v.Minutes);
        var dur = m >= 60 ? $"{m / 60} hr{(m % 60 > 0 ? $" {m % 60} min" : "")}" : $"{m} min";
        var by = v.By.Length > 0 ? v.By : "an owner";
        var promo = Venue.PromoLabel(v.Promo) is { Length: > 0 } p ? p.ToLowerInvariant() : "promotion";
        return v.Kind switch
        {
            "early" => $"clocked in at {t}, {dur} before doors open.",
            "afterclose" => $"clocked in at {t}, {dur} after closing.",
            "lateout" => $"clocked out at {t}, {dur} after closing.",
            "fix-in" => $"was clocked in at {t} by {by}.",
            "fix-out" => $"was clocked out at {t} by {by}.",
            "pf-fix-in" => $"was clocked in for {promo} at {t} by {by}.",
            "pf-fix-out" => $"was clocked out of {promo} at {t} by {by}.",
            _ => "was noted by Hermes.",
        };
    }

    /// <summary>Whether a clock-in right now would be flagged as early (Settings, clockRules).</summary>
    private bool IsEarly(DateTimeOffset now)
    {
        var sec = Venue.VenueSeconds(state.Config, now);
        var open = ParseTime(state.Config.StartTime);
        return sec >= 12 * 3600 && sec < open - (state.Config.ClockEarlyMinutes * 60);
    }

    private static double ParseTime(string hhmm) =>
        TimeSpan.TryParse(hhmm, out var t) ? t.TotalSeconds : 21 * 3600;

    private static Vector2 ButtonSize() => new(120 * Theme.Scale, 0);
}
