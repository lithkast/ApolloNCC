using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ApolloNCC.Services;

/// <summary>
/// Greeter mode: while it is on, each player who walks into the venue gets one welcome tell.
/// It is deliberately cautious, because it sends tells without a click each time:
///   - only owners, co-owners and Front of House's managers can switch it on, and only while on the clock
///   - only one person greets at a time (see <see cref="ApolloState.SetGreeter"/>)
///   - only inside the venue's own house, and only players who arrive after it was switched on
///   - never staff, never anyone on the blacklist, and nobody twice in a night
///   - a few seconds between tells
/// It switches itself off when the plugin loads, on clocking out, and when someone else takes over.
/// </summary>
public sealed class Greeter
{
    private static readonly TimeSpan ScanEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan BetweenTells = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan Night = TimeSpan.FromHours(16);

    private readonly Configuration config;
    private readonly ApolloState state;
    private readonly Alerts alerts;
    private readonly HashSet<string> present = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> greeted = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<(string Name, string World)> waiting = new();

    private bool primed;
    private uint territory;
    private DateTimeOffset nextScan = DateTimeOffset.MinValue;
    private DateTimeOffset nextTell = DateTimeOffset.MinValue;

    public Greeter(Configuration config, ApolloState state, Alerts alerts)
    {
        this.config = config;
        this.state = state;
        this.alerts = alerts;
    }

    /// <summary>Switched on in Settings. Never saved: every session starts with it off.</summary>
    public bool Enabled { get; private set; }

    /// <summary>What it is doing right now, for Settings.</summary>
    public string Status { get; private set; } = "Off.";

    public int GreetedTonight => this.greeted.Count;

    /// <summary>Why greeter mode can't be switched on right now, or null if it can.</summary>
    public string? Blocked(DateTimeOffset now)
    {
        if (this.state.Access?.CanGreet != true)
            return "Greeter mode is for owners, co-owners and Front of House's managers.";
        if (this.state.MyRow is not { } row || !Venue.OnClock(row, now))
            return "Clock in first: greeter mode only runs while you're on the clock.";
        return null;
    }

    public void TurnOn(DateTimeOffset now)
    {
        if (this.Blocked(now) != null)
            return;
        this.Enabled = true;
        this.primed = false;
        this.waiting.Clear();
        this.state.SetGreeter(true);
    }

    public void TurnOff()
    {
        if (!this.Enabled)
            return;
        this.Enabled = false;
        this.waiting.Clear();
        this.Status = "Off.";
        this.state.SetGreeter(false);
    }

    /// <summary>The welcome for one player, with their first name filled in.</summary>
    public string MessageFor(string fullName)
    {
        var first = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? fullName;
        var text = string.IsNullOrWhiteSpace(this.config.GreeterMessage) ? Configuration.DefaultGreeterMessage : this.config.GreeterMessage;
        return text.Replace("{name}", first).ReplaceLineEndings(" ").Trim();
    }

    /// <summary>Called every frame on the framework thread.</summary>
    public void Tick(DateTimeOffset now)
    {
        if (!this.Enabled)
            return;

        if (this.Blocked(now) is { } why)
        {
            // clocked out, or the role changed: stop, and give up the greeter's place
            this.TurnOff();
            this.Status = $"Switched off. {why}";
            return;
        }

        if (this.state.GreeterUid.Length > 0 && this.state.GreeterUid != this.state.Uid && !this.state.Busy)
        {
            this.Enabled = false;
            this.waiting.Clear();
            this.Status = $"Switched off: {(this.state.GreeterName.Length > 0 ? this.state.GreeterName : "someone else")} took over greeting.";
            return;
        }

        if (Plugin.ClientState.TerritoryType != this.territory)
        {
            // walked through a door: whoever is already there is not an arrival
            this.territory = Plugin.ClientState.TerritoryType;
            this.primed = false;
            this.waiting.Clear();
        }

        if (!InVenue(this.state.Config))
        {
            this.primed = false;
            this.Status = "Waiting: you're not inside the venue.";
            return;
        }

        if (this.state.Roster.Count == 0)
        {
            // without the staff list there is no telling staff from guests yet
            this.Status = "Getting the staff list...";
            return;
        }

        if (now >= this.nextScan)
        {
            this.nextScan = now + ScanEvery;
            this.Scan(now);
        }

        this.SendNext(now);
        this.Status = this.waiting.Count > 0 ? $"Greeting {this.waiting.Count} arrival{(this.waiting.Count == 1 ? "" : "s")}..." : "On: watching the door.";
    }

    private void Scan(DateTimeOffset now)
    {
        foreach (var old in this.greeted.Where(g => now - g.Value > Night).Select(g => g.Key).ToList())
            this.greeted.Remove(old);

        var self = Plugin.ObjectTable.LocalPlayer;
        var here = new List<(string Name, string World)>();
        foreach (var obj in Plugin.ObjectTable.PlayerObjects)
        {
            if (obj is IPlayerCharacter pc && pc.Address != self?.Address &&
                pc.HomeWorld.ValueNullable?.Name.ExtractText() is { Length: > 0 } world)
                here.Add((pc.Name.TextValue, world));
        }

        if (!this.primed)
        {
            // the first look after switching on or walking in: everyone already inside is left alone
            this.present.Clear();
            foreach (var p in here)
                this.present.Add($"{p.Name}@{p.World}");
            this.primed = true;
            return;
        }

        var staff = this.state.Roster.Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in here)
        {
            var key = $"{p.Name}@{p.World}";
            if (!this.present.Add(key) || this.greeted.ContainsKey(key))
                continue;
            if (staff.Contains(p.Name) || this.alerts.Match(p.Name, p.World) != null)
                continue;

            this.greeted[key] = now;
            this.waiting.Enqueue(p);
        }

        // someone who leaves and comes back counts as present again, but is never greeted twice
        this.present.IntersectWith(here.Select(p => $"{p.Name}@{p.World}"));
    }

    private void SendNext(DateTimeOffset now)
    {
        if (this.waiting.Count == 0 || now < this.nextTell)
            return;

        var (name, world) = this.waiting.Dequeue();
        this.nextTell = now + BetweenTells;
        try
        {
            GameChat.SendTell(name, world, this.MessageFor(name));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Plugin.Log.Warning(e, "Greeter could not send a tell");
        }
    }

    /// <summary>Standing inside the venue's own house: an estate's interior, in the ward and plot Settings names.</summary>
    private static unsafe bool InVenue(Api.VenueConfig cfg)
    {
        var housing = HousingManager.Instance();
        if (housing == null || !housing->IsInside())
            return false;

        var house = housing->GetCurrentIndoorHouseId();
        if (house.IsApartment)
            return false;

        // the game counts wards and plots from 0; the site's Settings count from 1
        return (cfg.Ward is not { } ward || house.WardIndex + 1 == ward) &&
               (cfg.Plot is not { } plot || house.PlotIndex + 1 == plot);
    }
}
