using System;
using System.Collections.Generic;

namespace ApolloNCC.Api;

public enum Rank
{
    Employee,
    AssistantManager,
    Manager,
    CoOwner,
    Owner,
}

/// <summary>Names the web app and plugin agree on.</summary>
public static class Names
{
    public const string DeptFrontOfHouse = "Front of House";
    public const string DeptSecurity = "Security";
    public const string JobDancer = "Dancer";
    public const string JobReceptionist = "Receptionist";
    public const string JobBouncer = "Bouncer";
}

/// <summary>The employee the plugin key belongs to. Always the main character, whatever alt is logged in.</summary>
public sealed class Profile
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public Rank Rank { get; set; }
    public string Department { get; set; } = "";
    public List<string> Jobs { get; set; } = [];
    public bool DiscordLinked { get; set; }
    public string? DiscordName { get; set; }
}

public sealed class JobInfo
{
    public string Name { get; set; } = "";
    public long BasePay { get; set; }
}

public sealed class VenueMeta
{
    public List<JobInfo> Jobs { get; set; } = [];
    public List<string> Rooms { get; set; } = [];
    public double CollabMultiplier { get; set; } = 1.5;
}

public sealed class CollabStatus
{
    public bool Active { get; set; }
    public string Job { get; set; } = Names.JobDancer;

    /// <summary>A non-dancer job waiting for owner / co-owner approval.</summary>
    public string? PendingJob { get; set; }
}

public sealed class EmployeeStatus
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public Rank Rank { get; set; }
    public string Department { get; set; } = "";
    public string Job { get; set; } = "";

    public bool ClockedIn { get; set; }
    public DateTimeOffset? ClockInAt { get; set; }
    public bool ClockInPendingApproval { get; set; }

    public bool PfClockedIn { get; set; }
    public DateTimeOffset? PfClockInAt { get; set; }
    public bool PfPartyFinder { get; set; }
    public bool PfShouts { get; set; }

    /// <summary>Gil per hour currently in effect (already includes collab pay).</summary>
    public long HourlyPay { get; set; }
    public long PfHourlyPay { get; set; }

    /// <summary>Completed shifts this pay period. Open shifts are added live by the plugin.</summary>
    public double HoursWorked { get; set; }
    public double PfHoursWorked { get; set; }
    public long Owed { get; set; }

    public CollabStatus Collab { get; set; } = new();

    public double ShiftHours(DateTimeOffset now) =>
        this.ClockedIn && this.ClockInAt is { } t ? Math.Max(0, (now - t).TotalHours) : 0;

    public double PfShiftHours(DateTimeOffset now) =>
        this.PfClockedIn && this.PfClockInAt is { } t ? Math.Max(0, (now - t).TotalHours) : 0;

    public long ShiftEarned(DateTimeOffset now) =>
        (long)((this.ShiftHours(now) * this.HourlyPay) + (this.PfShiftHours(now) * this.PfHourlyPay));
}

public sealed class Booking
{
    public string Id { get; set; } = "";
    public string StaffId { get; set; } = "";
    public string StaffName { get; set; } = "";

    /// <summary>Character the staff member was last seen on, for tells. May be an alt.</summary>
    public string? StaffCharacter { get; set; }
    public string? StaffWorld { get; set; }

    public string ClientName { get; set; } = "";
    public string? ClientWorld { get; set; }
    public string BookedByName { get; set; } = "";
    public string Room { get; set; } = "";
    public long Amount { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }

    public TimeSpan Remaining(DateTimeOffset now) => this.EndsAt > now ? this.EndsAt - now : TimeSpan.Zero;
}

public sealed class BookableStaff
{
    public string EmployeeId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Job { get; set; } = "";
    public bool ClockedIn { get; set; }
}

public sealed class ReceptionSummary
{
    public List<Booking> Bookings { get; set; } = [];
    public List<BookableStaff> Staff { get; set; } = [];
    public long TotalPaid { get; set; }
    public long ReceptionBonus { get; set; }
    public double BonusRate { get; set; } = 0.20;
}

public sealed class CreateBookingRequest
{
    public string StaffId { get; set; } = "";
    public string ClientName { get; set; } = "";
    public string? ClientWorld { get; set; }
    public string Room { get; set; } = "";
    public int Minutes { get; set; }
    public long Amount { get; set; }
}

public sealed class BlacklistEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? World { get; set; }
    public string Reason { get; set; } = "";
    public string? AddedBy { get; set; }
    public DateTimeOffset? AddedAt { get; set; }
}

public sealed class PatronWarning
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? World { get; set; }
    public string Reason { get; set; } = "";
    public string IssuedBy { get; set; } = "";
    public DateTimeOffset IssuedAt { get; set; }
}

/// <summary>A banned patron spotted in the venue by some staff member's plugin.</summary>
public sealed class Sighting
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? World { get; set; }
    public string Reason { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public DateTimeOffset SeenAt { get; set; }
}

public sealed class DiscordLinkCode
{
    public string Code { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
