using System;
using System.Collections.Generic;
using System.Linq;
using ApolloNCC.Api;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;

namespace ApolloNCC.Services;

/// <summary>
/// Watches for things staff need to be told about straight away: a booking running out,
/// or a blacklisted player nearby. Runs on the framework thread.
/// </summary>
public sealed class Alerts
{
    private const ushort Gold = 500;
    private const ushort Red = 17;

    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan RepeatAfter = TimeSpan.FromMinutes(10);

    // A booking that ran out longer ago than this is old news (the rows stay on the page all night).
    private static readonly TimeSpan TimeUpWindow = TimeSpan.FromMinutes(5);

    private readonly Configuration config;
    private readonly ApolloState state;
    private readonly HashSet<string> timeUpAnnounced = [];
    private readonly HashSet<string> sightingsAnnounced = [];
    private readonly Dictionary<string, DateTimeOffset> lastSeen = new(StringComparer.OrdinalIgnoreCase);

    private DateTimeOffset nextScan = DateTimeOffset.MinValue;
    private IReadOnlyList<Ban>? indexedFrom;
    private ILookup<string, Ban> byPlayer = Array.Empty<Ban>().ToLookup(b => b.Player);
    private bool sightingsPrimed;

    public Alerts(Configuration config, ApolloState state)
    {
        this.config = config;
        this.state = state;
    }

    public void Tick(DateTimeOffset now)
    {
        var access = this.state.Access;
        if (access is not { IsStaff: true } || !Plugin.ClientState.IsLoggedIn)
        {
            this.sightingsPrimed = false;
            return;
        }

        if (this.config.BookingTimeUpAlerts)
            this.CheckBookings(now);

        if (this.config.BannedPatronAlerts && access.IsSecurity)
        {
            this.CheckSightings(access);
            if (now >= this.nextScan)
            {
                this.nextScan = now + ScanInterval;
                this.ScanNearby(now);
            }
        }
    }

    /// <summary>The blacklist entry for a player, if there is one. An entry without a world matches any world.</summary>
    public Ban? Match(string name, string? world)
    {
        var list = this.state.Bans;
        if (!ReferenceEquals(list, this.indexedFrom))
        {
            this.byPlayer = list.ToLookup(b => b.Player, StringComparer.OrdinalIgnoreCase);
            this.indexedFrom = list;
        }

        return this.byPlayer[name.Trim()].FirstOrDefault(b =>
            b.World.Length == 0 || string.IsNullOrEmpty(world) || string.Equals(b.World, world, StringComparison.OrdinalIgnoreCase));
    }

    private void CheckBookings(DateTimeOffset now)
    {
        foreach (var booking in this.state.MyBookings)
        {
            if (booking.EndsAt is not { } end || end > now || now - end > TimeUpWindow || !this.timeUpAnnounced.Add($"{booking.Id}@{end.ToUnixTimeSeconds()}"))
                continue;

            var who = booking.Patron.Length > 0 ? $" with {booking.Patron}" : "";
            var where = booking.Location.Length > 0 ? $" in {booking.Location}" : "";
            var text = $"Time is up{who}{where}.";
            Plugin.ToastGui.ShowQuest(text);
            Print(text, Gold);
        }
    }

    /// <summary>Banned players reported by other staff's plugins, so everyone on security hears about it.</summary>
    private void CheckSightings(Access access)
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
            if (this.sightingsAnnounced.Add(s.Id) && s.ByUid != access.Uid)
                Announce(s.Name, s.World, s.Ban, $"spotted by {s.By}");
        }
    }

    private void ScanNearby(DateTimeOffset now)
    {
        if (this.state.Bans.Count == 0)
            return;

        var self = Plugin.ObjectTable.LocalPlayer;
        foreach (var obj in Plugin.ObjectTable.PlayerObjects)
        {
            if (obj is not IPlayerCharacter pc || pc.Address == self?.Address)
                continue;

            var name = pc.Name.TextValue;
            var world = pc.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
            var ban = this.Match(name, world);
            if (ban == null)
                continue;

            var key = $"{name}@{world}";
            if (this.lastSeen.TryGetValue(key, out var seen) && now - seen < RepeatAfter)
                continue;

            this.lastSeen[key] = now;
            Announce(name, world, ban.Reason, "nearby");
            this.state.ReportSighting(ban, name, world);
        }
    }

    private static void Announce(string name, string world, string reason, string where)
    {
        var who = world.Length > 0 ? $"{name}@{world}" : name;
        Plugin.ToastGui.ShowError($"Banned patron: {who}");
        Print($"BANNED PATRON {who} ({where}).{(reason.Length > 0 ? $" Reason: {reason}" : "")}", Red);
    }

    private static void Print(string text, ushort colour) =>
        Plugin.ChatGui.Print(new SeStringBuilder()
            .AddUiForeground("[Apollo] ", Gold)
            .AddUiForeground(text, colour)
            .Build());
}
