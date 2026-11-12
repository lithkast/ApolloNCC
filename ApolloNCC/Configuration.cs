using System;
using ApolloNCC.Api;
using Dalamud.Configuration;

namespace ApolloNCC;

/// <summary>
/// Saved per game install, not per character, which is what lets every alt act as the same staff login.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    // The saved sign-in. The password is never kept: Firebase hands back a refresh token, which only works for
    // this login and stops working if the password changes or the login is switched off.
    public string Uid { get; set; } = "";

    public string Email { get; set; } = "";

    public string RefreshToken { get; set; } = "";

    public bool BannedPatronAlerts { get; set; } = true;

    public bool BookingTimeUpAlerts { get; set; } = true;

    /// <summary>What greeter mode tells each arrival. {name} becomes their first name.</summary>
    public string GreeterMessage { get; set; } = DefaultGreeterMessage;

    public const string DefaultGreeterMessage =
        "Welcome to Apollo Night Club & Casino, {name}! We have dancers, a full bar, a casino and private rooms. Ask at reception to book, and enjoy your night!";

    public Session? SavedSession =>
        this.Uid.Length > 0 && this.RefreshToken.Length > 0 ? new Session(this.Uid, this.Email, this.RefreshToken) : null;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
