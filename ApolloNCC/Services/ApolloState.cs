using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ApolloNCC.Api;

namespace ApolloNCC.Services;

public enum View
{
    Hr,
    Reception,
    Security,
    Bar,
    Casino,
    Raffle,
    Other,
}

/// <summary>
/// The plugin's copy of what the web app knows, plus the actions that change it.
/// Network work runs in the background; the UI only ever reads the latest snapshot.
/// Lists are replaced whole, never edited in place, so reading them from the draw thread is safe.
/// </summary>
public sealed partial class ApolloState : IDisposable
{
    private readonly Configuration config;
    private readonly FirebaseClient client;
    private readonly CancellationTokenSource cts = new();
    private readonly Dictionary<string, DateTimeOffset> due = [];

    private int busy;
    private int refreshing;
    private int session;
    private string? myRowId;
    private string? publishedCharacter;
    private bool presenceRefused;

    public ApolloState(Configuration config)
    {
        this.config = config;
        this.client = new FirebaseClient(config.SavedSession);
    }

    public Access? Access { get; private set; }
    public VenueConfig Config { get; private set; } = new(null);
    public HrRow? MyRow { get; private set; }
    public bool CollabNight { get; private set; }
    public string Receptionist { get; private set; } = "";

    /// <summary>Whose plugin is greeting arrivals tonight (night/reception, greeterUid): one person at a time.</summary>
    public string GreeterUid { get; private set; } = "";
    public string GreeterName { get; private set; } = "";

    public string Uid => this.client.Session?.Uid ?? "";
    public IReadOnlyList<Booking> Bookings { get; private set; } = [];
    public IReadOnlyList<Doc> Buyouts { get; private set; } = [];
    public IReadOnlyList<HrRow> Roster { get; private set; } = [];
    public IReadOnlyList<ClockReview> Reviews { get; private set; } = [];
    public IReadOnlyList<Ban> Bans { get; private set; } = [];
    public IReadOnlyList<PatronWarning> Warnings { get; private set; } = [];
    public IReadOnlyList<Sighting> Sightings { get; private set; } = [];
    public IReadOnlyDictionary<string, Presence> Presence { get; private set; } = new Dictionary<string, Presence>();
    public IReadOnlyList<MenuItem> Menu { get; private set; } = [];
    public bool MenuLoaded { get; private set; }
    public IReadOnlyList<Buyout> BuyoutList { get; private set; } = [];
    public CasinoNight? Casino { get; private set; }
    public IReadOnlyList<RaffleEntry> RaffleEntries { get; private set; } = [];
    public RaffleNight Raffle { get; private set; } = new(null);
    public string? DiscordName { get; private set; }
    public (string Code, DateTimeOffset Expires)? DiscordCode { get; private set; }

    /// <summary>The web app's database rules don't know the plugin's collections yet (not deployed).</summary>
    public bool SecurityStoreMissing { get; private set; }

    /// <summary>Last thing that went wrong, worded for the user. Cleared by the next success.</summary>
    public string? Error { get; private set; }

    /// <summary>Last action that worked, worded for the user.</summary>
    public string? Notice { get; private set; }

    public bool Busy => this.busy > 0;

    public bool IsSignedIn => this.client.Session != null;

    public string Email => this.client.Session?.Email ?? "";

    /// <summary>The name on this login's roster row: the main character, whichever alt is logged in.</summary>
    public string MyName => this.MyRow?.Name is { Length: > 0 } n ? n : this.Access?.AccountName ?? "";

    public IEnumerable<Booking> MyBookings =>
        this.MyRow?.Name is { Length: > 0 } name
            ? this.Bookings.Where(b => string.Equals(b.Employee, name, StringComparison.OrdinalIgnoreCase))
            : [];

    public void Dispose()
    {
        this.cts.Cancel();
        this.cts.Dispose();
        this.client.Dispose();
    }

    public ShiftSummary? MySummary(DateTimeOffset now) =>
        this.MyRow is { } row
            ? Venue.WithExtras(Venue.Summarise(this.Config, row, this.CollabNight, now), this.Config, row.Name, this.Bookings, this.Receptionist, this.Buyouts, this.Casino)
            : null;

    /// <summary>Called every frame. Starts a background refresh of whatever is due.</summary>
    /// <param name="view">The open tab, or null when the window is closed.</param>
    /// <param name="character">"Name@World" of the logged-in character.</param>
    /// <param name="greeting">Greeter mode is on, which needs the roster and the blacklist to know who not to greet.</param>
    public void Tick(DateTimeOffset now, View? view, (string Name, string World)? character, bool bannedAlerts, bool greeting)
    {
        if (!this.IsSignedIn || Interlocked.Exchange(ref this.refreshing, 1) == 1)
            return;

        var jobs = new List<Func<int, CancellationToken, Task>>();
        var access = this.Access;
        if (this.Due("account", now, TimeSpan.FromMinutes(5)))
            jobs.Add(this.RefreshAccount);

        if (access is { IsStaff: true })
        {
            var open = view != null;
            var working = this.MyRow is { } r && (Venue.OnClock(r, now) || Venue.Promoting(r, now));
            if (this.Due("me", now, TimeSpan.FromSeconds(open || working ? 30 : 120)))
                jobs.Add((s, ct) => this.RefreshMe(s, open, ct));

            // the roster: HR's staff list, Reception's and the casino's pick lists, and who is tending bar for a buyout
            var wantsRoster = (view == View.Hr && access.SeesStaff) || (view == View.Reception && access.CanUseReception)
                              || (view == View.Bar && access.CanEditBar) || (view == View.Casino && access.ManagesCasino) || greeting;
            if (wantsRoster && this.Due("roster", now, TimeSpan.FromSeconds(view == View.Hr ? 30 : 90)))
                jobs.Add((s, ct) => this.RefreshRoster(s, view == View.Hr && access.IsOwner, ct));

            if (view == View.Reception && access.CanUseReception && this.Due("presence", now, TimeSpan.FromSeconds(60)))
                jobs.Add(this.RefreshPresence);

            // the menu carries a picture for each item, so it is fetched only for the Bar tab, and seldom
            if (view == View.Bar && this.Due("menu", now, TimeSpan.FromMinutes(10)))
                jobs.Add(this.RefreshMenu);
            if (view == View.Raffle && access.IsOwner && this.Due("raffle", now, TimeSpan.FromSeconds(15)))
                jobs.Add(this.RefreshRaffle);

            var watching = (access.IsSecurity && bannedAlerts) || greeting;
            if ((view == View.Security || watching) && this.Due("bans", now, view == View.Security ? TimeSpan.FromMinutes(1) : TimeSpan.FromMinutes(10)))
                jobs.Add(this.RefreshBans);
            if (view == View.Security && access.IsSecurity && this.Due("warnings", now, TimeSpan.FromSeconds(30)))
                jobs.Add(this.RefreshWarnings);
            if (access.IsSecurity && ((bannedAlerts && watching) || view == View.Security) && this.Due("sightings", now, TimeSpan.FromSeconds(30)))
                jobs.Add(this.RefreshSightings);

            if (character is { } c && !this.presenceRefused)
            {
                var key = $"{c.Name}@{c.World}";
                var changed = key != this.publishedCharacter;
                if (this.Due("presence-write", now, TimeSpan.FromMinutes(10)) || changed)
                {
                    this.publishedCharacter = key;
                    jobs.Add((_, ct) => this.PublishCharacter(c.Name, c.World, ct));
                }
            }
        }

        if (jobs.Count == 0)
        {
            this.refreshing = 0;
            return;
        }

        var started = this.session;
        Task.Run(async () =>
        {
            try
            {
                foreach (var job in jobs)
                {
                    if (started != this.session)
                        break;
                    await this.Guarded(() => job(started, this.cts.Token), quiet: true);
                }
            }
            finally
            {
                this.refreshing = 0;
            }
        });
    }

    /// <summary>Refresh the open tab on the next frame.</summary>
    public void RefreshSoon()
    {
        lock (this.due)
        {
            foreach (var key in this.due.Keys.Where(k => k is not ("account" or "presence-write")).ToList())
                this.due.Remove(key);
        }
    }

    public void SignIn(string email, string password) => this.Act(async ct =>
    {
        var s = await this.client.SignInAsync(email.Trim(), password, ct);
        this.config.Uid = s.Uid;
        this.config.Email = s.Email;
        this.config.RefreshToken = s.RefreshToken;
        this.config.Save();
        this.Forget();
        this.Notice = "Signed in.";
    });

    public void SignOut()
    {
        this.client.SignOut();
        this.config.Uid = this.config.RefreshToken = "";
        this.config.Save();
        this.Forget();
    }

    /// <summary>Clock in or out through the web app, exactly as Hermes' slash commands do.</summary>
    /// <param name="action">clockin, clockout, pfclockin or pfclockout.</param>
    /// <param name="kind">For pfclockin: pf, shout or both.</param>
    public void Clock(string action, string kind = "") => this.Act(async ct =>
    {
        var s = this.session;
        var result = await this.client.CallAsync("pluginClock", new JsonObject { ["action"] = action, ["kind"] = kind }, ct);
        var message = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("message", out var m) ? m.GetString() : null;
        await this.RefreshMe(s, true, ct);
        this.Notice = Plain(message ?? "Done.");
    });

    public void SetCollabNight(bool on) => this.Act(async ct =>
    {
        await this.client.SetAsync("night/collab", new Dictionary<string, object?>
        {
            ["on"] = on, ["by"] = this.MyName, ["at"] = NowMs(),
        }, ct);
        this.CollabNight = on;
        this.Notice = on
            ? $"Collab night is on: hourly pay x{this.Config.CollabRate:0.##}, and Reception takes no bookings."
            : "Collab night is off.";
    });

    public void MarkReviewed(ClockReview review) => this.Act(async ct =>
    {
        await this.client.DeleteAsync($"clockReviews/{review.Id}", ct);
        this.Reviews = this.Reviews.Where(r => r.Id != review.Id).ToList();
        this.Notice = $"Marked {review.Name}'s clock as reviewed.";
    });

    /// <summary>
    /// Takes or gives up the greeter's place. Only one person greets at a time, so a guest gets one welcome however
    /// many managers are in the venue; taking it while someone else has it takes over from them.
    /// </summary>
    public void SetGreeter(bool on) => this.Act(async ct =>
    {
        var uid = this.Uid;
        if (!on && this.GreeterUid != uid)
            return;
        await this.client.PatchAsync("night/reception", new Dictionary<string, object?>
        {
            ["greeterUid"] = on ? uid : "", ["greeter"] = on ? this.MyName : "", ["greeterAt"] = NowMs(),
        }, ct);
        this.GreeterUid = on ? uid : "";
        this.GreeterName = on ? this.MyName : "";
        this.Notice = on ? "Greeter mode is on: new arrivals get your welcome tell." : "Greeter mode is off.";
    });

    public void TakeReception() => this.Act(async ct =>
    {
        var name = this.MyName;
        await this.client.PatchAsync("night/reception", new Dictionary<string, object?> { ["receptionist"] = name }, ct);
        this.Receptionist = name;
        this.Notice = "You are tonight's receptionist: the reception bonus goes to you.";
    });

    public void CreateBooking(string employee, string patron, string rpType, double hours, int guests, bool vip, string location, Action onSuccess) =>
        this.Act(async ct =>
        {
            var s = this.session;
            var id = NewId();
            var now = NowMs();

            // the same row the site's Reception page makes: the clock starts when the duration goes in
            await this.client.CreateAsync("reception", id, new Dictionary<string, object?>
            {
                ["order"] = now, ["employee"] = employee, ["rpType"] = rpType, ["duration"] = hours,
                ["guests"] = (double)guests, ["vip"] = vip, ["location"] = location, ["timeIn"] = now,
            }, ct);

            // patron names are kept apart from the booking, for staff only
            if (patron.Length > 0)
                await this.client.CreateAsync("patrons", id, new Dictionary<string, object?> { ["name"] = patron, ["order"] = now }, ct);

            await this.RefreshMe(s, true, ct);
            this.Notice = employee.Length > 0 ? $"Booked {employee}{(patron.Length > 0 ? $" for {patron}" : "")}." : "Booking added.";
            onSuccess();
        });

    /// <summary>Change a booking that is already on the page, e.g. when the patron extends their time.</summary>
    public void UpdateBooking(Booking booking, double hours, int guests, bool vip, string location) => this.Act(async ct =>
    {
        var s = this.session;

        // the clock keeps running from when the booking started: only the length changes
        await this.client.PatchAsync($"reception/{booking.Id}", new Dictionary<string, object?>
        {
            ["duration"] = hours, ["guests"] = (double)guests, ["vip"] = vip, ["location"] = location,
        }, ct);
        await this.RefreshMe(s, true, ct);
        this.Notice = $"Booking updated{(booking.Patron.Length > 0 ? $" for {booking.Patron}" : "")}: {Venue.HoursText(hours)}.";
    });

    /// <summary>Take a booking off the Reception page, with its patron's name. It no longer counts towards tonight's total.</summary>
    public void RemoveBooking(Booking booking) => this.Act(async ct =>
    {
        var s = this.session;
        await this.client.DeleteAsync($"reception/{booking.Id}", ct);
        await this.client.DeleteAsync($"patrons/{booking.Id}", ct);
        await this.RefreshMe(s, true, ct);
        this.Notice = "Booking removed.";
    });

    public void RemoveWarning(PatronWarning warning) => this.Act(async ct =>
    {
        await this.client.DeleteAsync($"warnings/{warning.Id}", ct);
        this.Warnings = this.Warnings.Where(w => w.Id != warning.Id).ToList();
        this.Notice = $"Warning for {warning.Name} removed.";
    });

    /// <summary>Record a bar buyout at what it costs right now, as the site's Bar page does.</summary>
    public void RecordBuyout(string patron) => this.Act(async ct =>
    {
        var s = this.session;
        if (this.CollabNight)
            throw new ApolloException("There are no bar buyouts on a collab night.");
        var q = Venue.QuoteBuyout(this.Config, DateTimeOffset.UtcNow, this.Roster)
                ?? throw new ApolloException("Apollo is closed, so there is no buyout to record.");
        await this.client.CreateAsync("buyouts", NewId(), new Dictionary<string, object?>
        {
            ["order"] = NowMs(), ["patron"] = patron, ["hours"] = (double)q.Hours, ["amount"] = q.Amount, ["house"] = q.House,
            ["pool"] = q.Pool, ["bartenders"] = q.Bartenders.Cast<object?>().ToList(), ["each"] = q.Each, ["by"] = this.MyName,
        }, ct);
        await this.RefreshMe(s, true, ct);
        this.Notice = $"Buyout recorded{(patron.Length > 0 ? $" for {patron}" : "")}: collect {Venue.Gil(q.Amount)}.";
    });

    public void RemoveBuyout(Buyout buyout) => this.Act(async ct =>
    {
        var s = this.session;
        await this.client.DeleteAsync($"buyouts/{buyout.Id}", ct);
        await this.RefreshMe(s, true, ct);
        this.Notice = "Buyout removed.";
    });

    /// <summary>
    /// Puts a dealer on a casino shift, or changes their game or profit. Each dealer has one line per shift; the
    /// shift's gross profit and dealer list on the site's Casino page are then worked out from the lines.
    /// </summary>
    /// <param name="profit">Null leaves the profit as it is (or not entered yet).</param>
    public void SaveCasinoEntry(HrRow dealer, int shift, string game, double? profit, string done) => this.Act(async ct =>
    {
        var s = this.session;
        var key = CasinoKey(dealer, shift);
        var existing = this.Casino?.Entries.FirstOrDefault(e => e.Key == key);
        await this.client.PatchMapEntryAsync("night/casino", "entries", key, new Dictionary<string, object?>
        {
            ["name"] = dealer.Name, ["uid"] = dealer.Login, ["shift"] = (double)shift, ["game"] = game,
            ["profit"] = profit ?? existing?.Profit,
            ["at"] = existing != null ? (double)existing.At.ToUnixTimeMilliseconds() : NowMs(),
            ["by"] = this.MyName,
        }, ct);
        await this.SettleCasino(s, ct);
        this.Notice = done;
    });

    /// <summary>A manager taking a profit back off a line (entered by mistake), so the dealer can enter it again.</summary>
    public void ClearCasinoProfit(HrRow dealer, CasinoEntry entry, string game) => this.Act(async ct =>
    {
        var s = this.session;
        await this.client.PatchMapEntryAsync("night/casino", "entries", entry.Key, new Dictionary<string, object?>
        {
            ["name"] = dealer.Name, ["uid"] = dealer.Login, ["shift"] = (double)entry.Shift, ["game"] = game,
            ["profit"] = null, ["at"] = (double)entry.At.ToUnixTimeMilliseconds(), ["by"] = this.MyName,
        }, ct);
        await this.SettleCasino(s, ct);
        this.Notice = $"{entry.Name}'s profit cleared.";
    });

    public void RemoveCasinoEntry(CasinoEntry entry) => this.Act(async ct =>
    {
        var s = this.session;
        await this.client.PatchMapEntryAsync("night/casino", "entries", entry.Key, null, ct);
        await this.SettleCasino(s, ct, entry.Shift);
        this.Notice = $"{entry.Name} is off shift {entry.Shift}.";
    });

    public void SetRaffleStart(double amount) => this.Act(async ct =>
    {
        await this.RaffleNightPatch(new Dictionary<string, object?> { ["start"] = amount > 0 ? amount : null }, ct);
        await this.RefreshRaffle(this.session, ct);
        this.Notice = amount > 0 ? $"The pot starts at {Venue.Gil(amount)}." : "The pot starts empty.";
    });

    public void AddRaffleEntry(string patron, string world, string discord, int tickets, bool paid, Action onSuccess) => this.Act(async ct =>
    {
        var s = this.session;
        if (this.Raffle.Closed)
            throw new ApolloException("The raffle is closed. Reopen it to sell more tickets.");
        await this.client.CreateAsync("raffle", NewId(), new Dictionary<string, object?>
        {
            ["order"] = NowMs(), ["patron"] = patron, ["world"] = world, ["discord"] = discord,
            ["tickets"] = (double)tickets, ["takenBy"] = paid ? this.MyName : "", ["paid"] = paid,
        }, ct);
        await this.RefreshRaffle(s, ct);
        this.Notice = $"{patron}: {tickets} ticket{(tickets == 1 ? "" : "s")}, {Venue.Gil(tickets * this.Config.RaffleTicketPrice)}{(paid ? "" : " still to collect")}.";
        onSuccess();
    });

    public void SetRafflePaid(RaffleEntry entry, bool paid) => this.Act(async ct =>
    {
        await this.client.PatchAsync($"raffle/{entry.Id}", new Dictionary<string, object?>
        {
            ["paid"] = paid, ["takenBy"] = paid ? (entry.TakenBy.Length > 0 ? entry.TakenBy : this.MyName) : "",
        }, ct);
        await this.RefreshRaffle(this.session, ct);
    });

    public void RemoveRaffleEntry(RaffleEntry entry) => this.Act(async ct =>
    {
        await this.client.DeleteAsync($"raffle/{entry.Id}", ct);
        await this.RefreshRaffle(this.session, ct);
        this.Notice = $"{entry.Patron}'s tickets removed.";
    });

    public void SetRaffleClosed(bool closed) => this.Act(async ct =>
    {
        // reopening forgets a winner already drawn, so the raffle can be drawn again
        await this.RaffleNightPatch(closed
            ? new Dictionary<string, object?> { ["closed"] = true, ["closedAt"] = NowMs(), ["closedBy"] = this.MyName }
            : new Dictionary<string, object?> { ["closed"] = false, ["winner"] = null }, ct);
        await this.RefreshRaffle(this.session, ct);
        this.Notice = closed ? "The raffle is closed: no more tickets." : "The raffle is open again.";
    });

    /// <summary>Hermes draws the winner on the web app (every ticket is one chance) and announces it in Discord.</summary>
    public void PickRaffleWinner() => this.Act(async ct =>
    {
        var s = this.session;
        var result = await this.client.CallAsync("rafflePick", [], ct);
        await this.RefreshRaffle(s, ct);
        var announced = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("announced", out var a) && a.ValueKind == JsonValueKind.True;
        this.Notice = this.Raffle.Winner is { } w
            ? $"The winner is {w.Patron}: {Venue.Gil(w.Pot)}. {(announced ? "Hermes has announced it in Discord." : "Hermes could not announce it: type /rafflechannel in the Discord channel for winners.")}"
            : "The winner is drawn.";
    });

    /// <summary>Empties the raffle for a new night: every entry, the closed mark and the winner. The starting pot stays.</summary>
    public void ClearRaffle() => this.Act(async ct =>
    {
        var s = this.session;
        foreach (var e in this.RaffleEntries)
            await this.client.DeleteAsync($"raffle/{e.Id}", ct);
        if (this.Raffle.Closed || this.Raffle.Winner != null)
            await this.RaffleNightPatch(new Dictionary<string, object?> { ["closed"] = false, ["winner"] = null }, ct);
        await this.RefreshRaffle(s, ct);
        this.Notice = "The raffle is cleared.";
    });

    public void AddWarning(string name, string world, string reason, Action onSuccess) => this.Act(async ct =>
    {
        var now = NowMs();
        var fields = new Dictionary<string, object?>
        {
            ["name"] = name, ["world"] = world, ["reason"] = reason,
            ["by"] = this.MyName, ["byUid"] = this.client.Session!.Uid, ["at"] = now, ["order"] = now,
        };
        var id = NewId();
        try
        {
            await this.client.CreateAsync("warnings", id, fields, ct);
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            throw new ApolloException("The web app can't store warnings yet: its plugin update needs deploying first.");
        }

        this.Warnings = [new PatronWarning(new Doc(id, fields)), .. this.Warnings];
        this.Notice = $"Warning logged for {name}. Remember to post proof in the Discord security channel.";
        onSuccess();
    });

    /// <summary>Tell the rest of security that a blacklisted player is in the venue.</summary>
    public void ReportSighting(Ban ban, string name, string world)
    {
        var uid = this.client.Session?.Uid;
        if (uid == null || this.SecurityStoreMissing)
            return;
        _ = this.Guarded(
            () => this.client.CreateAsync("sightings", NewId(), new Dictionary<string, object?>
            {
                ["name"] = name, ["world"] = world, ["ban"] = Clip(ban.Reason.Length > 0 ? ban.Reason : ban.Notes, 300),
                ["by"] = this.MyName, ["byUid"] = uid, ["at"] = NowMs(),
            }, this.cts.Token),
            quiet: true);
    }

    public void StartDiscordLink() => this.Act(async ct =>
    {
        var result = await this.client.CallAsync("discordLinkCode", [], ct);
        if (result.TryGetProperty("linked", out var linked) && linked.ValueKind == JsonValueKind.True)
        {
            this.DueNow("account");
            this.Notice = "Your Discord is already linked.";
            return;
        }

        this.DiscordCode = (result.GetProperty("code").GetString()!, DateTimeOffset.FromUnixTimeMilliseconds(result.GetProperty("expires").GetInt64()));
    });

    public void UnlinkDiscord() => this.Act(async ct =>
    {
        await this.client.CallAsync("discordUnlink", [], ct);
        this.DiscordName = null;
        this.Notice = "Unlinked. Hermes no longer knows your Discord.";
    });

    private static double NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];

    // ids shaped like the site's own (newId in public/index.html)
    private static string NewId()
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var n = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var time = "";
        for (; n > 0; n /= 36)
            time = digits[(int)(n % 36)] + time;
        return "x" + time + string.Concat(Enumerable.Range(0, 5).Select(_ => digits[Random.Shared.Next(36)]));
    }

    /// <summary>Hermes words his replies for Discord: drop the markup and the sentences about slash commands.</summary>
    private static string Plain(string message) =>
        SlashSentence().Replace(message, "").Replace("**", "").Replace("`", "").Trim();

    [GeneratedRegex(@"\s*[^.!]*`/\w+`[^.!]*[.!]")]
    private static partial Regex SlashSentence();

    private bool Due(string key, DateTimeOffset now, TimeSpan every)
    {
        lock (this.due)
        {
            if (this.due.TryGetValue(key, out var at) && now < at)
                return false;
            this.due[key] = now + every;
            return true;
        }
    }

    private void DueNow(string key)
    {
        lock (this.due)
            this.due.Remove(key);
    }

    private void Forget()
    {
        this.session++;
        lock (this.due)
            this.due.Clear();
        this.myRowId = null;
        this.publishedCharacter = null;
        this.presenceRefused = false;
        this.Access = null;
        this.MyRow = null;
        this.CollabNight = false;
        this.Receptionist = "";
        this.GreeterUid = this.GreeterName = "";
        this.Bookings = [];
        this.Buyouts = [];
        this.BuyoutList = [];
        this.Menu = [];
        this.MenuLoaded = false;
        this.Casino = null;
        this.RaffleEntries = [];
        this.Raffle = new RaffleNight(null);
        this.Roster = [];
        this.Reviews = [];
        this.Bans = [];
        this.Warnings = [];
        this.Sightings = [];
        this.Presence = new Dictionary<string, Presence>();
        this.DiscordName = null;
        this.DiscordCode = null;
        this.SecurityStoreMissing = false;
        this.Error = null;
        this.Notice = null;
    }

    private async Task RefreshAccount(int s, CancellationToken ct)
    {
        var uid = this.client.Session!.Uid;
        var cfg = new VenueConfig(await this.client.GetAsync("config/main", ct));
        var account = await this.client.GetAsync($"accounts/{uid}", ct);
        var discord = await this.client.GetAsync($"discordLinks/{uid}", ct);
        if (s != this.session)
            return;

        this.Config = cfg;
        this.Access = new Access(uid, account, cfg);
        this.DiscordName = discord == null ? null : discord.Str("name") is { Length: > 0 } n ? n : discord.Str("username");
        if (discord != null)
            this.DiscordCode = null;
        this.Error = null;
    }

    private async Task RefreshMe(int s, bool withBuyouts, CancellationToken ct)
    {
        var uid = this.client.Session!.Uid;

        // this login's row on the roster: the one tied to it (the administrator's is picked in Settings)
        Doc? row = null;
        if (this.myRowId != null)
            row = await this.client.GetAsync($"hr/{this.myRowId}", ct);
        if (row == null || (row.Str("login") != uid && this.Config.AdminRoster != row.Id))
        {
            row = uid == Access.AdminUid && this.Config.AdminRoster.Length > 0
                ? await this.client.GetAsync($"hr/{this.Config.AdminRoster}", ct)
                : (await this.client.WhereAsync("hr", "login", uid, ct)).FirstOrDefault();
        }

        var collab = await this.client.GetAsync("night/collab", ct);
        var night = await this.client.GetAsync("night/reception", ct);
        var reception = await this.client.ListAsync("reception", ct);
        var patrons = (await this.client.ListAsync("patrons", ct)).ToDictionary(p => p.Id, p => p.Str("name"));
        var buyouts = withBuyouts ? await this.client.ListAsync("buyouts", ct) : null;
        var access = this.Access;
        var casino = access?.CanViewCasino == true ? new CasinoNight(await this.client.GetAsync("night/casino", ct)) : null;

        // owners keep an eye on the raffle so the plugin can yell a winner drawn from Discord
        var raffle = access?.IsOwner == true ? await this.GetRaffleNight(ct) : null;
        if (s != this.session)
            return;

        this.Casino = casino;
        this.Raffle = raffle ?? this.Raffle;

        this.myRowId = row?.Id;
        this.MyRow = row != null ? new HrRow(row) : null;
        this.CollabNight = collab?.Bool("on") == true;
        this.Receptionist = night?.Str("receptionist") ?? "";
        this.GreeterUid = night?.Str("greeterUid") ?? "";
        this.GreeterName = night?.Str("greeter") ?? "";
        this.Bookings = reception
            .OrderBy(r => r.Num("order") ?? 0)
            .Select(r => new Booking(r, patrons.GetValueOrDefault(r.Id) ?? "", Venue.ReceptionTotal(this.Config, r)))
            .ToList();
        this.Buyouts = buyouts ?? this.Buyouts;
        this.BuyoutList = this.Buyouts.Select(b => new Buyout(b)).Where(b => b.Amount > 0).OrderBy(b => b.At).ToList();
        this.Error = null;
    }

    private static string CasinoKey(HrRow dealer, int shift) =>
        $"s{shift}_{new string(dealer.Id.Where(char.IsAsciiLetterOrDigit).ToArray())}";

    /// <summary>
    /// After a dealer's line changes: bring each shift's gross profit and dealer list (what the site's Casino page
    /// and Hermes read) in step with the lines. A shift nobody has a line on is left as the site has it, unless its
    /// last line was just removed.
    /// </summary>
    private async Task SettleCasino(int s, CancellationToken ct, int? emptiedShift = null)
    {
        var night = new CasinoNight(await this.client.GetAsync("night/casino", ct));
        var fix = new Dictionary<string, object?>();
        for (var shift = 1; shift <= 2; shift++)
        {
            var lines = night.Entries.Where(e => e.Shift == shift).ToList();
            if (lines.Count == 0 && shift != emptiedShift)
                continue;

            var names = lines.Select(e => e.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            double? gross = lines.Any(e => e.Profit != null) ? lines.Sum(e => e.Profit ?? 0) : null;
            if (!names.SequenceEqual(night.Dealers[shift - 1]))
                fix[$"s{shift}Dealers"] = names.Cast<object?>().ToList();
            if (gross != night.Gross[shift - 1])
                fix[$"s{shift}Gross"] = gross;
        }

        if (fix.Count > 0)
        {
            await this.client.PatchAsync("night/casino", fix, ct);
            night = new CasinoNight(await this.client.GetAsync("night/casino", ct));
        }

        if (s == this.session)
            this.Casino = night;
    }

    private async Task RefreshMenu(int s, CancellationToken ct)
    {
        var items = await this.client.ListAsync("menu", ct);
        var macros = (await this.client.ListAsync("barmacros", ct)).ToDictionary(m => m.Id, m => m.Str("text"));
        if (s != this.session)
            return;
        this.Menu = items.Select(m => new MenuItem(m, macros.GetValueOrDefault(m.Id) ?? ""))
            .Where(m => m.Name.Length > 0).OrderBy(m => m.Order).ToList();
        this.MenuLoaded = true;
    }

    private async Task RefreshRaffle(int s, CancellationToken ct)
    {
        var rows = await this.client.ListAsync("raffle", ct);
        var night = await this.GetRaffleNight(ct);
        if (s != this.session)
            return;
        this.RaffleEntries = rows.Select(r => new RaffleEntry(r)).OrderBy(r => r.Order).ToList();
        this.Raffle = night;
    }

    private async Task RaffleNightPatch(Dictionary<string, object?> fields, CancellationToken ct)
    {
        try
        {
            await this.client.PatchAsync("night/raffle", fields, ct);
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            throw new ApolloException("The web app can't do this yet: its raffle update needs deploying first.");
        }
    }

    private async Task<RaffleNight> GetRaffleNight(CancellationToken ct)
    {
        try
        {
            return new RaffleNight(await this.client.GetAsync("night/raffle", ct));
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            // the web app's raffle update is not deployed yet
            return new RaffleNight(null);
        }
    }

    private async Task RefreshRoster(int s, bool withReviews, CancellationToken ct)
    {
        var rows = await this.client.ListAsync("hr", ct);
        var reviews = withReviews ? await this.client.ListAsync("clockReviews", ct) : null;
        if (s != this.session)
            return;

        // in the order of the HR page: by job as Settings lists them, then by name
        var jobOrder = this.Config.Jobs.Select((j, i) => (j.Name, i)).ToDictionary(x => x.Name, x => x.i);
        this.Roster = rows.Select(r => new HrRow(r)).Where(r => r.Name.Length > 0)
            .OrderBy(r => jobOrder.GetValueOrDefault(r.Job, int.MaxValue)).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (reviews != null)
            this.Reviews = reviews.Select(r => new ClockReview(r)).OrderByDescending(r => r.At).ToList();
    }

    private async Task RefreshPresence(int s, CancellationToken ct)
    {
        try
        {
            var docs = await this.client.ListAsync("presence", ct);
            if (s == this.session)
                this.Presence = docs.Select(d => new Presence(d)).ToDictionary(p => p.Uid);
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            // not deployed yet: tells fall back to finding the character nearby
        }
    }

    private async Task RefreshBans(int s, CancellationToken ct)
    {
        var docs = await this.client.ListAsync("bans", ct);
        if (s == this.session)
            this.Bans = docs.Select(d => new Ban(d)).Where(b => b.Name.Length > 0).ToList();
    }

    private async Task RefreshWarnings(int s, CancellationToken ct)
    {
        try
        {
            var docs = await this.client.ListAsync("warnings", ct, orderBy: "at desc", limit: 200);
            if (s != this.session)
                return;
            this.Warnings = docs.Select(d => new PatronWarning(d)).ToList();
            this.SecurityStoreMissing = false;
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            this.SecurityStoreMissing = true;
        }
    }

    private async Task RefreshSightings(int s, CancellationToken ct)
    {
        try
        {
            var docs = await this.client.ListAsync("sightings", ct, orderBy: "at desc", limit: 20);
            if (s == this.session)
                this.Sightings = docs.Select(d => new Sighting(d)).ToList();
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            this.SecurityStoreMissing = true;
        }
    }

    private async Task PublishCharacter(string name, string world, CancellationToken ct)
    {
        try
        {
            await this.client.SetAsync($"presence/{this.client.Session!.Uid}", new Dictionary<string, object?>
            {
                ["name"] = name, ["world"] = world, ["at"] = NowMs(),
            }, ct);
        }
        catch (ApolloException e) when (e.Kind == ApolloError.Denied)
        {
            this.presenceRefused = true;
        }
    }

    /// <summary>Run something the user asked for, showing a spinner and the outcome.</summary>
    private void Act(Func<CancellationToken, Task> work)
    {
        Interlocked.Increment(ref this.busy);
        this.Notice = null;
        Task.Run(async () =>
        {
            try
            {
                if (await this.Guarded(() => work(this.cts.Token), quiet: false))
                    this.Error = null;
            }
            finally
            {
                Interlocked.Decrement(ref this.busy);
            }
        });
    }

    private async Task<bool> Guarded(Func<Task> work, bool quiet)
    {
        try
        {
            await work();
            return true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApolloException e) when (e.Kind == ApolloError.SignedOut && this.IsSignedIn)
        {
            // the saved sign-in no longer works (password changed, login switched off): forget it
            this.SignOut();
            this.Error = e.Message;
        }
        catch (ApolloException e)
        {
            this.Error = e.Message;
        }
        catch (Exception e)
        {
            if (!quiet)
                this.Error = "Something went wrong inside the plugin. See /xllog for details.";
            Plugin.Log.Error(e, "Apollo background task failed");
        }

        return false;
    }
}
