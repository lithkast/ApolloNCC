using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class CasinoTab(ApolloState state)
{
    private readonly Confirm confirm = new();

    // the dealer's own shift
    private int myShift = 1;
    private string myGame = "";
    private int myProfit;

    // a manager putting someone on a shift
    private string assignId = "";
    private int assignShift = 1;
    private string assignGame = "";

    // a manager's Edit popup
    private string editGame = "";
    private int editProfit;
    private bool editHasProfit;

    public void Draw(Access access)
    {
        var casino = state.Casino;
        if (casino == null)
        {
            Theme.Muted("Loading the casino sheet...");
            return;
        }

        if (access.CanEditCasino && state.MyRow is { } me)
            this.DrawMyShift(access, casino, me);
        if (access.ManagesCasino)
            this.DrawAssign(casino);

        for (var shift = 1; shift <= 2; shift++)
            this.DrawShift(access, casino, shift);
        DrawNight(casino, state.Config);
    }

    private void DrawMyShift(Access access, CasinoNight casino, HrRow me)
    {
        Theme.Section("Your shift");
        ImGui.AlignTextToFramePadding();
        Theme.Muted("Shift");
        ImGui.SameLine();
        ImGui.RadioButton("1##mine", ref this.myShift, 1);
        ImGui.SameLine();
        ImGui.RadioButton("2##mine", ref this.myShift, 2);

        var entry = casino.Entries.FirstOrDefault(e => e.Shift == this.myShift && string.Equals(e.Name, me.Name, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            ImGui.SameLine(0, 20 * Theme.Scale);
            this.GameBox("##mygame", ref this.myGame, casino);
            ImGui.SameLine();
            ImGui.BeginDisabled(this.myGame.Trim().Length == 0 || state.Busy);
            if (ImGui.Button("Go on shift"))
                state.SaveCasinoEntry(me, this.myShift, this.myGame.Trim(), null, $"You're dealing {this.myGame.Trim()} on shift {this.myShift}.");
            ImGui.EndDisabled();
            if (this.myGame.Trim().Length == 0)
            {
                ImGui.SameLine();
                Theme.Muted("Enter the game you're dealing.");
            }

            return;
        }

        // on the shift: the game can be corrected; the profit is entered once
        ImGui.SameLine(0, 20 * Theme.Scale);
        ImGui.TextColored(Theme.Good, $"Dealing {entry.Game} since {Venue.FormatSeconds(Venue.VenueSeconds(state.Config, entry.At))}");

        ImGui.AlignTextToFramePadding();
        Theme.Muted("Wrong game?");
        ImGui.SameLine();
        this.GameBox("##mygame", ref this.myGame, casino);
        ImGui.SameLine();
        var newGame = this.myGame.Trim();
        ImGui.BeginDisabled(newGame.Length == 0 || newGame == entry.Game || state.Busy);
        if (ImGui.Button("Change game"))
            state.SaveCasinoEntry(me, entry.Shift, newGame, null, $"Game changed to {newGame}.");
        ImGui.EndDisabled();

        if (entry.Profit is { } profit)
        {
            ImGui.AlignTextToFramePadding();
            Theme.Muted("Profit entered");
            ImGui.SameLine();
            ImGui.TextColored(Theme.GoldBright, Venue.Gil(profit));
            ImGui.SameLine();
            Theme.Muted(access.ManagesCasino ? "(change it with Edit below)" : "(only the dealer manager or an owner can change it now)");
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            Theme.Muted("Profit for your game this shift");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180 * Theme.Scale);
            ImGui.InputInt("##myprofit", ref this.myProfit, 100_000, 1_000_000);
            ImGui.SameLine();
            Theme.Muted("gil");
            ImGui.SameLine();
            ImGui.BeginDisabled(state.Busy);
            if (this.confirm.Button("profit", "Enter profit", small: false))
            {
                state.SaveCasinoEntry(me, entry.Shift, entry.Game, this.myProfit, $"Profit entered for shift {entry.Shift}: {Venue.Gil(this.myProfit)}.");
                this.myProfit = 0;
            }

            ImGui.SameLine();
            if (this.confirm.Button("leave", "Leave shift", small: false))
                state.RemoveCasinoEntry(entry);
            ImGui.EndDisabled();
            Theme.Muted("Enter it when your shift ends. Once entered, only the dealer manager or an owner can change it.");
        }

        var share = Venue.DealerShare(state.Config, casino, me.Name);
        if (share > 0)
        {
            ImGui.Spacing();
            Theme.Stat("Your dealer share tonight", Venue.Gil(share), Theme.Gold);
            Theme.EndStats();
            Theme.Muted("It is added to your total on the HR tab.");
        }
    }

    private void DrawAssign(CasinoNight casino)
    {
        Theme.Section("Put a dealer on a shift");
        var picked = state.Roster.FirstOrDefault(r => r.Id == this.assignId);
        ImGui.SetNextItemWidth(260 * Theme.Scale);
        if (ImGui.BeginCombo("##dealer", picked != null ? $"{picked.Name} ({picked.Job})" : "Choose a dealer"))
        {
            // dealers first, then everyone else, in case someone is covering
            foreach (var r in state.Roster.OrderByDescending(r => r.Job == "Dealer"))
            {
                if (ImGui.Selectable($"{r.Name} ({r.Job})", r.Id == this.assignId))
                    this.assignId = r.Id;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.RadioButton("Shift 1##assign", ref this.assignShift, 1);
        ImGui.SameLine();
        ImGui.RadioButton("Shift 2##assign", ref this.assignShift, 2);
        ImGui.SameLine();
        this.GameBox("##assigngame", ref this.assignGame, casino);
        ImGui.SameLine();
        var game = this.assignGame.Trim();
        ImGui.BeginDisabled(picked == null || game.Length == 0 || state.Busy);
        if (ImGui.Button("Assign") && picked != null)
        {
            state.SaveCasinoEntry(picked, this.assignShift, game, null, $"{picked.Name} is dealing {game} on shift {this.assignShift}.");
            this.assignId = "";
        }

        ImGui.EndDisabled();
    }

    private void DrawShift(Access access, CasinoNight casino, int shift)
    {
        var cfg = state.Config;
        var gross = casino.Gross[shift - 1];
        var dealers = casino.Dealers[shift - 1];
        var pool = Venue.DealerPool(cfg, gross);
        double? each = pool != null && dealers.Count > 0 ? pool / dealers.Count : null;

        Theme.Section($"Shift {shift}, two hours");
        Theme.Stat("Shift gross profit", Venue.Gil(gross));
        Theme.Stat("Dealer pool", Venue.Gil(pool));
        Theme.Stat("Apollo retained", gross != null && pool != null ? Venue.Gil(gross - pool) : "-");
        Theme.Stat("Each dealer", Venue.Gil(each), Theme.Gold);
        Theme.EndStats();

        var lines = casino.Entries.Where(e => e.Shift == shift).ToList();
        if (lines.Count == 0)
        {
            // a shift filled in on the site's Casino page has dealers but no lines
            Theme.Muted(dealers.Count > 0 ? $"Dealers: {string.Join(", ", dealers)}" : "No dealers on this shift yet.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable($"shift{shift}", access.ManagesCasino ? 4 : 3, flags))
            return;

        ImGui.TableSetupColumn("Dealer", ImGuiTableColumnFlags.None, 2.2f);
        ImGui.TableSetupColumn("Game", ImGuiTableColumnFlags.None, 2f);
        ImGui.TableSetupColumn("Game profit", ImGuiTableColumnFlags.None, 1.6f);
        if (access.ManagesCasino)
            ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableHeadersRow();
        foreach (var e in lines)
        {
            ImGui.PushID(e.Key);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(e.Name);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.Game);
            ImGui.TableNextColumn();
            if (e.Profit is { } p)
                ImGui.TextUnformatted(Venue.Gil(p));
            else
                Theme.Muted("not entered yet");

            if (access.ManagesCasino)
            {
                ImGui.TableNextColumn();
                this.DrawManage(e);
            }

            ImGui.PopID();
        }

        ImGui.EndTable();
    }

    private void DrawManage(CasinoEntry e)
    {
        if (ImGui.SmallButton("Edit"))
        {
            this.editGame = e.Game;
            this.editHasProfit = e.Profit != null;
            this.editProfit = (int)(e.Profit ?? 0);
            ImGui.OpenPopup("edit");
        }

        if (ImGui.BeginPopup("edit"))
        {
            ImGui.TextColored(Theme.Gold, $"{e.Name}, shift {e.Shift}");
            ImGui.SetNextItemWidth(220 * Theme.Scale);
            ImGui.InputText("Game", ref this.editGame, 40);
            ImGui.Checkbox("Profit entered", ref this.editHasProfit);
            if (this.editHasProfit)
            {
                ImGui.SetNextItemWidth(220 * Theme.Scale);
                ImGui.InputInt("Profit (gil)", ref this.editProfit, 100_000, 1_000_000);
            }

            ImGui.Spacing();
            var dealer = state.Roster.FirstOrDefault(r => string.Equals(r.Name, e.Name, StringComparison.OrdinalIgnoreCase));
            ImGui.BeginDisabled(dealer == null || this.editGame.Trim().Length == 0 || state.Busy);
            if (ImGui.Button("Save") && dealer != null)
            {
                if (this.editHasProfit)
                    state.SaveCasinoEntry(dealer, e.Shift, this.editGame.Trim(), this.editProfit, $"{e.Name}'s line updated.");
                else
                    state.ClearCasinoProfit(dealer, e, this.editGame.Trim());
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(state.Busy);
        if (this.confirm.Button(e.Key, "Remove"))
            state.RemoveCasinoEntry(e);
        ImGui.EndDisabled();
    }

    private static void DrawNight(CasinoNight casino, VenueConfig cfg)
    {
        double gross = (casino.Gross[0] ?? 0) + (casino.Gross[1] ?? 0);
        double paid = (Venue.DealerPool(cfg, casino.Gross[0]) ?? 0) + (Venue.DealerPool(cfg, casino.Gross[1]) ?? 0);
        Theme.Section("End of night");
        Theme.Stat("Night gross profit", Venue.Gil(gross));
        Theme.Stat("Dealer payouts", Venue.Gil(paid));
        Theme.Stat("Apollo retained", Venue.Gil(gross - paid), Theme.Gold);
        Theme.EndStats();
        Theme.Muted("Each shift's pool uses the progressive dealer profit share in the site's Settings, split evenly among that shift's dealers.");
    }

    /// <summary>A box for the game's name, with the games already on tonight's sheet to pick from.</summary>
    private void GameBox(string id, ref string game, CasinoNight casino)
    {
        ImGui.SetNextItemWidth(180 * Theme.Scale);
        ImGui.InputTextWithHint(id, "Game, e.g. Eorzean Poker", ref game, 40);
        var known = casino.Entries.Select(e => e.Game).Where(g => g.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (known.Count == 0)
            return;

        ImGui.SameLine(0, 2);
        if (ImGui.BeginCombo(id + "pick", "", ImGuiComboFlags.NoPreview))
        {
            foreach (var g in known)
            {
                if (ImGui.Selectable(g))
                    game = g;
            }

            ImGui.EndCombo();
        }
    }
}
