using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ApolloNCC.Api;

namespace ApolloNCC.Services;

/// <summary>What someone has made tonight, the way the HR page and Hermes' /shift work it out.</summary>
public sealed record ShiftSummary
{
    /// <summary>Clocked in tonight (they may have clocked out since).</summary>
    public bool OnShiftTonight { get; init; }
    public bool ClockedOut { get; init; }
    public double? TimeIn { get; init; }
    public double? TimeOut { get; init; }
    public double? Hours { get; init; }
    public bool FlatPay { get; init; }

    /// <summary>Null when the job has no pay set; infinity for the Goddess.</summary>
    public double? ShiftPay { get; init; }
    public double BaseRate { get; init; }
    public bool Promoting { get; init; }
    public string PromoKind { get; init; } = "";
    public double? PromoIn { get; init; }
    public double PfHours { get; init; }
    public double ShoutHours { get; init; }
    public double PromoPay { get; init; }
    public double Bookings { get; init; }
    public double ReceptionBonus { get; init; }
    public double BarShare { get; init; }
    public double DealerShare { get; init; }

    public double Total => (this.ShiftPay ?? 0) + this.PromoPay + this.Bookings + this.ReceptionBonus + this.BarShare + this.DealerShare;
}

/// <summary>
/// Venue time and pay sums. These mirror functions/clock.js and the "spreadsheet formulas" in public/index.html,
/// so the plugin shows the same figures as the site. If those change, change these.
/// </summary>
public static class Venue
{
    /// <summary>A clock-in stamped more than this long ago is last night's.</summary>
    private static readonly TimeSpan Stale = TimeSpan.FromHours(16);

    // .NET on Windows without ICU (as under Wine) only knows Windows zone names; these are the zones the site offers.
    private static readonly Dictionary<string, string> WindowsZones = new()
    {
        ["America/New_York"] = "Eastern Standard Time",
        ["America/Chicago"] = "Central Standard Time",
        ["America/Denver"] = "Mountain Standard Time",
        ["America/Los_Angeles"] = "Pacific Standard Time",
        ["UTC"] = "UTC",
        ["Europe/London"] = "GMT Standard Time",
        ["Europe/Berlin"] = "W. Europe Standard Time",
        ["Asia/Tokyo"] = "Tokyo Standard Time",
        ["Australia/Sydney"] = "AUS Eastern Standard Time",
    };

    public static TimeZoneInfo Zone(VenueConfig cfg)
    {
        foreach (var id in new[] { cfg.TimeZone, WindowsZones.GetValueOrDefault(cfg.TimeZone), "America/New_York", "Eastern Standard Time" })
        {
            if (id == null)
                continue;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return TimeZoneInfo.Utc;
    }

    public static DateTimeOffset VenueTime(VenueConfig cfg, DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone(cfg));

    /// <summary>Seconds into the venue's day.</summary>
    public static double VenueSeconds(VenueConfig cfg, DateTimeOffset now) => Math.Floor(VenueTime(cfg, now).TimeOfDay.TotalSeconds);

    /// <summary>The 7-minute rule: 8:52 is 8:45, 8:53 is 9:00, 9:07 is 9:00 and 9:08 is 9:15.</summary>
    public static double RoundToQuarter(double sec)
    {
        var m = Math.Floor(sec / 60);
        var r = m % 15;
        return ((r <= 7 ? m - r : m + 15 - r) * 60) % 86400;
    }

    public static string FormatSeconds(double sec)
    {
        var h = (int)Math.Floor(sec / 3600) % 24;
        var m = (int)Math.Floor(sec / 60) % 60;
        return $"{((h + 11) % 12) + 1}:{m:00} {(h < 12 ? "AM" : "PM")}";
    }

    public static string Gil(double? amount) => amount switch
    {
        null => "-",
        double.PositiveInfinity => "Infinite",
        { } v => Math.Round(v).ToString("N0", CultureInfo.InvariantCulture) + "g",
    };

    public static string HoursText(double hours) =>
        Math.Round(hours, 2).ToString("0.##", CultureInfo.InvariantCulture) + (Math.Round(hours, 2) == 1 ? " hour" : " hours");

    public static string PromoLabel(string kind) => kind switch
    {
        "pf" => "Party finder",
        "shout" => "Shouts",
        "both" => "Party finder and shouts",
        _ => "",
    };

    public static double? BasePay(VenueConfig cfg, string job)
    {
        var j = cfg.Jobs.Find(x => x.Name == job);
        if (j == null || j.Pay is "" or "none" or "flat")
            return null;
        if (j.Pay == "infinite")
            return double.PositiveInfinity;
        return int.TryParse(j.Pay, out var grade) && grade >= 1 && grade <= cfg.PayGrades.Count ? cfg.PayGrades[grade - 1] : null;
    }

    public static double? ReceptionTotal(VenueConfig cfg, Doc r) =>
        ReceptionTotal(cfg, r.Str("rpType"), r.Num("duration"), r.Num("guests"), r.Bool("vip"));

    public static double? ReceptionTotal(VenueConfig cfg, string rpType, double? hours, double? guests, bool vip)
    {
        if (hours is not { } h || guests is not { } g)
            return null;
        var extra = Math.Max(g - 1, 0);
        double? price = rpType switch
        {
            "Public" => (cfg.RpPrice * h) + (extra * cfg.GuestRatePublic),
            "Private" => (cfg.ErpPrice * h) + (extra * cfg.GuestRatePrivate),
            "No Staff" => (cfg.RoomRental * h) + (extra * cfg.GuestRatePublic),
            _ => null,
        };
        return price is { } p && vip ? p * cfg.VipDiscount : price;
    }

    public static bool IsStale(HrRow r, DateTimeOffset now) =>
        r.ClockedAt is { } at && now.ToUnixTimeMilliseconds() - at > Stale.TotalMilliseconds;

    public static bool OnClock(HrRow r, DateTimeOffset now) => r.ClockIn && !r.ClockOut && !IsStale(r, now);

    public static bool Promoting(HrRow r, DateTimeOffset now) =>
        PromoLabel(r.PromoKind).Length > 0 && r.PromoIn != null && PromoFresh(r, now);

    /// <summary>Shift and promotion pay for one roster row. Add <see cref="WithExtras"/> for the rest.</summary>
    public static ShiftSummary Summarise(VenueConfig cfg, HrRow r, bool collabNight, DateTimeOffset now)
    {
        var stamp = RoundToQuarter(VenueSeconds(cfg, now));
        var tonight = r.ClockIn && !IsStale(r, now);
        var job = cfg.Jobs.Find(j => j.Name == r.Job);
        var flat = job?.Pay == "flat";
        var rate = BasePay(cfg, r.Job);
        var mult = collabNight ? cfg.CollabRate : 1;

        double? outSec = r.ClockOut && r.TimeOut != null ? r.TimeOut : stamp;
        double? hours = tonight && r.TimeIn is { } tin && outSec is { } o ? SpanHours(tin, o) : null;
        double? pay = null;
        if (tonight)
        {
            if (flat)
                pay = job!.Flat;
            else if (rate is { } b && hours is { } h)
                pay = double.IsPositiveInfinity(b) ? b : Round1000(b * mult * h);
        }

        var fresh = PromoFresh(r, now);
        var pf = fresh ? r.PromoPf : 0;
        var shout = fresh ? r.PromoShout : 0;
        var promoting = Promoting(r, now);
        if (promoting)
        {
            // promotion still running counts up to now, to the quarter hour
            var so = SpanHours(r.PromoIn!.Value, stamp);
            if (r.PromoKind != "shout")
                pf = Math.Round(pf + so, 2);
            if (r.PromoKind != "pf")
                shout = Math.Round(shout + so, 2);
        }

        return new ShiftSummary
        {
            OnShiftTonight = tonight,
            ClockedOut = tonight && r.ClockOut,
            TimeIn = tonight ? r.TimeIn : null,
            TimeOut = tonight && r.ClockOut ? r.TimeOut : null,
            Hours = hours,
            FlatPay = flat,
            ShiftPay = pay,
            BaseRate = flat ? job!.Flat ?? 0 : (rate ?? 0) * mult,
            Promoting = promoting,
            PromoKind = promoting ? r.PromoKind : "",
            PromoIn = promoting ? r.PromoIn : null,
            PfHours = pf,
            ShoutHours = shout,
            PromoPay = Round1000((pf * cfg.PromoPf) + (shout * cfg.PromoShout)),
        };
    }

    /// <summary>Adds booking fees, the reception bonus and the bar buyout share someone earned tonight.</summary>
    /// <param name="receptionist">Tonight's receptionist (night/reception).</param>
    /// <param name="buyouts">Tonight's bar buyouts.</param>
    public static ShiftSummary WithExtras(
        ShiftSummary s, VenueConfig cfg, string name, IReadOnlyList<Booking> bookings, string receptionist, IReadOnlyList<Doc> buyouts)
    {
        bool Me(string? x) => name.Length > 0 && string.Equals(x?.Trim(), name, StringComparison.OrdinalIgnoreCase);

        var all = bookings.Sum(b => b.Total ?? 0);
        return s with
        {
            Bookings = Math.Round(bookings.Where(b => Me(b.Employee)).Sum(b => b.Total ?? 0)),
            ReceptionBonus = Me(receptionist) ? Math.Round(all * cfg.ReceptionBonus) : 0,
            BarShare = Math.Round(buyouts
                .Where(b => b.Num("each") != null && b.List("bartenders").Any(x => Me(x as string)))
                .Sum(b => b.Num("each")!.Value)),
        };
    }

    private static bool PromoFresh(HrRow r, DateTimeOffset now) =>
        r.PromoAt is { } at && now.ToUnixTimeMilliseconds() - at <= Stale.TotalMilliseconds;

    private static double SpanHours(double a, double b) => ((((b - a) % 86400) + 86400) % 86400) / 3600;

    // JavaScript's Math.round: halves go up.
    private static double Round1000(double x) => Math.Floor((x / 1000) + 0.5) * 1000;
}
