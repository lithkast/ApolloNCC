using System;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows;

/// <summary>A button that only acts on a second click within a few seconds, for things that can't be undone.</summary>
public sealed class Confirm
{
    private string armed = "";
    private DateTimeOffset until;

    /// <returns>True on the confirming click.</returns>
    public bool Button(string id, string label, bool small = true)
    {
        var now = DateTimeOffset.UtcNow;
        var confirming = this.armed == id && now < this.until;
        if (confirming)
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Bad with { W = 0.45f });
        var text = $"{(confirming ? "Click again" : label)}##{id}";
        var clicked = small ? ImGui.SmallButton(text) : ImGui.Button(text);
        if (confirming)
            ImGui.PopStyleColor();
        if (!clicked)
            return false;
        if (confirming)
        {
            this.armed = "";
            return true;
        }

        this.armed = id;
        this.until = now.AddSeconds(4);
        return false;
    }
}
