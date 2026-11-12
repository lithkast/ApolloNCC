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

/// <summary>One thing on the bar menu (menu/&lt;id&gt;), with its preparation macro if it has one (barmacros/&lt;id&gt;).</summary>
public sealed class MenuItem(Doc d, string macro)
{
    public string Id { get; } = d.Id;
    public string Section { get; } = d.Str("type").Trim() is { Length: > 0 } t ? t : "Other";
    public string Name { get; } = d.Str("name").Trim();

    /// <summary>The in-game item to hand over.</summary>
    public string Item { get; } = d.Str("item").Trim();
    public double? Cost { get; } = d.Num("cost");
    public string Description { get; } = d.Str("desc").Trim();

    /// <summary>A small picture, kept on the item itself as a data: address. Empty when there is none.</summary>
    public string Photo { get; } = d.Str("photo") is { } p && p.StartsWith("data:image/") ? p : "";
    public double Order { get; } = d.Num("order") ?? 0;

    /// <summary>The macro's lines, one chat command each.</summary>
    public IReadOnlyList<string> Macro { get; } =
        macro.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
}

/// <summary>One recorded bar buyout (buyouts/&lt;id&gt;).</summary>
public sealed class Buyout(Doc d)
{
    public string Id { get; } = d.Id;
    public string Patron { get; } = d.Str("patron");
    public double? Hours { get; } = d.Num("hours");
    public double Amount { get; } = d.Num("amount") ?? 0;
    public double House { get; } = d.Num("house") ?? 0;
    public double Pool { get; } = d.Num("pool") ?? 0;
    public List<string> Bartenders { get; } = d.List("bartenders").OfType<string>().ToList();
    public double? Each { get; } = d.Num("each");
    public string By { get; } = d.Str("by");
    public DateTimeOffset At { get; } = DateTimeOffset.FromUnixTimeMilliseconds((long)(d.Num("order") ?? 0));
}

/// <summary>What one dealer is doing on one casino shift, as entered in the plugin (night/casino, entries).</summary>
public sealed class CasinoEntry(string key, Dictionary<string, object?> f)
{
    public string Key { get; } = key;
    public string Name { get; } = f.GetValueOrDefault("name") as string ?? "";
    public string Uid { get; } = f.GetValueOrDefault("uid") as string ?? "";
    public int Shift { get; } = f.GetValueOrDefault("shift") is double s ? (int)s : 1;
    public string Game { get; } = f.GetValueOrDefault("game") as string ?? "";

    /// <summary>The game's profit this shift. Null until the dealer enters it; after that only a manager changes it.</summary>
    public double? Profit { get; } = f.GetValueOrDefault("profit") as double?;
    public DateTimeOffset At { get; } = DateTimeOffset.FromUnixTimeMilliseconds((long)(f.GetValueOrDefault("at") as double? ?? 0));
}

/// <summary>Tonight's casino sheet (night/casino): two shifts, each with its gross profit and dealers.</summary>
public sealed class CasinoNight
{
    public CasinoNight(Doc? d)
    {
        d ??= new Doc("casino", []);
        this.Gross = [d.Num("s1Gross"), d.Num("s2Gross")];
        this.Dealers = [d.List("s1Dealers").OfType<string>().ToList(), d.List("s2Dealers").OfType<string>().ToList()];
        this.Entries = d.Map("entries")
            .Where(e => e.Value is Dictionary<string, object?>)
            .Select(e => new CasinoEntry(e.Key, (Dictionary<string, object?>)e.Value!))
            .OrderBy(e => e.Shift).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Index 0 is shift 1.</summary>
    public double?[] Gross { get; }
    public List<string>[] Dealers { get; }
    public List<CasinoEntry> Entries { get; }
}

/// <summary>One person's raffle tickets (raffle/&lt;id&gt;).</summary>
public sealed class RaffleEntry(Doc d)
{
    public string Id { get; } = d.Id;
    public string Patron { get; } = d.Str("patron").Trim();
    public string World { get; } = d.Str("world");
    public string Discord { get; } = d.Str("discord");
    public double Tickets { get; } = d.Num("tickets") is { } t and > 0 ? t : 0;
    public string TakenBy { get; } = d.Str("takenBy");
    public bool Paid { get; } = d.Bool("paid");
    public double Order { get; } = d.Num("order") ?? 0;
}

public sealed record RaffleWinner(string Patron, string World, double Tickets, double Pot, double At, string By, string ByUid, bool Mentioned);

/// <summary>The state of tonight's raffle (night/raffle).</summary>
public sealed class RaffleNight
{
    public RaffleNight(Doc? d)
    {
        this.Start = d?.Num("start") is { } s and > 0 ? s : 0;
        this.Closed = d?.Bool("closed") == true;
        if (d?.Fields.GetValueOrDefault("winner") is Dictionary<string, object?> w)
        {
            string S(string k) => w.GetValueOrDefault(k) as string ?? "";
            double N(string k) => w.GetValueOrDefault(k) as double? ?? 0;
            this.Winner = new RaffleWinner(S("patron"), S("world"), N("tickets"), N("pot"), N("at"), S("by"), S("byUid"), S("discordId").Length > 0);
        }
    }

    /// <summary>Gil in the pot before any ticket is sold.</summary>
    public double Start { get; }
    public bool Closed { get; }
    public RaffleWinner? Winner { get; }
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
        this.CloseTime = d.Str("closeTime") is { Length: > 0 } ct ? ct : "01:00";
        this.BarSections = d.Fields.ContainsKey("barSections")
            ? d.List("barSections").OfType<string>().Select(x => x.Trim()).Where(x => x.Length > 0).Distinct().ToList()
            : ["Weekly Specials", "On Tap", "Cocktails", "Appetizers", "Entrees", "Desserts", "Tub Grub"];
        this.BarBuyoutRate = d.Num("barBuyoutRate") is { } br and > 0 ? br : 1_000_000;
        this.HouseSplit = d.Num("houseSplit") is { } hs and >= 0 ? hs : 0.5;
        this.BarSplit = d.Num("barSplit") is { } bs and >= 0 ? bs : 0.5;
        this.CasinoBrackets = d.Fields.ContainsKey("casinoBrackets")
            ? d.List("casinoBrackets").OfType<Dictionary<string, object?>>()
                .Select(b => (b.GetValueOrDefault("upTo") as double?, b.GetValueOrDefault("rate") as double? ?? 0)).ToList()
            : [(16_000_000, 0.2), (20_000_000, 0.25), (25_000_000, 0.3), (30_000_000, 0.35), (null, 0.4)];
        this.RaffleTicketPrice = N("raffleTicketPrice", 100_000);
        this.RaffleClubCut = N("raffleClubCut", 0.5);
        this.RaffleMacro = d.Fields.ContainsKey("raffleMacro")
            ? d.Str("raffleMacro")
            : "/y 50/50 Raffle Here! Buy tickets before we roll! The lottery is currently at {total}g. Tickets are {price} each with no maximum. Come to the blinking Cactuar to purchase tickets. Must be in our discord to participate. May the luck of Tyche be in your favor!";
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
    public string CloseTime { get; }
    public List<string> BarSections { get; }

    /// <summary>A bar buyout costs this much for each hour left until close.</summary>
    public double BarBuyoutRate { get; }
    public double HouseSplit { get; }
    public double BarSplit { get; }

    /// <summary>The dealers' progressive share of a shift's profit: each slice earns its bracket's rate.</summary>
    public List<(double? UpTo, double Rate)> CasinoBrackets { get; }
    public double RaffleTicketPrice { get; }
    public double RaffleClubCut { get; }

    /// <summary>The raffle's announcement, with {total} and {price} to fill in.</summary>
    public string RaffleMacro { get; }
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
