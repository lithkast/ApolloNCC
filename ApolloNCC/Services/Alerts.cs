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

    private readonly Dictionary<string, int> casinoReminded = [];
    private double raffleYelledAt;

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
        this.CheckCasino(access, now);
        this.CheckRaffleWinner(access, now);

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

    /// <summary>Every two hours on a casino shift, remind a dealer who has not entered their game's profit yet.</summary>
    private void CheckCasino(Access access, DateTimeOffset now)
    {
        if (this.state.Casino is not { } casino)
            return;

        foreach (var e in casino.Entries)
        {
            if (e.Uid != access.Uid || e.Profit != null)
                continue;

            var periods = (int)((now - e.At).TotalHours / 2);
            if (periods < 1 || periods > 4 || this.casinoReminded.GetValueOrDefault(e.Key) >= periods)
                continue;

            this.casinoReminded[e.Key] = periods;
            var text = $"Casino shift {e.Shift}: time to enter your profit{(e.Game.Length > 0 ? $" for {e.Game}" : "")}.";
            Plugin.ToastGui.ShowQuest(text);
            Print(text, Gold);
        }
    }

    /// <summary>
    /// When this login drew the raffle (from the plugin, the site or Hermes), yell the winner in game. Only the
    /// person who drew it yells, so the venue hears it once however many owners have the plugin open.
    /// </summary>
    private void CheckRaffleWinner(Access access, DateTimeOffset now)
    {
        if (this.state.Raffle.Winner is not { } w || w.ByUid != access.Uid || w.At <= this.raffleYelledAt)
            return;

        this.raffleYelledAt = w.At;

        // a winner drawn a while ago (the plugin was just opened) is old news
        if (now.ToUnixTimeMilliseconds() - w.At > 120_000)
            return;
        try
        {
            GameChat.SendLine(RaffleYell(w));
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            Plugin.Log.Warning(e, "Could not yell the raffle winner");
        }
    }

    public static string RaffleYell(RaffleWinner w) =>
        $"/y The 50/50 raffle winner is {w.Patron}! They take home {Venue.Gil(w.Pot)}. Congratulations!";

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
