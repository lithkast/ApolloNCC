using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace ApolloNCC.Api;

public enum ApolloError
{
    Other,

    /// <summary>The sign-in is gone (password changed, account disabled): sign in again.</summary>
    SignedOut,

    /// <summary>The database rules refused it.</summary>
    Denied,

    /// <summary>A function the plugin needs has not been deployed to the web app yet.</summary>
    NotDeployed,
}

public sealed class ApolloException(string message, ApolloError kind = ApolloError.Other) : Exception(message)
{
    public ApolloError Kind { get; } = kind;
}

/// <summary>Who is signed in. The refresh token is what gets saved; the password never is.</summary>
public sealed record Session(string Uid, string Email, string RefreshToken);

/// <summary>
/// The plugin's connection to the Apollo web app's Firebase project. It signs in as the staff member, with the
/// same email and password as apolloncc.org, so the database's own access rules decide what it may read and
/// change, exactly as they do for the site.
/// </summary>
public sealed class FirebaseClient : IDisposable
{
    // The web app's public Firebase settings (public/index.html, FIREBASE_CONFIG). These identify the project;
    // they are not secrets, and grant nothing without a sign-in.
    private const string ApiKey = "AIzaSyCkp9nMJbVCEpO_OmF5Pcp6nhbjkRXMB4c";
    private const string ProjectId = "apollohr-9b84f";

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly SemaphoreSlim tokenGate = new(1, 1);
    private readonly string authUrl;
    private readonly string tokenUrl;
    private readonly string documentsUrl;
    private readonly string functionsUrl;

    private string? idToken;
    private DateTimeOffset idTokenExpires;

    /// <param name="session">A saved sign-in to carry on with, if there is one.</param>
    /// <param name="testServer">Only for tests: one address standing in for all of Firebase.</param>
    public FirebaseClient(Session? session, string? testServer = null)
    {
        this.Session = session;
        this.authUrl = testServer ?? "https://identitytoolkit.googleapis.com";
        this.tokenUrl = testServer ?? "https://securetoken.googleapis.com";
        this.documentsUrl = $"{testServer ?? "https://firestore.googleapis.com"}/v1/projects/{ProjectId}/databases/(default)/documents";
        this.functionsUrl = testServer ?? $"https://us-central1-{ProjectId}.cloudfunctions.net";
    }

    public Session? Session { get; private set; }

    public void Dispose()
    {
        this.http.Dispose();
        this.tokenGate.Dispose();
    }

    public async Task<Session> SignInAsync(string email, string password, CancellationToken ct)
    {
        var body = new JsonObject { ["email"] = email, ["password"] = password, ["returnSecureToken"] = true };
        using var response = await this.SendAsync(
            () => Json(HttpMethod.Post, $"{this.authUrl}/v1/accounts:signInWithPassword?key={ApiKey}", body), ct).ConfigureAwait(false);
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApolloException(ErrorCode(doc) switch
            {
                var c when c.StartsWith("INVALID_LOGIN_CREDENTIALS") || c.StartsWith("EMAIL_NOT_FOUND") ||
                           c.StartsWith("INVALID_PASSWORD") || c.StartsWith("INVALID_EMAIL") =>
                    "That email or password isn't right. They are the same ones you use on apolloncc.org.",
                var c when c.StartsWith("USER_DISABLED") => "That login has been switched off. Ask an owner.",
                var c when c.StartsWith("TOO_MANY_ATTEMPTS") => "Too many tries. Wait a few minutes and try again.",
                var c => $"Could not sign in ({(c.Length > 0 ? c : ((int)response.StatusCode).ToString())}).",
            });
        }

        var root = doc.RootElement;
        this.Session = new Session(root.GetProperty("localId").GetString()!, email, root.GetProperty("refreshToken").GetString()!);
        this.idToken = root.GetProperty("idToken").GetString();
        this.idTokenExpires = DateTimeOffset.UtcNow.AddSeconds(int.Parse(root.GetProperty("expiresIn").GetString()!));
        return this.Session;
    }

    public void SignOut()
    {
        this.Session = null;
        this.idToken = null;
    }

    public async Task<Doc?> GetAsync(string path, CancellationToken ct)
    {
        using var response = await this.AuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{this.documentsUrl}/{path}"), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        EnsureOk(response, doc);
        return Doc.FromRest(doc.RootElement);
    }

    /// <summary>Every document in a collection (the venue's are all small), or the first few in an order.</summary>
    public async Task<List<Doc>> ListAsync(string collection, CancellationToken ct, string? orderBy = null, int? limit = null)
    {
        var all = new List<Doc>();
        string? page = null;
        do
        {
            var url = $"{this.documentsUrl}/{collection}?pageSize={limit ?? 300}"
                      + (orderBy != null ? $"&orderBy={Uri.EscapeDataString(orderBy)}" : "")
                      + (page != null ? $"&pageToken={Uri.EscapeDataString(page)}" : "");
            using var response = await this.AuthedAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct).ConfigureAwait(false);
            using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
            EnsureOk(response, doc);
            if (doc.RootElement.TryGetProperty("documents", out var documents))
            {
                foreach (var d in documents.EnumerateArray())
                    all.Add(Doc.FromRest(d));
            }

            page = doc.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page != null && limit == null && all.Count < 5000);
        return all;
    }

    /// <summary>The documents in a collection whose field equals a text value.</summary>
    public async Task<List<Doc>> WhereAsync(string collection, string field, string value, CancellationToken ct)
    {
        var query = new JsonObject
        {
            ["structuredQuery"] = new JsonObject
            {
                ["from"] = new JsonArray(new JsonObject { ["collectionId"] = collection }),
                ["where"] = new JsonObject
                {
                    ["fieldFilter"] = new JsonObject
                    {
                        ["field"] = new JsonObject { ["fieldPath"] = field },
                        ["op"] = "EQUAL",
                        ["value"] = new JsonObject { ["stringValue"] = value },
                    },
                },
                ["limit"] = 5,
            },
        };
        using var response = await this.AuthedAsync(() => Json(HttpMethod.Post, $"{this.documentsUrl}:runQuery", query), ct).ConfigureAwait(false);
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        EnsureOk(response, doc);
        var found = new List<Doc>();
        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.TryGetProperty("document", out var d))
                    found.Add(Doc.FromRest(d));
            }
        }

        return found;
    }

    /// <summary>Adds a new document under the given id. Fails if it already exists.</summary>
    public Task CreateAsync(string collection, string id, IReadOnlyDictionary<string, object?> fields, CancellationToken ct) =>
        this.WriteAsync(HttpMethod.Post, $"{this.documentsUrl}/{collection}?documentId={Uri.EscapeDataString(id)}", fields, ct);

    /// <summary>Replaces a document, making it if it isn't there.</summary>
    public Task SetAsync(string path, IReadOnlyDictionary<string, object?> fields, CancellationToken ct) =>
        this.WriteAsync(HttpMethod.Patch, $"{this.documentsUrl}/{path}", fields, ct);

    /// <summary>Changes only the given fields of a document.</summary>
    public Task PatchAsync(string path, IReadOnlyDictionary<string, object?> fields, CancellationToken ct)
    {
        var mask = new StringBuilder();
        foreach (var key in fields.Keys)
            mask.Append(mask.Length == 0 ? '?' : '&').Append("updateMask.fieldPaths=").Append(Uri.EscapeDataString(key));
        return this.WriteAsync(HttpMethod.Patch, $"{this.documentsUrl}/{path}{mask}", fields, ct);
    }

    public async Task DeleteAsync(string path, CancellationToken ct)
    {
        using var response = await this.AuthedAsync(() => new HttpRequestMessage(HttpMethod.Delete, $"{this.documentsUrl}/{path}"), ct).ConfigureAwait(false);
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        EnsureOk(response, doc);
    }

    /// <summary>Calls one of the web app's callable functions (functions/index.js) and returns its answer.</summary>
    public async Task<JsonElement> CallAsync(string name, JsonObject data, CancellationToken ct)
    {
        using var response = await this.AuthedAsync(
            () => Json(HttpMethod.Post, $"{this.functionsUrl}/{name}", new JsonObject { ["data"] = data.DeepClone() }), ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new ApolloException("The web app doesn't have the plugin's update yet. It needs to be deployed first.", ApolloError.NotDeployed);
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = ErrorCode(doc);
            throw new ApolloException(message.Length > 0 && message != "INTERNAL"
                ? message
                : $"The web app could not do that ({(int)response.StatusCode}).");
        }

        return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : default;
    }

    private async Task WriteAsync(HttpMethod method, string url, IReadOnlyDictionary<string, object?> fields, CancellationToken ct)
    {
        var body = new JsonObject { ["fields"] = Doc.ToRestFields(fields) };
        using var response = await this.AuthedAsync(() => Json(method, url, body), ct).ConfigureAwait(false);
        using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
        EnsureOk(response, doc);
    }

    private static HttpRequestMessage Json(HttpMethod method, string url, JsonNode body) =>
        new(method, url) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };

    private async Task<HttpResponseMessage> AuthedAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        var token = await this.TokenAsync(ct).ConfigureAwait(false);
        return await this.SendAsync(
            () =>
            {
                var request = make();
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                return request;
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>A sign-in token lasts an hour; this swaps the saved refresh token for a new one when needed.</summary>
    private async Task<string> TokenAsync(CancellationToken ct)
    {
        await this.tokenGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var session = this.Session ?? throw new ApolloException("Sign in under Settings first.", ApolloError.SignedOut);
            if (this.idToken != null && DateTimeOffset.UtcNow < this.idTokenExpires.AddMinutes(-2))
                return this.idToken;

            using var response = await this.SendAsync(
                () => new HttpRequestMessage(HttpMethod.Post, $"{this.tokenUrl}/v1/token?key={ApiKey}")
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["refresh_token"] = session.RefreshToken,
                    }),
                },
                ct).ConfigureAwait(false);
            using var doc = await ReadAsync(response, ct).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new ApolloException("Your sign-in has ended. Sign in again under Settings.", ApolloError.SignedOut);
            EnsureOk(response, doc);

            this.idToken = doc.RootElement.GetProperty("id_token").GetString()!;
            this.idTokenExpires = DateTimeOffset.UtcNow.AddSeconds(int.Parse(doc.RootElement.GetProperty("expires_in").GetString()!));
            return this.idToken;
        }
        finally
        {
            this.tokenGate.Release();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, CancellationToken ct)
    {
        try
        {
            using var request = make();
            return await this.http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            throw new ApolloException("Could not reach the Apollo web app. Check your connection.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ApolloException("The Apollo web app took too long to answer.");
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            return JsonDocument.Parse(text.Length > 0 ? text : "{}");
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    private static string ErrorCode(JsonDocument doc)
    {
        var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
            ? doc.RootElement[0]
            : doc.RootElement;
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var e) &&
               e.ValueKind == JsonValueKind.Object && e.TryGetProperty("message", out var m)
            ? m.GetString() ?? ""
            : "";
    }

    private static void EnsureOk(HttpResponseMessage response, JsonDocument doc)
    {
        if (response.IsSuccessStatusCode)
            return;
        throw response.StatusCode switch
        {
            HttpStatusCode.Forbidden => new ApolloException("Your login isn't allowed to do that.", ApolloError.Denied),
            HttpStatusCode.Unauthorized => new ApolloException("Your sign-in has ended. Sign in again under Settings.", ApolloError.SignedOut),
            HttpStatusCode.Conflict => new ApolloException("That was already saved."),
            _ => new ApolloException($"The web app answered with an error ({(int)response.StatusCode} {ErrorCode(doc)})."),
        };
    }
}
