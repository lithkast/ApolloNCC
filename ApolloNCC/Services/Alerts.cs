using System;
using System.Collections.Generic;
using ApolloNCC.Api;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;

namespace ApolloNCC.Services;

/// <summary>
/// Watches for things staff need to be told about straight away: a booking running out,
/// or a blacklisted patron nearby. Runs on the framework thread.
/// </summary>
public sealed class Alerts
{
    private const ushort Gold = 500;
    private const ushort Red = 17;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RepeatAfter = TimeSpan.FromMinutes(10);

    private readonly Configuration config;
    private readonly ApolloState state;
    private readonly HashSet<string> timeUpAnnounced = [];
    private readonly HashSet<string> sightingsAnnounced = [];
    private readonly Dictionary<string, DateTimeOffset> lastSeen = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset nextScan = DateTimeOffset.MinValue;
    private IReadOnlyList<BlacklistEntry>? indexedFrom;
    private Dictionary<string, BlacklistEntry> byNameAndWorld = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, BlacklistEntry> byNameOnly = new(StringComparer.OrdinalIgnoreCase);
    private bool sightingsPrimed;

    public Alerts(Configuration config, ApolloState state)
    {
        this.config = config;
        this.state = state;
    }

    public void Reset()
    {
        this.timeUpAnnounced.Clear();
        this.sightingsAnnounced.Clear();
        this.lastSeen.Clear();
        this.sightingsPrimed = false;
    }

    public void Tick(DateTimeOffset now)
    {
        var profile = this.state.Profile;
        if (profile == null || !Plugin.ClientState.IsLoggedIn)
            return;

        if (this.config.BookingTimeUpAlerts)
            this.CheckBookings(now);

        if (this.config.BannedPatronAlerts && profile.CanUseSecurityTools())
        {
            this.CheckSightings(profile);
            if (now >= this.nextScan)
            {
                this.nextScan = now + ScanInterval;
                this.ScanNearby(now);
            }
        }
    }

    public BlacklistEntry? Match(string name, string? world)
    {
        this.Reindex();
        if (world != null && this.byNameAndWorld.TryGetValue($"{name}@{world}", out var exact))
            return exact;
        return this.byNameOnly.GetValueOrDefault(name);
    }

    private void CheckBookings(DateTimeOffset now)
    {
        foreach (var booking in this.state.MyBookings)
        {
            if (booking.EndsAt > now || !this.timeUpAnnounced.Add(booking.Id))
                continue;

            var text = $"Time is up for {booking.ClientName} in {booking.Room}.";
            Plugin.ToastGui.ShowQuest(text);
            Print(text, Gold);
        }
    }

    /// <summary>Banned patrons reported by other staff, so everyone on security hears about it.</summary>
    private void CheckSightings(Profile profile)
    {
        var sightings = this.state.Sightings;

        // Whatever is already on the list when we sign in is old news.
        if (!this.sightingsPrimed)
        {
            foreach (var s in sightings)
                this.sightingsAnnounced.Add(s.Id);
            this.sightingsPrimed = true;
            return;
        }

        foreach (var s in sightings)
        {
            if (!this.sightingsAnnounced.Add(s.Id) ||
                string.Equals(s.ReportedBy, profile.Name, StringComparison.OrdinalIgnoreCase))
                continue;

            Announce(s.Name, s.World, s.Reason, $"spotted by {s.ReportedBy}");
        }
    }

    private void ScanNearby(DateTimeOffset now)
    {
        this.Reindex();
        if (this.byNameAndWorld.Count == 0 && this.byNameOnly.Count == 0)
            return;

        var self = Plugin.ObjectTable.LocalPlayer;
        foreach (var obj in Plugin.ObjectTable.PlayerObjects)
        {
            if (obj is not IPlayerCharacter pc || pc.Address == self?.Address)
                continue;

            var name = pc.Name.TextValue;
            var world = pc.HomeWorld.ValueNullable?.Name.ExtractText();
            var entry = this.Match(name, world);
            if (entry == null)
                continue;

            var key = $"{name}@{world}";
            if (this.lastSeen.TryGetValue(key, out var seen) && now - seen < RepeatAfter)
                continue;

            this.lastSeen[key] = now;
            Announce(name, world, entry.Reason, "nearby");
            this.state.ReportSighting(entry, name, world);
        }
    }

    private void Reindex()
    {
        var list = this.state.Blacklist;
        if (ReferenceEquals(list, this.indexedFrom))
            return;

        var exact = new Dictionary<string, BlacklistEntry>(StringComparer.OrdinalIgnoreCase);
        var nameOnly = new Dictionary<string, BlacklistEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in list)
        {
            if (string.IsNullOrWhiteSpace(entry.World))
                nameOnly[entry.Name.Trim()] = entry;
            else
                exact[$"{entry.Name.Trim()}@{entry.World.Trim()}"] = entry;
        }

        this.byNameAndWorld = exact;
        this.byNameOnly = nameOnly;
        this.indexedFrom = list;
    }

    private static void Announce(string name, string? world, string reason, string where)
    {
        var who = world is { Length: > 0 } ? $"{name}@{world}" : name;
        Plugin.ToastGui.ShowError($"Banned patron: {who}");
        Print($"BANNED PATRON {who} ({where}). Reason: {reason}", Red);
    }

    private static void Print(string text, ushort colour) =>
        Plugin.ChatGui.Print(new SeStringBuilder()
            .AddUiForeground("[Apollo] ", Gold)
            .AddUiForeground(text, colour)
            .Build());
}
