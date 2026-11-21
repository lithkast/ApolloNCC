using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace DanceLoop;

/// <summary>
/// /danceloop: starts the default /dance again whenever it ends. Stops when you move, use another emote or log out,
/// and waits while you're mounted, in combat, in a cutscene or busy. Nothing is saved; it's off every time the game starts.
/// </summary>
public sealed unsafe class Plugin : IDalamudPlugin
{
    private const string Command = "/danceloop";

    private static readonly TimeSpan MinGap = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromMilliseconds(250);

    private static readonly ConditionFlag[] Busy =
    [
        ConditionFlag.InCombat, ConditionFlag.Mounted, ConditionFlag.Casting, ConditionFlag.BetweenAreas,
        ConditionFlag.WatchingCutscene, ConditionFlag.Occupied, ConditionFlag.OccupiedInQuestEvent,
        ConditionFlag.Crafting, ConditionFlag.Gathering, ConditionFlag.Unconscious, ConditionFlag.Jumping,
    ];

    private bool enabled;
    private Vector3 anchor;
    private ushort danceId;
    private DateTimeOffset lastSent = DateTimeOffset.MinValue;
    private DateTimeOffset nextCheck = DateTimeOffset.MinValue;

    public Plugin()
    {
        Commands.AddHandler(Command, new CommandInfo((_, _) => this.Toggle())
        {
            HelpMessage = "Keep doing /dance until you move. Type it again to stop.",
        });
        Framework.Update += this.OnUpdate;
    }

    [PluginService] private static ICommandManager Commands { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IObjectTable Objects { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IChatGui Chat { get; set; } = null!;

    public void Dispose()
    {
        Framework.Update -= this.OnUpdate;
        Commands.RemoveHandler(Command);
    }

    private void Toggle()
    {
        if (this.enabled)
        {
            this.Stop(null);
            return;
        }

        if (Objects.LocalPlayer is not { } me)
            return;
        this.anchor = me.Position;
        this.lastSent = DateTimeOffset.MinValue;
        this.enabled = true;
        Chat.Print("Dance loop on. Move, use another emote or type /danceloop to stop.");
    }

    private void Stop(string? why)
    {
        this.enabled = false;
        Chat.Print(why == null ? "Dance loop off." : $"Dance loop off: {why}.");
    }

    private void OnUpdate(IFramework framework)
    {
        var now = DateTimeOffset.UtcNow;
        if (!this.enabled || now < this.nextCheck)
            return;
        this.nextCheck = now + CheckEvery;

        if (!ClientState.IsLoggedIn || Objects.LocalPlayer is not { } me)
        {
            this.enabled = false;
            return;
        }

        if (Vector3.Distance(me.Position, this.anchor) > 0.3f)
        {
            this.Stop("you moved");
            return;
        }

        var emote = ((Character*)me.Address)->EmoteController.EmoteId;

        // learn which emote id the game uses for /dance the first time it plays after we asked for it
        if (this.danceId == 0 && emote != 0 && now - this.lastSent < TimeSpan.FromSeconds(3))
            this.danceId = emote;
        if (emote != 0 && this.danceId != 0 && emote != this.danceId)
        {
            this.Stop("you used another emote");
            return;
        }

        foreach (var flag in Busy)
        {
            if (Condition[flag])
                return;
        }

        if (emote != 0 || now - this.lastSent < MinGap)
            return;

        this.lastSent = now;
        var ui = UIModule.Instance();
        if (ui == null)
            return;
        var text = Utf8String.FromString("/dance");
        try
        {
            ui->ProcessChatBoxEntry(text);
        }
        finally
        {
            text->Dtor(true);
        }
    }
}
