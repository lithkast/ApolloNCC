using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace ApolloNCC.Api;

/// <summary>Talks to the Apollo web app's plugin API (docs/API.md).</summary>
public sealed class HttpApolloApi : IApolloApi, IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Func<string> baseUrl;
    private readonly Func<string> secretKey;
    private readonly Func<string?> character;

    /// <param name="baseUrl">Web app root, e.g. https://apolloncc.org.</param>
    /// <param name="secretKey">The employee's plugin key.</param>
    /// <param name="character">"Name@World" of the logged-in character, so the web app knows which alt is in use.</param>
    public HttpApolloApi(Func<string> baseUrl, Func<string> secretKey, Func<string?> character)
    {
        this.baseUrl = baseUrl;
        this.secretKey = secretKey;
        this.character = character;
    }

    public void Dispose() => this.http.Dispose();

    public Task<Profile> GetProfileAsync(CancellationToken ct) => this.Get<Profile>("me", ct);
    public Task<VenueMeta> GetMetaAsync(CancellationToken ct) => this.Get<VenueMeta>("meta", ct);

    public Task<EmployeeStatus> GetMyStatusAsync(CancellationToken ct) => this.Get<EmployeeStatus>("hr/me", ct);
    public Task<EmployeeStatus> ClockInAsync(CancellationToken ct) => this.Post<EmployeeStatus>("hr/clock-in", null, ct);
    public Task<EmployeeStatus> ClockOutAsync(CancellationToken ct) => this.Post<EmployeeStatus>("hr/clock-out", null, ct);

    public Task<EmployeeStatus> PfClockInAsync(bool partyFinder, bool shouts, CancellationToken ct) =>
        this.Post<EmployeeStatus>("hr/pf/clock-in", new { partyFinder, shouts }, ct);

    public Task<EmployeeStatus> PfClockOutAsync(CancellationToken ct) => this.Post<EmployeeStatus>("hr/pf/clock-out", null, ct);

    public Task<List<EmployeeStatus>> GetStaffAsync(CancellationToken ct) => this.Get<List<EmployeeStatus>>("hr/staff", ct);

    public Task<EmployeeStatus> ApproveClockInAsync(string employeeId, CancellationToken ct) =>
        this.Post<EmployeeStatus>($"hr/staff/{Uri.EscapeDataString(employeeId)}/approve-clock-in", null, ct);

    public Task<EmployeeStatus> SetCollabAsync(string employeeId, bool active, string job, CancellationToken ct) =>
        this.Post<EmployeeStatus>($"hr/staff/{Uri.EscapeDataString(employeeId)}/collab", new { active, job }, ct);

    public Task<EmployeeStatus> DecideCollabJobAsync(string employeeId, bool approve, CancellationToken ct) =>
        this.Post<EmployeeStatus>($"hr/staff/{Uri.EscapeDataString(employeeId)}/collab/decision", new { approve }, ct);

    public Task<List<Booking>> GetMyBookingsAsync(CancellationToken ct) => this.Get<List<Booking>>("bookings/mine", ct);
    public Task<ReceptionSummary> GetReceptionAsync(CancellationToken ct) => this.Get<ReceptionSummary>("reception", ct);

    public Task<Booking> CreateBookingAsync(CreateBookingRequest request, CancellationToken ct) =>
        this.Post<Booking>("bookings", request, ct);

    public Task EndBookingAsync(string bookingId, CancellationToken ct) =>
        this.Send(HttpMethod.Post, $"bookings/{Uri.EscapeDataString(bookingId)}/end", null, ct);

    public Task<List<BlacklistEntry>> GetBlacklistAsync(CancellationToken ct) => this.Get<List<BlacklistEntry>>("security/blacklist", ct);
    public Task<List<Booking>> GetSecurityBookingsAsync(CancellationToken ct) => this.Get<List<Booking>>("security/bookings", ct);
    public Task<List<PatronWarning>> GetWarningsAsync(CancellationToken ct) => this.Get<List<PatronWarning>>("security/warnings", ct);

    public Task<PatronWarning> AddWarningAsync(string name, string? world, string reason, CancellationToken ct) =>
        this.Post<PatronWarning>("security/warnings", new { name, world, reason }, ct);

    public Task<List<Sighting>> GetSightingsAsync(CancellationToken ct) => this.Get<List<Sighting>>("security/sightings", ct);

    public Task ReportSightingAsync(string blacklistId, string name, string? world, CancellationToken ct) =>
        this.Send(HttpMethod.Post, "security/sightings", new { blacklistId, name, world }, ct);

    public Task<DiscordLinkCode> StartDiscordLinkAsync(CancellationToken ct) => this.Post<DiscordLinkCode>("discord/link-code", null, ct);

    private Task<T> Get<T>(string path, CancellationToken ct) => this.Send<T>(HttpMethod.Get, path, null, ct);

    private Task<T> Post<T>(string path, object? body, CancellationToken ct) => this.Send<T>(HttpMethod.Post, path, body, ct);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await this.SendCore(method, path, body, ct).ConfigureAwait(false);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(Json, ct).ConfigureAwait(false)
                   ?? throw new ApolloApiException("The web app sent an empty reply.");
        }
        catch (JsonException)
        {
            throw new ApolloApiException("The web app sent a reply the plugin could not read. The plugin may be out of date.");
        }
    }

    private async Task Send(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var response = await this.SendCore(method, path, body, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCore(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        var key = this.secretKey();
        if (string.IsNullOrWhiteSpace(key))
            throw new ApolloApiException("No plugin key set. Enter it under Settings.");

        var root = this.baseUrl().Trim().TrimEnd('/');
        if (!Uri.TryCreate($"{root}/api/plugin/{path}", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback))
            throw new ApolloApiException("The web app address under Settings must be an https:// address.");

        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
        if (this.character() is { Length: > 0 } who)
            request.Headers.TryAddWithoutValidation("X-Apollo-Character", who);
        if (body != null)
            request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await this.http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new ApolloApiException("Could not reach the Apollo web app.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApolloApiException("The Apollo web app took too long to answer.");
        }

        if (response.IsSuccessStatusCode)
            return response;

        using (response)
        {
            var message = await ReadError(response, ct).ConfigureAwait(false);
            throw new ApolloApiException(
                message ?? response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "The web app rejected the plugin key. Check it under Settings.",
                    HttpStatusCode.Forbidden => "Your role does not allow that.",
                    HttpStatusCode.NotFound => "The web app does not know that request. The plugin or web app may be out of date.",
                    _ => $"The web app answered with an error ({(int)response.StatusCode}).",
                },
                keyRejected: response.StatusCode == HttpStatusCode.Unauthorized);
        }
    }

    /// <summary>Errors come back as {"error": "..."}; that text is shown to the user as is.</summary>
    private static async Task<string?> ReadError(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("error", out var e) &&
                   e.ValueKind == JsonValueKind.String
                ? e.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
