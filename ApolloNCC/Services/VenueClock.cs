using System;

namespace ApolloNCC.Services;

/// <summary>Venue time rules. The web app is the authority; this only lets the plugin warn up front.</summary>
public static class VenueClock
{
    /// <summary>Sunday clock-ins before this Eastern time need a manager's approval.</summary>
    public static readonly TimeSpan SundayOpenClockIn = new(20, 53, 0);

    private static readonly TimeZoneInfo? Eastern = FindEastern();

    public static DateTimeOffset EasternNow(DateTimeOffset utcNow) =>
        Eastern != null ? TimeZoneInfo.ConvertTime(utcNow, Eastern) : utcNow.ToOffset(TimeSpan.FromHours(-5));

    public static bool ClockInNeedsApproval(DateTimeOffset utcNow)
    {
        var et = EasternNow(utcNow);
        return et.DayOfWeek == DayOfWeek.Sunday && et.TimeOfDay < SundayOpenClockIn;
    }

    private static TimeZoneInfo? FindEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return null;
    }
}
