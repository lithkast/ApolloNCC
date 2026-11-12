using System;
using System.Collections.Generic;
using System.Linq;
using ApolloNCC.Api;

namespace ApolloNCC.Services;

/// <summary>
/// What the signed-in login may see and do, read from its account the way the site does (public/index.html).
/// This only decides what the plugin shows: the database's rules are what actually allow or refuse a change.
/// </summary>
public sealed class Access
{
    /// <summary>The site's administrator (firestore.rules, isOwner).</summary>
    public const string AdminUid = "1CZiVZOMhMUIdhhJNwTelBrIeYb2";

    private static readonly Dictionary<string, string> RoleLabels = new()
    {
        ["dancer"] = "Dancer", ["host"] = "Host", ["receptionist"] = "Receptionist", ["bartender"] = "Bartender",
        ["dealer"] = "Dealer", ["bouncer"] = "Bouncer", ["fortune"] = "Fortune Teller", ["photographer"] = "Photographer",
        ["dj"] = "DJ",
        ["asst_foh"] = "Asst. Manager (Front of House)", ["asst_dancer"] = "Asst. Manager (Dancer)",
        ["asst_bar"] = "Asst. Manager (Bar)", ["asst_bouncer"] = "Asst. Manager (Bouncer)", ["asst_dealer"] = "Asst. Manager (Dealer)",
        ["mgr_foh"] = "Manager (Front of House)", ["mgr_dancer"] = "Manager (Dancer)", ["mgr_bar"] = "Manager (Bar)",
        ["mgr_bouncer"] = "Manager (Bouncer)", ["mgr_dealer"] = "Manager (Dealer)",
        ["coowner"] = "Co Owner", ["owner"] = "Owner", ["none"] = "View only",
    };

    // the jobs each department's manager (and assistant manager) looks after: MANAGED_JOBS on the site
    private static readonly Dictionary<string, string[]> ManagedJobs = new()
    {
        ["bar"] = ["Bartender"],
        ["foh"] = ["Host", "Receptionist"],
        ["dancer"] = ["Dancer"],
        ["bouncer"] = ["Bouncer"],
        ["dealer"] = ["Dealer", "Fortune Teller"],
    };

    private readonly Dictionary<string, object?> tabs;
    private readonly bool maintenance;

    public Access(string uid, Doc? account, VenueConfig cfg)
    {
        this.Uid = uid;
        this.IsAdmin = uid == AdminUid;
        this.IsStaff = this.IsAdmin || account != null;
        this.Role = account?.Str("role") ?? "";
        this.AccountName = account?.Str("name") ?? "";
        this.tabs = account?.Map("tabs") ?? [];
        this.maintenance = cfg.Maintenance;
        this.RoleLabel = this.IsAdmin
            ? "Administrator"
            : RoleLabels.GetValueOrDefault(this.Role) ?? cfg.CustomRoles.GetValueOrDefault(this.Role) ?? "Staff";
    }

    public string Uid { get; }
    public bool IsAdmin { get; }

    /// <summary>Has a staff login. A Mortal (signed up, not staff) can sign in but sees nothing here.</summary>
    public bool IsStaff { get; }
    public string Role { get; }
    public string RoleLabel { get; }
    public string AccountName { get; }

    public bool IsOwner => this.IsAdmin || this.Role is "owner" or "coowner";

    /// <summary>"bar", "foh", "dancer", "bouncer" or "dealer" for that department's manager or assistant.</summary>
    public string? Department =>
        this.Role.StartsWith("mgr_") || this.Role.StartsWith("asst_") ? this.Role[(this.Role.IndexOf('_') + 1)..] : null;

    /// <summary>Managers see their department on HR; owners see everyone.</summary>
    public bool SeesStaff => this.IsOwner || (this.Department != null && ManagedJobs.ContainsKey(this.Department));

    public bool CanUseReception => this.CanEdit("reception");

    /// <summary>Bouncers, their managers and the owners: bookings, warnings and banned-patron alerts.</summary>
    public bool IsSecurity => this.IsOwner || this.Role is "bouncer" or "mgr_bouncer" or "asst_bouncer";

    /// <summary>Whoever keeps the Blacklist on the site (security's managers and the owners) can remove a warning.</summary>
    public bool CanRemoveWarnings => this.CanEdit("bans");

    /// <summary>The bar's managers and the owners: record buyouts.</summary>
    public bool CanEditBar => this.CanEdit("bar");

    public bool CanViewCasino => this.IsAdmin || (this.tabs.GetValueOrDefault("casino") is double level && level >= 1);

    /// <summary>Dealers, their managers and the owners put their own shift on the casino sheet.</summary>
    public bool CanEditCasino => this.CanEdit("casino");

    /// <summary>The dealers' managers and the owners assign dealers and correct profits.</summary>
    public bool ManagesCasino => this.CanEditCasino && (this.IsOwner || this.Department == "dealer");

    /// <summary>Owners and co-owners switch collab nights and clear Hermes' review list.</summary>
    public bool CanChangeNight => this.IsAdmin || (this.IsOwner && !this.maintenance);

    public bool Sees(HrRow row) =>
        this.IsOwner || row.Login == this.Uid ||
        (this.Department is { } d && ManagedJobs.GetValueOrDefault(d)?.Contains(row.Job.Trim(), StringComparer.OrdinalIgnoreCase) == true);

    /// <summary>2 on a page means it can be changed there (firestore.rules, canEdit).</summary>
    private bool CanEdit(string tab) =>
        this.IsAdmin || (!this.maintenance && this.tabs.GetValueOrDefault(tab) is double level && level >= 2);
}
