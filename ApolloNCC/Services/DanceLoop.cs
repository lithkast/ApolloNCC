using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ApolloNCC.Services;

/// <summary>
/// Keeps the default /dance going: whenever the dance ends, it starts it again. It stops by itself as soon as you
/// move, do a different emote, or log out, so it never fights you; and it waits while you are mounted, in combat,
/// in a cutscene or busy with something else. Never saved: it is off every time the game starts.
/// </summary>
public sealed class DanceLoop
{
    private const string Command = "/dance";

    // never more often than this, whatever happens
    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(250);

    private static readonly ConditionFlag[] Busy =
    [
        ConditionFlag.InCombat, ConditionFlag.Mounted, ConditionFlag.Casting, ConditionFlag.BetweenAreas,
        ConditionFlag.WatchingCutscene, ConditionFlag.Occupied, ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.Crafting, ConditionFlag.Gathering, ConditionFlag.Unconscious, ConditionFlag.Jumping,
    ];

    private Vector3 anchor;
    private DateTimeOffset lastSent = DateTimeOffset.MinValue;
    private DateTimeOffset nextCheck = DateTimeOffset.MinValue;

    // the emote the game reports for /dance, learned the first time it plays
    private ushort danceId;

    public bool Enabled { get; private set; }

    /// <summary>Why it last switched itself off, or what it is doing, for the window.</summary>
    public string Status { get; private set; } = "Off.";

    public void Toggle()
    {
        if (this.Enabled)
            this.Stop("Off.");
        else
            this.Start();
    }

    public void Start()
    {
        if (Plugin.ObjectTable.LocalPlayer is not { } me)
        {
            this.Status = "Log in to a character first.";
            return;
        }

        this.anchor = me.Position;
        this.Enabled = true;
        this.lastSent = DateTimeOffset.MinValue;
        this.Status = "Dancing. Move or use another emote to stop.";
        Plugin.ChatGui.Print("[Apollo] Dance loop on. Move or use another emote to stop.");
    }

    public void Stop(string why)
    {
        if (this.Enabled && why != "Off.")
            Plugin.ChatGui.Print($"[Apollo] Dance loop off: {why.TrimEnd('.').ToLowerInvariant()}.");
        this.Enabled = false;
        this.Status = why;
    }

    /// <summary>Called every frame on the framework thread.</summary>
    public unsafe void Tick(DateTimeOffset now)
    {
        if (!this.Enabled || now < this.nextCheck)
            return;
        this.nextCheck = now + CheckEvery;

        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer is not { } me)
        {
            this.Stop("You logged out.");
            return;
        }

        // walking away is how you stop dancing in the game, so it stops the loop too
        if (Vector3.Distance(me.Position, this.anchor) > 0.3f)
        {
            this.Stop("You moved.");
            return;
        }

        var emote = ((Character*)me.Address)->EmoteController.EmoteId;

        // the game names the dance the first time it plays after we asked for it
        if (this.danceId == 0 && emote != 0 && now - this.lastSent < TimeSpan.FromSeconds(3))
            this.danceId = emote;

        if (emote != 0 && this.danceId != 0 && emote != this.danceId)
        {
            this.Stop("You used another emote.");
            return;
        }

        foreach (var flag in Busy)
        {
            if (Plugin.Condition[flag])
            {
                this.Status = "Waiting: you're busy (mounted, in combat, a cutscene...).";
                return;
            }
        }

        this.Status = "Dancing. Move or use another emote to stop.";
        if (emote != 0 || now - this.lastSent < MinGap)
            return;

        try
        {
            GameChat.SendLine(Command);
            this.lastSent = now;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            this.Stop($"The game would not take the command ({e.Message}).");
        }
    }
}
