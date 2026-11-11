using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace ApolloNCC.Windows.Tabs;

/// <summary>Name and world boxes with a button that fills them from the current target.</summary>
public static class TargetPicker
{
    public static void Draw(string id, ref string name, ref string world)
    {
        ImGui.PushID(id);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        ImGui.InputTextWithHint("##name", "Character name", ref name, 32);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120 * Theme.Scale);
        ImGui.InputTextWithHint("##world", "World", ref world, 32);
        ImGui.SameLine();

        var target = Plugin.TargetManager.Target as IPlayerCharacter;
        ImGui.BeginDisabled(target == null);
        if (ImGui.Button("From target") && target != null)
        {
            name = target.Name.TextValue;
            world = target.HomeWorld.ValueNullable?.Name.ExtractText() ?? "";
        }

        ImGui.EndDisabled();
        if (target == null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Target a player first.");
        ImGui.PopID();
    }
}
