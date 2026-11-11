using System;
using System.Collections.Generic;
using System.Linq;

namespace ApolloNCC.Api;

// The web app's rows, as the plugin uses them. Field names are the ones public/index.html and functions/clock.js
// read and write.

/// <summary>One person on the HR roster (hr/&lt;id&gt;). Clock times are seconds into the venue's day.</summary>
public sealed class HrRow(Doc d)
{
    public string Id { get; } = d.Id;
    public string Name { get; } = d.Str("name").Trim();
    public string Job { get; } = d.Str("job");
    public string Status { get; } = d.Str("status");

    /// <summary>The login (Firebase uid) this row belongs to, if any.</summary>
    public string Login { get; } = d.Str("login");
    public bool ClockIn { get; } = d.Bool("clockIn");
    public double? TimeIn { get; } = d.Num("timeIn");
    public bool ClockOut { get; } = d.Bool("clockOut");
    public double? TimeOut { get; } = d.Num("timeOut");

    /// <summary>When Hermes or the plugin stamped the clock-in (ms). A tick on the site leaves none.</summary>
    public double? ClockedAt { get; } = d.Num("clockedAt");
    public bool Paid { get; } = d.Bool("paid");
    public string PromoKind { get; } = d.Str("promoKind");
    public double? PromoIn { get; } = d.Num("promoIn");
    public double? PromoAt { get; } = d.Num("promoAt");
    public double PromoPf { get; } = d.Num("promoPf") ?? 0;
    public double PromoShout { get; } = d.Num("promoShout") ?? 0;
    public double Order { get; } = d.Num("order") ?? 0;
}

/// <summary>One booking on the Reception page (reception/&lt;id&gt;), with its patron (patrons/&lt;id&gt;).</summary>
public sealed class Booking(Doc d, string patron, double? total)
{
    public string Id { get; } = d.Id;
    public string Employee { get; } = d.Str("employee").Trim();
    public string Patron { get; } = patron;
    public string RpType { get; } = d.Str("rpType");
    public double? Hours { get; } = d.Num("duration");
    public double? Guests { get; } = d.Num("guests");
    public bool Vip { get; } = d.Bool("vip");
    public string Location { get; } = d.Str("location");

    /// <summary>What the patron pays, by the venue's prices. Null until the row is filled in.</summary>
    public double? Total { get; } = total;

    public DateTimeOffset? StartsAt { get; } =
        d.Num("timeIn") is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;

    public DateTimeOffset? EndsAt => this.StartsAt is { } s && this.Hours is { } h ? s.AddHours(h) : null;

    public bool IsRunning(DateTimeOffset now) => this.EndsAt is { } end && end > now;
}

/// <summary>One line of the Blacklist (bans/&lt;id&gt;). The name is free text, sometimes "First Last@World".</summary>
public sealed class Ban
{
    public Ban(Doc d)
    {
        this.Id = d.Id;
        this.Name = d.Str("name").Trim();
        this.Date = d.Str("date");
        this.By = d.Str("by");
        this.Reason = d.Str("reason");
        this.Proof = d.Str("proof");
        this.Notes = d.Str("notes");

        var at = this.Name.IndexOf('@');
        this.Player = (at >= 0 ? this.Name[..at] : this.Name).Trim();
        this.World = at >= 0 ? this.Name[(at + 1)..].Trim() : "";
    }

    public string Id { get; }
    public string Name { get; }
    public string Player { get; }
    public string World { get; }
    public string Date { get; }
    public string By { get; }
    public string Reason { get; }
    public string Proof { get; }
    public string Notes { get; }
}

/// <summary>A clock-in or clock-out Hermes flagged for the owners (clockReviews/&lt;id&gt;).</summary>
public sealed class ClockReview(Doc d)
{
    public string Id { get; } = d.Id;
    public string Kind { get; } = d.Str("kind");
    public string Name { get; } = d.Str("name");
    public string Job { get; } = d.Str("job");
    public double At { get; } = d.Num("at") ?? 0;
    public double? Stamp { get; } = d.Num("stamp");
    public double Minutes { get; } = d.Num("minutes") ?? 0;
    public string By { get; } = d.Str("by");
    public string Promo { get; } = d.Str("promo");
}

/// <summary>A warning security gave a patron (warnings/&lt;id&gt;). The plugin's own collection.</summary>
public sealed class PatronWarning(Doc d)
{
    public string Id { get; } = d.Id;
    public string Name { get; } = d.Str("name");
    public string World { get; } = d.Str("world");
    public string Reason { get; } = d.Str("reason");
    public string By { get; } = d.Str("by");
    public DateTimeOffset At { get; } = DateTimeOffset.FromUnixTimeMilliseconds((long)(d.Num("at") ?? 0));
}

/// <summary>A blacklisted player some plugin saw in the venue (sightings/&lt;id&gt;).</summary>
public sealed class Sighting(Doc d)
{
    public string Id { get; } = d.Id;
    public string Name { get; } = d.Str("name");
    public string World { get; } = d.Str("world");
    public string Ban { get; } = d.Str("ban");
    public string By { get; } = d.Str("by");
    public string ByUid { get; } = d.Str("byUid");
    public DateTimeOffset At { get; } = DateTimeOffset.FromUnixTimeMilliseconds((long)(d.Num("at") ?? 0));
}

/// <summary>The character a login is playing right now (presence/&lt;uid&gt;). It may be an alt.</summary>
public sealed class Presence(Doc d)
{
    public string Uid { get; } = d.Id;
    public string Name { get; } = d.Str("name");
    public string World { get; } = d.Str("world");
    public DateTimeOffset At { get; } = DateTimeOffset.FromUnixTimeMilliseconds((long)(d.Num("at") ?? 0));
}

public sealed record Job(string Name, string Pay, double? Flat);

/// <summary>The venue's settings (config/main over the site's defaults).</summary>
public sealed class VenueConfig
{
    public VenueConfig(Doc? d)
    {
        d ??= new Doc("main", []);
        double N(string k, double fallback) => d.Num(k) ?? fallback;

        this.TimeZone = d.Str("timeZone") is { Length: > 0 } z ? z : "America/New_York";
        this.Maintenance = d.Bool("maintenance");
        this.AdminRoster = d.Str("adminRoster");
        this.Rooms = d.List("rooms").OfType<string>().ToList();
        this.ReceptionBonus = N("receptionBonus", 0.2);
        this.VipDiscount = N("vipDiscount", 0.75);
        this.RpPrice = N("rpPrice", 100_000);
        this.ErpPrice = N("erpPrice", 150_000);
        this.RoomRental = N("roomRental", 50_000);
        this.GuestRatePublic = N("guestRatePublic", 50_000);
        this.GuestRatePrivate = N("guestRatePrivate", 75_000);
        this.CollabRate = d.Num("collabRate") is { } r and >= 1 ? r : 1.5;
        this.PayGrades = d.Fields.ContainsKey("payGrades")
            ? d.List("payGrades").Select(v => v as double?).ToList()
            : [125_000, 150_000, 175_000, 200_000, 225_000, 250_000, 1_000_000];
        this.Jobs = d.Fields.ContainsKey("jobs")
            ? d.List("jobs").OfType<Dictionary<string, object?>>()
                .Select(j => new Job(j.GetValueOrDefault("name") as string ?? "", j.GetValueOrDefault("pay") as string ?? "", j.GetValueOrDefault("flat") as double?))
                .ToList()
            : DefaultJobs;

        var promo = d.Map("promoPay");
        this.PromoPf = promo.GetValueOrDefault("pf") is double pf and >= 0 ? pf : 60_000;
        this.PromoShout = promo.GetValueOrDefault("shout") is double sh and >= 0 ? sh : 140_000;
        this.ClockEarlyMinutes = d.Map("clockRules").GetValueOrDefault("early") is double e and >= 0 ? e : 60;
        this.StartTime = d.Str("startTime") is { Length: > 0 } st ? st : "21:00";
        this.CustomRoles = d.List("customRoles").OfType<Dictionary<string, object?>>()
            .Where(c => c.GetValueOrDefault("id") is string)
            .ToDictionary(c => (string)c["id"]!, c => c.GetValueOrDefault("label") as string ?? "");
    }

    public string TimeZone { get; }
    public bool Maintenance { get; }
    public string AdminRoster { get; }
    public List<string> Rooms { get; }
    public double ReceptionBonus { get; }
    public double VipDiscount { get; }
    public double RpPrice { get; }
    public double ErpPrice { get; }
    public double RoomRental { get; }
    public double GuestRatePublic { get; }
    public double GuestRatePrivate { get; }
    public double CollabRate { get; }
    public List<double?> PayGrades { get; }
    public List<Job> Jobs { get; }
    public double PromoPf { get; }
    public double PromoShout { get; }
    public double ClockEarlyMinutes { get; }
    public string StartTime { get; }
    public Dictionary<string, string> CustomRoles { get; }

    private static List<Job> DefaultJobs =>
    [
        new("Goddess", "infinite", null), new("Owner", "6", null), new("Co Owner", "6", null),
        new("Manager", "5", null), new("Asst. Manager", "4", null),
        new("DJ", "flat", 2_000_000), new("Photographer", "flat", 1_000_000), new("Fortune Teller", "flat", 1_000_000),
        new("Bouncer", "3", null), new("Bartender", "2", null), new("Dealer", "2", null), new("Receptionist", "2", null),
        new("Host", "1", null), new("Dancer", "1", null),
    ];
}
