using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

/// <summary>The 50/50 raffle, for owners and co-owners.</summary>
public sealed class RaffleTab(ApolloState state, MacroRunner macros)
{
    private readonly Confirm confirm = new();

    private int startInput;
    private double startSaved = -1;
    private string patron = "";
    private string world = "";
    private string discord = "";
    private int tickets = 1;
    private bool paid;

    public void Draw(Access access)
    {
        var cfg = state.Config;
        var night = state.Raffle;
        var totals = Venue.Raffle(cfg, state.RaffleEntries, night);

        Theme.Section("50/50 raffle");
        ImGui.TextColored(night.Closed ? Theme.Warn : Theme.Good, night.Closed ? "Closed: no more tickets." : "Open: selling tickets.");
        ImGui.SameLine();
        Theme.Muted($"{Venue.Gil(cfg.RaffleTicketPrice)} a ticket.");

        Theme.Stat("Lottery total", Venue.Gil(totals.Total));
        Theme.Stat($"Club cut ({cfg.RaffleClubCut:P0} of sales)", Venue.Gil(totals.ClubCut));
        Theme.Stat("Winner's cut", Venue.Gil(totals.WinnerCut), Theme.Gold);
        Theme.Stat("Tickets sold", $"{totals.Tickets:0} to {totals.People}");
        Theme.EndStats();

        this.DrawStart(night);
        this.DrawFlow(night, totals);
        if (night.Winner is { } winner)
            this.DrawWinner(winner);
        if (!night.Closed)
            this.DrawAdd(cfg);
        this.DrawEntries(cfg);
    }

    private void DrawStart(RaffleNight night)
    {
        // follow what is saved, unless the owner is part-way through typing something else
        if (night.Start != this.startSaved)
        {
            if (this.startSaved < 0 || this.startInput == (int)this.startSaved)
                this.startInput = (int)night.Start;
            this.startSaved = night.Start;
        }

        ImGui.AlignTextToFramePadding();
        Theme.Muted("Starting pot");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180 * Theme.Scale);
        ImGui.InputInt("##start", ref this.startInput, 100_000, 1_000_000);
        this.startInput = Math.Max(0, this.startInput);
        ImGui.SameLine();
        Theme.Muted("gil");
        ImGui.SameLine();
        ImGui.BeginDisabled(this.startInput == (int)night.Start || state.Busy);
        if (ImGui.Button("Set"))
            state.SetRaffleStart(this.startInput);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Gil already in the pot before any ticket is sold. The club's cut is taken from ticket sales only.");
    }

    private void DrawFlow(RaffleNight night, RaffleTotals totals)
    {
        ImGui.Spacing();
        ImGui.BeginDisabled(state.Busy);
        if (!night.Closed)
        {
            ImGui.BeginDisabled(totals.People == 0);
            if (ImGui.Button("Close the raffle"))
                state.SetRaffleClosed(true);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Stops ticket sales and unlocks Raffle Pick. You can reopen it if it was closed by mistake.");
        }
        else
        {
            if (ImGui.Button("Reopen the raffle"))
                state.SetRaffleClosed(false);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Sells tickets again. A winner already drawn is forgotten.");

            ImGui.SameLine();
            ImGui.BeginDisabled(night.Winner != null || totals.People == 0);
            if (this.confirm.Button("pick", "Raffle Pick", small: false))
                state.PickRaffleWinner();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(night.Winner != null
                    ? "The winner is drawn. Reopen the raffle to draw again."
                    : "Draws the winner (every ticket is one chance), yells it in game and has Hermes announce it in Discord.");
            }
        }

        // the announcement macro from the site, with tonight's pot filled in
        var announce = Venue.FillRaffleMacro(state.Config, totals.Total).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (announce.Count > 0)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(macros.Running);
            if (ImGui.Button("Announce the raffle"))
                macros.Play(announce);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(string.Join("\n", announce));
        }

        if (state.RaffleEntries.Count > 0)
        {
            ImGui.SameLine();
            if (this.confirm.Button("clear", "Clear the raffle", small: false))
                state.ClearRaffle();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("For a new night: removes every entry and the winner. Log the night to Data on the site first.");
        }

        ImGui.EndDisabled();
        if (night.Closed && night.Winner == null && totals.Unpaid > 0)
            ImGui.TextColored(Theme.Warn, $"{totals.Unpaid} {(totals.Unpaid == 1 ? "entry is" : "entries are")} not marked paid. Their tickets still count in the draw.");
    }

    private void DrawWinner(RaffleWinner w)
    {
        ImGui.Spacing();
        ImGui.SetWindowFontScale(1.3f);
        ImGui.TextColored(Theme.Gold, $"Winner: {Theme.PersonAt(w.Patron, w.World)}");
        ImGui.SetWindowFontScale(1f);
        ImGui.TextUnformatted($"{w.Tickets:0} ticket{(w.Tickets == 1 ? "" : "s")}. They take home {Venue.Gil(w.Pot)}. Drawn by {(w.By.Length > 0 ? w.By : "Hermes")}.");
        ImGui.BeginDisabled(macros.Running);
        if (ImGui.SmallButton("Yell the winner"))
            macros.Play([Alerts.RaffleYell(w)]);
        ImGui.EndDisabled();
        ImGui.SameLine();
        Theme.Muted(w.Mentioned ? "Hermes mentioned them in Discord." : "No Discord name matched, so Hermes announced them by name.");
    }

    private void DrawAdd(VenueConfig cfg)
    {
        Theme.Section("Add tickets");
        TargetPicker.Draw("raffle", ref this.patron, ref this.world);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        ImGui.InputTextWithHint("##discord", "Discord name (optional)", ref this.discord, 60);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Their Discord username, so Hermes can @ them if they win.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * Theme.Scale);
        ImGui.InputInt("tickets", ref this.tickets, 1, 5);
        this.tickets = Math.Clamp(this.tickets, 1, 9999);
        ImGui.SameLine();
        ImGui.TextColored(Theme.GoldBright, Venue.Gil(this.tickets * cfg.RaffleTicketPrice));

        ImGui.Checkbox("I've collected the gil from them", ref this.paid);
        ImGui.SameLine();
        var name = this.patron.Trim();
        ImGui.BeginDisabled(name.Length == 0 || state.Busy);
        if (ImGui.Button("Add to the raffle"))
        {
            state.AddRaffleEntry(name, this.world.Trim(), this.discord.Trim().TrimStart('@'), this.tickets, this.paid, () =>
            {
                this.patron = this.world = this.discord = "";
                this.tickets = 1;
                this.paid = false;
            });
        }

        ImGui.EndDisabled();
        if (!this.paid)
        {
            ImGui.SameLine();
            Theme.Muted("Not ticked: they go on the list as still owing.");
        }
    }

    private void DrawEntries(VenueConfig cfg)
    {
        Theme.Section("Tickets sold");
        var entries = state.RaffleEntries;
        if (entries.Count == 0)
        {
            Theme.Muted("No tickets sold yet.");
            return;
        }

        const ImGuiTableFlags flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp;
        if (!ImGui.BeginTable("raffle", 7, flags))
            return;

        ImGui.TableSetupColumn("Patron", ImGuiTableColumnFlags.None, 2.4f);
        ImGui.TableSetupColumn("Discord", ImGuiTableColumnFlags.None, 1.6f);
        ImGui.TableSetupColumn("Tickets", ImGuiTableColumnFlags.None, 0.9f);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.None, 1.3f);
        ImGui.TableSetupColumn("Paid", ImGuiTableColumnFlags.None, 0.7f);
        ImGui.TableSetupColumn("Taken by", ImGuiTableColumnFlags.None, 1.8f);
        ImGui.TableSetupColumn("##remove", ImGuiTableColumnFlags.None, 1.2f);
        ImGui.TableHeadersRow();
        foreach (var e in entries)
        {
            ImGui.PushID(e.Id);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(e.Paid ? Theme.Text : Theme.Warn, Theme.PersonAt(e.Patron.Length > 0 ? e.Patron : "(no name)", e.World));
            ImGui.TableNextColumn();
            Theme.Muted(e.Discord);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted($"{e.Tickets:0}");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(Venue.Gil(e.Tickets * cfg.RaffleTicketPrice));
            ImGui.TableNextColumn();
            var isPaid = e.Paid;
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Checkbox("##paid", ref isPaid))
                state.SetRafflePaid(e, isPaid);
            ImGui.EndDisabled();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(e.TakenBy);
            ImGui.TableNextColumn();
            ImGui.BeginDisabled(state.Busy);
            if (this.confirm.Button(e.Id, "Remove"))
                state.RemoveRaffleEntry(e);
            ImGui.EndDisabled();
            ImGui.PopID();
        }

        ImGui.EndTable();
    }
}
