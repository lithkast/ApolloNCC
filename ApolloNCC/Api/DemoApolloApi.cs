using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ApolloNCC.Services;

namespace ApolloNCC.Api;

/// <summary>
/// Offline stand-in for the web app with made-up data, so the plugin can be looked at without a login.
/// Nothing here leaves the machine or is saved.
/// </summary>
public sealed class DemoApolloApi : IApolloApi
{
    private const string MeId = "demo-me";

    private readonly object gate = new();
    private readonly Func<Rank> rank;
    private readonly VenueMeta meta = new()
    {
        Jobs =
        [
            new() { Name = "Dancer", BasePay = 125_000 },
            new() { Name = "Receptionist", BasePay = 100_000 },
            new() { Name = "Bouncer", BasePay = 100_000 },
            new() { Name = "Bartender", BasePay = 100_000 },
            new() { Name = "Dealer", BasePay = 110_000 },
        ],
        Rooms = ["VIP 1", "VIP 2", "VIP 3", "Private Lounge"],
    };

    private readonly List<EmployeeStatus> staff;
    private readonly List<Booking> bookings = [];
    private readonly List<PatronWarning> warnings = [];
    private readonly List<Sighting> sightings = [];
    private readonly List<BlacklistEntry> blacklist =
    [
        new() { Id = "b1", Name = "Rowdy Patron", World = "Gilgamesh", Reason = "Harassing staff", AddedBy = "Demo Owner", AddedAt = DateTimeOffset.UtcNow.AddDays(-40) },
        new() { Id = "b2", Name = "Card Counter", World = "Balmung", Reason = "Casino scamming", AddedBy = "Demo Owner", AddedAt = DateTimeOffset.UtcNow.AddDays(-12) },
    ];

    private int nextId = 1;

    public DemoApolloApi(Func<Rank> rank)
    {
        this.rank = rank;
        var now = DateTimeOffset.UtcNow;
        this.staff =
        [
            Make(MeId, "Demo Employee", "Dancer", "Dancer"),
            Make("s1", "Luna Starfall", "Dancer", "Dancer", now.AddMinutes(-95)),
            Make("s2", "Kira Moonshade", "Dancer", "Dancer", now.AddMinutes(-20), pending: true),
            Make("s3", "Tessa Brightwind", Names.DeptFrontOfHouse, "Receptionist", now.AddMinutes(-60)),
            Make("s4", "Garrick Stonefist", Names.DeptSecurity, "Bouncer", now.AddMinutes(-70)),
            Make("s5", "Milo Quickpour", "Bar", "Bartender"),
            Make("s6", "Vex Highroller", "Casino", "Dealer", now.AddMinutes(-45)),
        ];
        this.bookings.Add(new Booking
        {
            Id = "bk0", StaffId = MeId, StaffName = "Demo Employee", StaffCharacter = "Demo Employee", StaffWorld = "Gilgamesh",
            ClientName = "Generous Guest", ClientWorld = "Gilgamesh", BookedByName = "Tessa Brightwind", Room = "VIP 1",
            Amount = 500_000, StartsAt = now.AddMinutes(-28), EndsAt = now.AddMinutes(2),
        });
    }

    public Task<Profile> GetProfileAsync(CancellationToken ct) => this.Do(() =>
    {
        var r = this.rank();
        this.Me.Rank = r;
        return new Profile
        {
            EmployeeId = MeId,
            Name = this.Me.Name,
            Rank = r,
            Department = this.Me.Department,
            Jobs = [this.Me.Job],
        };
    });

    public Task<VenueMeta> GetMetaAsync(CancellationToken ct) => this.Do(() => this.meta);

    public Task<EmployeeStatus> GetMyStatusAsync(CancellationToken ct) => this.Do(() => Copy(this.Me));

    public Task<EmployeeStatus> ClockInAsync(CancellationToken ct) => this.Do(() =>
    {
        var me = this.Me;
        if (me.ClockedIn)
            throw new ApolloApiException("You are already clocked in.");
        me.ClockedIn = true;
        me.ClockInAt = DateTimeOffset.UtcNow;
        me.ClockInPendingApproval = VenueClock.ClockInNeedsApproval(DateTimeOffset.UtcNow);
        return Copy(me);
    });

    public Task<EmployeeStatus> ClockOutAsync(CancellationToken ct) => this.Do(() =>
    {
        var me = this.Me;
        if (!me.ClockedIn)
            throw new ApolloApiException("You are not clocked in.");
        var now = DateTimeOffset.UtcNow;
        me.HoursWorked += me.ShiftHours(now);
        me.Owed += (long)(me.ShiftHours(now) * me.HourlyPay);
        me.ClockedIn = false;
        me.ClockInAt = null;
        return Copy(me);
    });

    public Task<EmployeeStatus> PfClockInAsync(bool partyFinder, bool shouts, CancellationToken ct) => this.Do(() =>
    {
        var me = this.Me;
        if (!partyFinder && !shouts)
            throw new ApolloApiException("Tick Party Finder, Shouts, or both.");
        if (me.PfClockedIn)
            throw new ApolloApiException("You are already clocked in for PF.");
        me.PfClockedIn = true;
        me.PfClockInAt = DateTimeOffset.UtcNow;
        me.PfPartyFinder = partyFinder;
        me.PfShouts = shouts;
        return Copy(me);
    });

    public Task<EmployeeStatus> PfClockOutAsync(CancellationToken ct) => this.Do(() =>
    {
        var me = this.Me;
        if (!me.PfClockedIn)
            throw new ApolloApiException("You are not clocked in for PF.");
        var now = DateTimeOffset.UtcNow;
        me.PfHoursWorked += me.PfShiftHours(now);
        me.Owed += (long)(me.PfShiftHours(now) * me.PfHourlyPay);
        me.PfClockedIn = false;
        me.PfClockInAt = null;
        return Copy(me);
    });

    public Task<List<EmployeeStatus>> GetStaffAsync(CancellationToken ct) => this.Do(() =>
    {
        var r = this.rank();
        if (r < Rank.AssistantManager)
            throw new ApolloApiException("Your role does not allow that.");
        return this.staff
            .Where(s => r >= Rank.CoOwner || s.Department == this.Me.Department)
            .Select(Copy)
            .ToList();
    });

    public Task<EmployeeStatus> ApproveClockInAsync(string employeeId, CancellationToken ct) => this.Do(() =>
    {
        var s = this.Find(employeeId);
        s.ClockInPendingApproval = false;
        return Copy(s);
    });

    public Task<EmployeeStatus> SetCollabAsync(string employeeId, bool active, string job, CancellationToken ct) => this.Do(() =>
    {
        var s = this.Find(employeeId);
        s.Collab.Active = active;
        s.Collab.PendingJob = null;
        if (!active || job == Names.JobDancer || this.rank() >= Rank.CoOwner)
            s.Collab.Job = active ? job : Names.JobDancer;
        else
            s.Collab.PendingJob = job;
        this.ApplyPay(s);
        return Copy(s);
    });

    public Task<EmployeeStatus> DecideCollabJobAsync(string employeeId, bool approve, CancellationToken ct) => this.Do(() =>
    {
        if (this.rank() < Rank.CoOwner)
            throw new ApolloApiException("Only an owner or co-owner can approve a collab job change.");
        var s = this.Find(employeeId);
        if (approve && s.Collab.PendingJob != null)
            s.Collab.Job = s.Collab.PendingJob;
        s.Collab.PendingJob = null;
        this.ApplyPay(s);
        return Copy(s);
    });

    public Task<List<Booking>> GetMyBookingsAsync(CancellationToken ct) =>
        this.Do(() => this.bookings.Where(b => b.StaffId == MeId).ToList());

    public Task<ReceptionSummary> GetReceptionAsync(CancellationToken ct) => this.Do(() =>
    {
        var total = this.bookings.Sum(b => b.Amount);
        return new ReceptionSummary
        {
            Bookings = this.bookings.ToList(),
            Staff = this.staff.Select(s => new BookableStaff { EmployeeId = s.EmployeeId, Name = s.Name, Job = s.Job, ClockedIn = s.ClockedIn }).ToList(),
            TotalPaid = total,
            ReceptionBonus = (long)(total * 0.20),
        };
    });

    public Task<Booking> CreateBookingAsync(CreateBookingRequest request, CancellationToken ct) => this.Do(() =>
    {
        var s = this.Find(request.StaffId);
        var now = DateTimeOffset.UtcNow;
        if (this.bookings.Any(b => b.Room == request.Room && b.EndsAt > now))
            throw new ApolloApiException($"{request.Room} is already in use.");
        var booking = new Booking
        {
            Id = $"bk{this.nextId++}",
            StaffId = s.EmployeeId,
            StaffName = s.Name,
            StaffCharacter = s.Name,
            StaffWorld = "Gilgamesh",
            ClientName = request.ClientName,
            ClientWorld = request.ClientWorld,
            BookedByName = this.Me.Name,
            Room = request.Room,
            Amount = request.Amount,
            StartsAt = now,
            EndsAt = now.AddMinutes(request.Minutes),
        };
        this.bookings.Add(booking);
        return booking;
    });

    public Task EndBookingAsync(string bookingId, CancellationToken ct) =>
        this.Do(() => this.bookings.RemoveAll(b => b.Id == bookingId));

    public Task<List<BlacklistEntry>> GetBlacklistAsync(CancellationToken ct) => this.Do(() => this.blacklist.ToList());

    public Task<List<Booking>> GetSecurityBookingsAsync(CancellationToken ct) => this.Do(() => this.bookings.ToList());

    public Task<List<PatronWarning>> GetWarningsAsync(CancellationToken ct) => this.Do(() => this.warnings.ToList());

    public Task<PatronWarning> AddWarningAsync(string name, string? world, string reason, CancellationToken ct) => this.Do(() =>
    {
        var w = new PatronWarning
        {
            Id = $"w{this.nextId++}", Name = name, World = world, Reason = reason,
            IssuedBy = this.Me.Name, IssuedAt = DateTimeOffset.UtcNow,
        };
        this.warnings.Insert(0, w);
        return w;
    });

    public Task<List<Sighting>> GetSightingsAsync(CancellationToken ct) => this.Do(() => this.sightings.ToList());

    public Task ReportSightingAsync(string blacklistId, string name, string? world, CancellationToken ct) => this.Do(() =>
    {
        this.sightings.Insert(0, new Sighting
        {
            Id = $"si{this.nextId++}", Name = name, World = world,
            Reason = this.blacklist.Find(b => b.Id == blacklistId)?.Reason ?? "",
            ReportedBy = this.Me.Name, SeenAt = DateTimeOffset.UtcNow,
        });
        return 0;
    });

    public Task<DiscordLinkCode> StartDiscordLinkAsync(CancellationToken ct) =>
        this.Do(() => new DiscordLinkCode { Code = "DEMO-0000", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10) });

    private EmployeeStatus Me => this.staff[0];

    private static EmployeeStatus Copy(EmployeeStatus s) =>
        JsonSerializer.Deserialize<EmployeeStatus>(JsonSerializer.Serialize(s, HttpApolloApi.Json), HttpApolloApi.Json)!;

    private EmployeeStatus Make(string id, string name, string dept, string job, DateTimeOffset? clockIn = null, bool pending = false)
    {
        var s = new EmployeeStatus
        {
            EmployeeId = id, Name = name, Department = dept, Job = job,
            ClockedIn = clockIn != null, ClockInAt = clockIn, ClockInPendingApproval = pending,
            PfHourlyPay = 50_000, HoursWorked = 6.5, Owed = 812_500,
        };
        this.ApplyPay(s);
        return s;
    }

    private void ApplyPay(EmployeeStatus s)
    {
        long Base(string job) => this.meta.Jobs.Find(j => j.Name == job)?.BasePay ?? 100_000;
        s.HourlyPay = s.Collab.Active ? (long)(Base(s.Collab.Job) * this.meta.CollabMultiplier) : Base(s.Job);
    }

    private EmployeeStatus Find(string id) =>
        this.staff.Find(s => s.EmployeeId == id) ?? throw new ApolloApiException("Unknown employee.");

    private Task<T> Do<T>(Func<T> action)
    {
        lock (this.gate)
        {
            try
            {
                return Task.FromResult(action());
            }
            catch (ApolloApiException e)
            {
                return Task.FromException<T>(e);
            }
        }
    }
}
