using System;
using ApolloNCC.Api;
using Dalamud.Configuration;

namespace ApolloNCC;

/// <summary>
/// Saved per game install, not per character, which is what lets every alt act as the main employee.
/// </summary>
[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public const string DefaultBaseUrl = "https://apolloncc.org";

    public int Version { get; set; } = 1;

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    /// <summary>The employee's plugin key from the web app. Kept in Dalamud's plugin config file on this PC.</summary>
    public string SecretKey { get; set; } = "";

    public bool BannedPatronAlerts { get; set; } = true;

    public bool BookingTimeUpAlerts { get; set; } = true;

    /// <summary>Use made-up offline data instead of the web app.</summary>
    public bool DemoMode { get; set; }

    public Rank DemoRank { get; set; } = Rank.Owner;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
