using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ApolloNCC.Api;

/// <summary>Everything the plugin asks of the Apollo web app. See docs/API.md for the wire format.</summary>
public interface IApolloApi
{
    Task<Profile> GetProfileAsync(CancellationToken ct);
    Task<VenueMeta> GetMetaAsync(CancellationToken ct);

    Task<EmployeeStatus> GetMyStatusAsync(CancellationToken ct);
    Task<EmployeeStatus> ClockInAsync(CancellationToken ct);
    Task<EmployeeStatus> ClockOutAsync(CancellationToken ct);
    Task<EmployeeStatus> PfClockInAsync(bool partyFinder, bool shouts, CancellationToken ct);
    Task<EmployeeStatus> PfClockOutAsync(CancellationToken ct);

    Task<List<EmployeeStatus>> GetStaffAsync(CancellationToken ct);
    Task<EmployeeStatus> ApproveClockInAsync(string employeeId, CancellationToken ct);
    Task<EmployeeStatus> SetCollabAsync(string employeeId, bool active, string job, CancellationToken ct);
    Task<EmployeeStatus> DecideCollabJobAsync(string employeeId, bool approve, CancellationToken ct);

    Task<List<Booking>> GetMyBookingsAsync(CancellationToken ct);
    Task<ReceptionSummary> GetReceptionAsync(CancellationToken ct);
    Task<Booking> CreateBookingAsync(CreateBookingRequest request, CancellationToken ct);
    Task EndBookingAsync(string bookingId, CancellationToken ct);

    Task<List<BlacklistEntry>> GetBlacklistAsync(CancellationToken ct);
    Task<List<Booking>> GetSecurityBookingsAsync(CancellationToken ct);
    Task<List<PatronWarning>> GetWarningsAsync(CancellationToken ct);
    Task<PatronWarning> AddWarningAsync(string name, string? world, string reason, CancellationToken ct);
    Task<List<Sighting>> GetSightingsAsync(CancellationToken ct);
    Task ReportSightingAsync(string blacklistId, string name, string? world, CancellationToken ct);

    Task<DiscordLinkCode> StartDiscordLinkAsync(CancellationToken ct);
}

public sealed class ApolloApiException(string message, bool keyRejected = false) : System.Exception(message)
{
    /// <summary>The web app no longer accepts the plugin key, e.g. it was revoked.</summary>
    public bool KeyRejected { get; } = keyRejected;
}
