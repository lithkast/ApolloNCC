using System;
using ApolloNCC.Api;

namespace ApolloNCC.Services;

/// <summary>
/// What the UI shows for a profile. This is only for tidiness: the web app enforces the same rules on every request.
/// </summary>
public static class Permissions
{
    public static bool IsOwner(this Profile p) => p.Rank >= Rank.CoOwner;

    public static bool IsManagement(this Profile p) => p.Rank >= Rank.AssistantManager;

    public static bool HasJob(this Profile p, string job) =>
        p.Jobs.Exists(j => string.Equals(j, job, StringComparison.OrdinalIgnoreCase));

    private static bool Manages(this Profile p, string department) =>
        p.IsManagement() && string.Equals(p.Department, department, StringComparison.OrdinalIgnoreCase);

    /// <summary>Managers see their department, owners see everyone.</summary>
    public static bool CanSeeStaff(this Profile p) => p.IsManagement();

    public static bool CanUseReception(this Profile p) =>
        p.IsOwner() || p.HasJob(Names.JobReceptionist) || p.Manages(Names.DeptFrontOfHouse);

    /// <summary>Bookings overview, banned patron alerts and patron warnings. The blacklist itself is open to all staff.</summary>
    public static bool CanUseSecurityTools(this Profile p) =>
        p.IsOwner() || p.HasJob(Names.JobBouncer) || p.Manages(Names.DeptSecurity);

    public static string Title(this Profile p) => p.Rank switch
    {
        Rank.Owner => "Owner",
        Rank.CoOwner => "Co-Owner",
        Rank.Manager => $"{p.Department} Manager",
        Rank.AssistantManager => $"Assistant {p.Department} Manager",
        _ => p.Jobs.Count > 0 ? string.Join(" / ", p.Jobs) : p.Department,
    };
}
