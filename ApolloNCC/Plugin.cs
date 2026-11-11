using System;
using ApolloNCC.Services;
using ApolloNCC.Windows;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace ApolloNCC;

public sealed class Plugin : IDalamudPlugin
{
    private const string Command = "/apollo";

    private readonly WindowSystem windows = new("ApolloNCC");
    private readonly Configuration config;
    private readonly ApolloState state;
    private readonly Alerts alerts;
    private readonly MainWindow mainWindow;

    private string? character;

    public Plugin()
    {
        this.config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        this.state = new ApolloState(this.config, () => this.character);
        this.alerts = new Alerts(this.config, this.state);
        this.mainWindow = new MainWindow(this.config, this.state, this.alerts);
        this.windows.AddWindow(this.mainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "Open the Apollo staff window. \"/apollo settings\" opens its settings.",
        });

        PluginInterface.UiBuilder.Draw += this.windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenSettings;
        Framework.Update += this.OnUpdate;
    }

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IToastGui ToastGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public void Dispose()
    {
        Framework.Update -= this.OnUpdate;
        PluginInterface.UiBuilder.Draw -= this.windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenMain;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenSettings;
        CommandManager.RemoveHandler(Command);
        this.windows.RemoveAllWindows();
        this.state.Dispose();
    }

    private void OnUpdate(IFramework framework)
    {
        // Which character is logged in. The web app maps every alt back to the main employee through the plugin key.
        this.character = PlayerState.IsLoaded
            ? $"{PlayerState.CharacterName}@{PlayerState.HomeWorld.ValueNullable?.Name.ExtractText()}"
            : null;

        if (!ClientState.IsLoggedIn)
            return;

        var now = DateTimeOffset.UtcNow;
        this.state.Tick(now, this.mainWindow.IsOpen ? this.mainWindow.CurrentView : null);
        this.alerts.Tick(now);
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("settings", StringComparison.OrdinalIgnoreCase))
            this.OpenSettings();
        else
            this.mainWindow.Toggle();
    }

    private void OpenMain() => this.mainWindow.IsOpen = true;

    private void OpenSettings()
    {
        this.mainWindow.ShowSettings();
        this.mainWindow.IsOpen = true;
    }
}
