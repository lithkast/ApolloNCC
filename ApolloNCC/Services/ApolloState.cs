using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ApolloNCC.Api;

namespace ApolloNCC.Services;

/// <summary>
/// The plugin's copy of what the web app knows, plus the actions that change it.
/// Network work runs in the background; the UI only ever reads the latest snapshot.
/// Lists are replaced whole, never edited in place, so reading them from the draw thread is safe.
/// </summary>
public sealed class ApolloState : IDisposable
{
    private static readonly TimeSpan CoreInterval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ViewInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BlacklistInterval = TimeSpan.FromMinutes(3);

    private readonly Configuration config;
    private readonly HttpApolloApi http;
    private readonly DemoApolloApi demo;
    private readonly CancellationTokenSource cts = new();

    private DateTimeOffset nextCore = DateTimeOffset.MinValue;
    private DateTimeOffset nextView = DateTimeOffset.MinValue;
    private DateTimeOffset nextBlacklist = DateTimeOffset.MinValue;
    private int busy;
    private int coreRunning;
    private int viewRunning;
    private int session;

    public ApolloState(Configuration config, Func<string?> character)
    {
        this.config = config;
        this.http = new HttpApolloApi(() => config.BaseUrl, () => config.SecretKey, character);
        this.demo = new DemoApolloApi(() => config.DemoRank);
    }

    public Profile? Profile { get; private set; }
    public VenueMeta Meta { get; private set; } = new();
    public EmployeeStatus? MyStatus { get; private set; }
    public IReadOnlyList<Booking> MyBookings { get; private set; } = [];
    public IReadOnlyList<EmployeeStatus> Staff { get; private set; } = [];
    public ReceptionSummary? Reception { get; private set; }
    public IReadOnlyList<Booking> SecurityBookings { get; private set; } = [];
    public IReadOnlyList<BlacklistEntry> Blacklist { get; private set; } = [];
    public IReadOnlyList<PatronWarning> Warnings { get; private set; } = [];
    public IReadOnlyList<Sighting> Sightings { get; private set; } = [];
    public DiscordLinkCode? DiscordCode { get; private set; }

    /// <summary>Last thing that went wrong, worded for the user. Cleared by the next success.</summary>
    public string? Error { get; private set; }

    /// <summary>Last action that worked, worded for the user.</summary>
    public string? Notice { get; private set; }

    public bool Busy => this.busy > 0;

    public bool IsConfigured => this.config.DemoMode || !string.IsNullOrWhiteSpace(this.config.SecretKey);

    public bool IsLinked => this.Profile != null;

    private IApolloApi Api => this.config.DemoMode ? this.demo : this.http;

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
        this.http.Dispose();
    }

    /// <summary>Forget everything and sign in again. Call after the key, address or demo mode changes.</summary>
    public void Reconnect()
    {
        this.session++;
        this.Profile = null;
        this.MyStatus = null;
        this.MyBookings = [];
        this.Staff = [];
        this.Reception = null;
        this.SecurityBookings = [];
        this.Blacklist = [];
        this.Warnings = [];
        this.Sightings = [];
        this.DiscordCode = null;
        this.Error = null;
        this.Notice = null;
        this.nextCore = this.nextView = this.nextBlacklist = DateTimeOffset.MinValue;
    }

    /// <summary>Called every frame. Starts background refreshes when they are due.</summary>
    /// <param name="view">Which tab's data to keep fresh, or null when the window is closed.</param>
    public void Tick(DateTimeOffset now, View? view)
    {
        if (!this.IsConfigured)
            return;

        if (now >= this.nextCore && Interlocked.Exchange(ref this.coreRunning, 1) == 0)
        {
            // Without a working sign-in, retry slowly instead of hammering the web app.
            this.nextCore = now + CoreInterval;
            var blacklistDue = now >= this.nextBlacklist;
            if (blacklistDue)
                this.nextBlacklist = now + BlacklistInterval;
            this.Background(() => this.RefreshCore(blacklistDue), () => this.coreRunning = 0);
        }

        if (view is { } v && this.Profile != null && now >= this.nextView &&
            Interlocked.Exchange(ref this.viewRunning, 1) == 0)
        {
            this.nextView = now + ViewInterval;
            this.Background(() => this.RefreshView(v), () => this.viewRunning = 0);
        }
    }

    /// <summary>Refresh the open tab on the next frame.</summary>
    public void RefreshSoon() => this.nextCore = this.nextView = DateTimeOffset.MinValue;

    public void ClockIn() => this.Act(async ct => this.MyStatus = await this.Api.ClockInAsync(ct),
        () => this.MyStatus?.ClockInPendingApproval == true
            ? "Clocked in. This clock-in needs your manager's approval before pay goes out."
            : "Clocked in.");

    public void ClockOut() => this.Act(async ct => this.MyStatus = await this.Api.ClockOutAsync(ct), () => "Clocked out.");

    public void PfClockIn(bool partyFinder, bool shouts) =>
        this.Act(async ct => this.MyStatus = await this.Api.PfClockInAsync(partyFinder, shouts, ct), () => "Clocked in for PF.");

    public void PfClockOut() => this.Act(async ct => this.MyStatus = await this.Api.PfClockOutAsync(ct), () => "Clocked out of PF.");

    public void ApproveClockIn(EmployeeStatus who) =>
        this.Act(async ct => this.ReplaceStaff(await this.Api.ApproveClockInAsync(who.EmployeeId, ct)),
            () => $"Approved {who.Name}'s clock-in.");

    public void SetCollab(EmployeeStatus who, bool active, string job) =>
        this.Act(async ct => this.ReplaceStaff(await this.Api.SetCollabAsync(who.EmployeeId, active, job, ct)), () =>
        {
            var updated = this.Staff.FirstOrDefault(s => s.EmployeeId == who.EmployeeId);
            if (!active)
                return $"Collab pay turned off for {who.Name}.";
            return updated?.Collab.PendingJob is { } pending
                ? $"{who.Name} stays on {updated.Collab.Job} collab pay until an owner approves {pending}."
                : $"Collab pay set for {who.Name}.";
        });

    public void DecideCollabJob(EmployeeStatus who, bool approve) =>
        this.Act(async ct => this.ReplaceStaff(await this.Api.DecideCollabJobAsync(who.EmployeeId, approve, ct)),
            () => approve ? $"Approved {who.Name}'s collab job." : $"Declined {who.Name}'s collab job.");

    public void CreateBooking(CreateBookingRequest request, Action onSuccess) =>
        this.Act(async ct =>
        {
            var booking = await this.Api.CreateBookingAsync(request, ct);
            this.Reception = await this.Api.GetReceptionAsync(ct);
            this.Notice = $"Booked {booking.StaffName} for {booking.ClientName} in {booking.Room}.";
            onSuccess();
        });

    public void EndBooking(Booking booking) =>
        this.Act(async ct =>
        {
            await this.Api.EndBookingAsync(booking.Id, ct);
            this.Reception = await this.Api.GetReceptionAsync(ct);
        }, () => "Booking ended.");

    public void AddWarning(string name, string? world, string reason, Action onSuccess) =>
        this.Act(async ct =>
        {
            var warning = await this.Api.AddWarningAsync(name, world, reason, ct);
            this.Warnings = [warning, .. this.Warnings];
            this.Notice = $"Warning logged for {warning.Name}. Remember to post proof in the Discord security channel.";
            onSuccess();
        });

    public void ReportSighting(BlacklistEntry entry, string name, string? world) =>
        this.Background(() => this.Api.ReportSightingAsync(entry.Id, name, world, this.cts.Token));

    public void StartDiscordLink() =>
        this.Act(async ct => this.DiscordCode = await this.Api.StartDiscordLinkAsync(ct));

    private async Task RefreshCore(bool blacklistDue)
    {
        var ct = this.cts.Token;
        var api = this.Api;
        var session = this.session;

        Profile profile;
        try
        {
            profile = await api.GetProfileAsync(ct);
        }
        catch (ApolloApiException e) when (e.KeyRejected && session == this.session)
        {
            // A revoked key must not leave the last employee's data on screen.
            this.Reconnect();
            this.nextCore = DateTimeOffset.UtcNow + CoreInterval;
            throw;
        }

        var meta = this.Profile == null ? await api.GetMetaAsync(ct) : null;
        var status = await api.GetMyStatusAsync(ct);
        var bookings = await api.GetMyBookingsAsync(ct);
        var blacklist = blacklistDue ? await api.GetBlacklistAsync(ct) : null;
        var sightings = profile.CanUseSecurityTools() ? await api.GetSightingsAsync(ct) : null;

        // The key or mode changed while this was in flight: these answers belong to the old sign-in.
        if (session != this.session)
            return;

        this.Profile = profile;
        this.Meta = meta ?? this.Meta;
        this.MyStatus = status;
        this.MyBookings = bookings;
        this.Blacklist = blacklist ?? this.Blacklist;
        this.Sightings = sightings ?? this.Sightings;
        this.Error = null;
    }

    private async Task RefreshView(View view)
    {
        var ct = this.cts.Token;
        var api = this.Api;
        var session = this.session;
        var profile = this.Profile;
        if (profile == null)
            return;

        switch (view)
        {
            case View.Hr when profile.CanSeeStaff():
                var staff = await api.GetStaffAsync(ct);
                if (session == this.session)
                    this.Staff = staff;
                break;
            case View.Reception when profile.CanUseReception():
                var reception = await api.GetReceptionAsync(ct);
                if (session == this.session)
                    this.Reception = reception;
                break;
            case View.Security when profile.CanUseSecurityTools():
                var bookings = await api.GetSecurityBookingsAsync(ct);
                var warnings = await api.GetWarningsAsync(ct);
                if (session == this.session)
                {
                    this.SecurityBookings = bookings;
                    this.Warnings = warnings;
                }

                break;
        }
    }

    private void ReplaceStaff(EmployeeStatus updated)
    {
        this.Staff = this.Staff.Select(s => s.EmployeeId == updated.EmployeeId ? updated : s).ToList();
        if (updated.EmployeeId == this.Profile?.EmployeeId)
            this.MyStatus = updated;
    }

    /// <summary>Run something the user asked for, showing a spinner and the outcome.</summary>
    private void Act(Func<CancellationToken, Task> work, Func<string>? notice = null)
    {
        Interlocked.Increment(ref this.busy);
        this.Notice = null;
        this.Background(
            async () =>
            {
                await work(this.cts.Token);
                this.Error = null;
                if (notice != null)
                    this.Notice = notice();
            },
            () => Interlocked.Decrement(ref this.busy));
    }

    private void Background(Func<Task> work, Action? done = null)
    {
        Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (OperationCanceledException)
            {
            }
            catch (ApolloApiException e)
            {
                this.Error = e.Message;
            }
            catch (Exception e)
            {
                this.Error = "Something went wrong inside the plugin. See /xllog for details.";
                Plugin.Log.Error(e, "Apollo background task failed");
            }
            finally
            {
                done?.Invoke();
            }
        });
    }
}

public enum View
{
    Hr,
    Reception,
    Security,
    Other,
}
