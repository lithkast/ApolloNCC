using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures.TextureWraps;

namespace ApolloNCC.Windows.Tabs;

public sealed class BarTab(ApolloState state, MacroRunner macros) : IDisposable
{
    private readonly Dictionary<string, (string Photo, Task<IDalamudTextureWrap?> Texture)> photos = [];
    private readonly Confirm confirm = new();

    private string search = "";
    private string selectedId = "";
    private bool buyoutOpen;
    private string buyoutPatron = "";
    private string buyoutWorld = "";

    public void Dispose()
    {
        foreach (var (_, texture) in this.photos.Values)
        {
            if (texture.IsCompletedSuccessfully)
                texture.Result?.Dispose();
        }

        this.photos.Clear();
    }

    public void Draw(Access access)
    {
        var now = DateTimeOffset.UtcNow;
        this.DrawBuyouts(access, now);

        Theme.Section("Menu");
        if (!state.MenuLoaded)
        {
            Theme.Muted("Fetching the menu...");
            return;
        }

        if (state.Menu.Count == 0)
        {
            Theme.Muted("The menu is empty. The bar manager adds drinks and food on the site.");
            return;
        }

        ImGui.SetNextItemWidth(260 * Theme.Scale);
        ImGui.InputTextWithHint("##search", "Search the menu or an item", ref this.search, 64);

        var listWidth = 250 * Theme.Scale;
        if (ImGui.BeginChild("menu-list", new Vector2(listWidth, 0), true))
            this.DrawList();
        ImGui.EndChild();

        ImGui.SameLine();
        if (ImGui.BeginChild("menu-card", Vector2.Zero, true))
        {
            var item = state.Menu.FirstOrDefault(m => m.Id == this.selectedId);
            if (item == null)
                Theme.Muted("Pick something from the menu to see how to serve it.");
            else
                this.DrawCard(item);
        }

        ImGui.EndChild();
    }

    private void DrawList()
    {
        var needle = this.search.Trim();
        bool Hit(MenuItem m) => needle.Length == 0 ||
                                m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                                m.Item.Contains(needle, StringComparison.OrdinalIgnoreCase);

        // the sections in the order Settings lists them, then any other type still on an item
        var sections = state.Config.BarSections.Concat(state.Menu.Select(m => m.Section)).Distinct().ToList();
        var any = false;
        foreach (var section in sections)
        {
            var items = state.Menu.Where(m => m.Section == section && Hit(m)).ToList();
            if (items.Count == 0)
                continue;

            any = true;
            ImGui.TextColored(Theme.Gold, section.ToUpperInvariant());
            foreach (var m in items)
            {
                if (ImGui.Selectable($"{m.Name}##{m.Id}", m.Id == this.selectedId))
                    this.selectedId = m.Id;
            }

            ImGui.Spacing();
        }

        if (!any)
            Theme.Muted("Nothing on the menu matches.");
    }

    private void DrawCard(MenuItem m)
    {
        var scale = Theme.Scale;
        var picture = this.Picture(m);
        if (picture != null)
        {
            // fit the picture in a box, keeping its shape
            var box = 180 * scale;
            var fit = Math.Min(box / picture.Width, box / picture.Height);
            ImGui.Image(picture.Handle, new Vector2(picture.Width * fit, picture.Height * fit));
            ImGui.SameLine();
        }

        ImGui.BeginGroup();
        ImGui.SetWindowFontScale(1.3f);
        ImGui.TextColored(Theme.Gold, m.Name);
        ImGui.SetWindowFontScale(1f);
        Theme.Muted(m.Section);
        ImGui.Spacing();
        Theme.Stat("Price", m.Cost is { } cost ? (cost > 0 ? Venue.Gil(cost) : "Free") : "Not set");
        Theme.EndStats();

        Theme.Muted("In-game item");
        if (m.Item.Length > 0)
        {
            ImGui.TextUnformatted(m.Item);
            ImGui.SameLine();
            if (ImGui.SmallButton("Copy"))
                ImGui.SetClipboardText(m.Item);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Copies the item name, to paste into the game's item search.");
        }
        else
        {
            Theme.Muted("Not set yet");
        }

        ImGui.EndGroup();

        if (m.Description.Length > 0)
        {
            Theme.Section("Description");
            ImGui.TextWrapped(m.Description);
        }

        Theme.Section("Serve it");
        ImGui.BeginDisabled(macros.Running);
        if (ImGui.Button("Say it"))
            macros.Play([$"/s One {m.Name}, coming right up!"]);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"Says in /say: \"One {m.Name}, coming right up!\"");
        ImGui.SameLine();
        ImGui.BeginDisabled(m.Macro.Count == 0);
        if (ImGui.Button("Make it"))
            macros.Play(m.Macro);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(m.Macro.Count > 0 ? "Plays the preparation macro below in chat, line by line." : "No preparation macro yet. The bar manager adds it on the site.");
        ImGui.EndDisabled();
        if (macros.Running)
        {
            ImGui.SameLine();
            if (ImGui.Button("Stop"))
                macros.Stop();
        }

        if (macros.Error != null)
            ImGui.TextColored(Theme.Bad, $"The macro stopped: {macros.Error}");

        if (m.Macro.Count > 0)
        {
            ImGui.Spacing();
            Theme.Muted($"Preparation macro, {m.Macro.Count} line{(m.Macro.Count == 1 ? "" : "s")}");
            foreach (var line in m.Macro)
                Theme.Wrapped(Theme.TextMuted, line);
            if (ImGui.SmallButton("Copy macro"))
                ImGui.SetClipboardText(string.Join("\r\n", m.Macro));
        }
    }

    /// <summary>The item's picture as a texture, decoded once and kept until the picture changes.</summary>
    private IDalamudTextureWrap? Picture(MenuItem m)
    {
        if (m.Photo.Length == 0)
            return null;

        if (!this.photos.TryGetValue(m.Id, out var cached) || cached.Photo != m.Photo)
        {
            if (cached.Texture is { IsCompletedSuccessfully: true })
                cached.Texture.Result?.Dispose();
            cached = (m.Photo, LoadPicture(m.Photo));
            this.photos[m.Id] = cached;
        }

        return cached.Texture.IsCompletedSuccessfully ? cached.Texture.Result : null;
    }

    private static async Task<IDalamudTextureWrap?> LoadPicture(string dataAddress)
    {
        try
        {
            var comma = dataAddress.IndexOf(',');
            var bytes = Convert.FromBase64String(dataAddress[(comma + 1)..]);
            return await Plugin.TextureProvider.CreateFromImageAsync(bytes);
        }
        catch (Exception e)
        {
            // a picture type the game can't draw: the card simply shows without it
            Plugin.Log.Debug(e, "Could not load a menu picture");
            return null;
        }
    }

    private void DrawBuyouts(Access access, DateTimeOffset now)
    {
        var buyouts = state.BuyoutList;
        if (!access.CanEditBar && buyouts.Count == 0)
            return;

        Theme.Section("Bar buyouts");
        if (access.CanEditBar)
            this.DrawBuyoutPanel(now);

        if (buyouts.Count == 0)
            return;

        ImGui.TextColored(Theme.Gold, $"The bar is bought out tonight{(buyouts[^1].Patron.Length > 0 ? $" by {buyouts[^1].Patron}" : "")}: drinks are on the house.");
        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("buyouts", access.CanEditBar ? 5 : 4, flags))
            return;

        ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.None, 1f);
        ImGui.TableSetupColumn("Bought by", ImGuiTableColumnFlags.None, 2f);
        ImGui.TableSetupColumn("Collected", ImGuiTableColumnFlags.None, 1.4f);
        ImGui.TableSetupColumn("Bartenders' share", ImGuiTableColumnFlags.None, 3.2f);
        if (access.CanEditBar)
            ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableHeadersRow();
        foreach (var b in buyouts)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Venue.FormatSeconds(Venue.VenueSeconds(state.Config, b.At)));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(b.Patron.Length > 0 ? b.Patron : "-");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Venue.Gil(b.Amount));
            ImGui.TableNextColumn();
            ImGui.TextWrapped(b.Bartenders.Count == 0
                ? $"{Venue.Gil(b.Pool)}, nobody clocked in to share it"
                : $"{string.Join(", ", b.Bartenders)}{(b.Each is { } each ? $": {Venue.Gil(each)} each" : "")}");
            if (access.CanEditBar)
            {
                ImGui.TableNextColumn();
                ImGui.BeginDisabled(state.Busy);
                if (this.confirm.Button(b.Id, "Remove"))
                    state.RemoveBuyout(b);
                ImGui.EndDisabled();
            }
        }

        ImGui.EndTable();
    }

    private void DrawBuyoutPanel(DateTimeOffset now)
    {
        if (state.CollabNight)
        {
            Theme.Muted("No bar buyouts on a collab night.");
            return;
        }

        var quote = Venue.QuoteBuyout(state.Config, now, state.Roster);
        if (quote == null)
        {
            this.buyoutOpen = false;
            Theme.Muted($"Bar buyouts open on Sundays, {Clock(state.Config.StartTime)} to {Clock(state.Config.CloseTime)}.");
            return;
        }

        if (ImGui.Button(this.buyoutOpen ? "Hide the bar buyout" : "Bar buyout"))
            this.buyoutOpen = !this.buyoutOpen;
        if (!this.buyoutOpen)
            return;

        ImGui.Spacing();
        var minutes = (int)Math.Ceiling(quote.SecondsLeft / 60);
        Theme.Stat("Time left until close", minutes >= 60 ? $"{minutes / 60} h {minutes % 60} min" : $"{minutes} min");
        Theme.Stat("Charged as", $"{quote.Hours} hour{(quote.Hours == 1 ? "" : "s")}");
        Theme.Stat("Gil to collect", Venue.Gil(quote.Amount), Theme.Gold);
        Theme.Stat("The venue takes", Venue.Gil(quote.House));
        Theme.Stat("Bartenders share", Venue.Gil(quote.Pool));
        Theme.EndStats();
        Theme.Wrapped(Theme.TextMuted, quote.Bartenders.Count > 0
            ? $"Split between the bartenders clocked in on HR: {string.Join(", ", quote.Bartenders)}{(quote.Each is { } each && quote.Bartenders.Count > 1 ? $", {Venue.Gil(each)} each" : "")}."
            : "No bartenders are clocked in on HR right now, so the bartenders' share is not split yet.");

        ImGui.AlignTextToFramePadding();
        Theme.Muted("Who is buying it out");
        ImGui.SameLine();
        TargetPicker.Draw("buyout", ref this.buyoutPatron, ref this.buyoutWorld);
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.Button("Record this buyout"))
        {
            var name = this.buyoutPatron.Trim();
            var world = this.buyoutWorld.Trim();
            state.RecordBuyout(name.Length > 0 && world.Length > 0 ? $"{name}@{world}" : name);
            this.buyoutPatron = this.buyoutWorld = "";
            this.buyoutOpen = false;
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        Theme.Muted($"Collect {Venue.Gil(quote.Amount)} from them first.");
    }

    private static string Clock(string hhmm) =>
        TimeSpan.TryParse(hhmm, out var t) ? Venue.FormatSeconds(t.TotalSeconds) : hhmm;
}
