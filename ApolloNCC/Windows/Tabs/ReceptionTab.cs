using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;

namespace ApolloNCC.Windows.Tabs;

public sealed class ReceptionTab(ApolloState state)
{
    private static readonly string[] RpTypes = ["Public", "Private", "No Staff"];

    private string employee = "";
    private string patron = "";
    private string patronWorld = "";
    private string rpType = "";
    private double hours = 1;
    private int guests = 1;
    private bool vip;
    private string location = "";
    private string? tellResult;

    // the booking being changed in the Edit popup, and the one whose Remove button is waiting for a second click
    private string editId = "";
    private double editHours;
    private int editGuests;
    private bool editVip;
    private string editLocation = "";
    private string confirmRemoveId = "";
    private DateTimeOffset confirmRemoveUntil;

    public void Draw(Access access)
    {
        var cfg = state.Config;
        var now = DateTimeOffset.UtcNow;
        var total = state.Bookings.Sum(b => b.Total ?? 0);

        Theme.Section("Tonight");
        Theme.Stat("Receptionist", state.Receptionist.Length > 0 ? state.Receptionist : "nobody yet");
        Theme.Stat("Bookings total", Venue.Gil(total));
        Theme.Stat($"Reception bonus ({cfg.ReceptionBonus:P0})", Venue.Gil(total * cfg.ReceptionBonus), Theme.Gold);
        Theme.Stat("Running now", state.Bookings.Count(b => b.IsRunning(now)).ToString());
        Theme.EndStats();
        if (state.MyName.Length > 0 && !string.Equals(state.Receptionist, state.MyName, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Button("I'm on reception tonight"))
                state.TakeReception();
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Puts your name down as tonight's receptionist. The reception bonus goes to whoever is named.");
        }

        if (state.CollabNight)
        {
            Theme.Section("New booking");
            ImGui.TextColored(Theme.Warn, "Reception is not taking bookings on a collab night.");
        }
        else
        {
            this.DrawForm(cfg, now);
        }

        Theme.Section("Bookings");
        BookingTable.Draw(
            "reception", state.Bookings, now,
            BookingTable.Columns.Employee | BookingTable.Columns.Patron | BookingTable.Columns.Type | BookingTable.Columns.Total,
            b => this.DrawActions(b, now));
        if (this.tellResult != null)
            Theme.Muted(this.tellResult);
    }

    private void DrawForm(VenueConfig cfg, DateTimeOffset now)
    {
        Theme.Section("New booking");
        var labelWidth = 90 * Theme.Scale;

        Label("Staff", labelWidth);
        ImGui.SetNextItemWidth(330 * Theme.Scale);
        if (ImGui.BeginCombo("##staff", this.employee.Length > 0 ? this.employee : "Choose staff member"))
        {
            // People on the clock first: they are the ones who can actually take a booking.
            foreach (var r in state.Roster.OrderByDescending(r => Venue.OnClock(r, now)))
            {
                var label = $"{r.Name} ({r.Job}){(Venue.OnClock(r, now) ? "" : "  - off the clock")}";
                if (ImGui.Selectable(label, r.Name == this.employee))
                    this.employee = r.Name;
            }

            ImGui.EndCombo();
        }

        Label("Patron", labelWidth);
        TargetPicker.Draw("patron", ref this.patron, ref this.patronWorld);

        Label("RP type", labelWidth);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        if (ImGui.BeginCombo("##type", this.rpType.Length > 0 ? this.rpType : "Choose type"))
        {
            foreach (var t in RpTypes)
            {
                if (ImGui.Selectable(t, t == this.rpType))
                    this.rpType = t;
            }

            ImGui.EndCombo();
        }

        Label("Hours", labelWidth);
        ImGui.SetNextItemWidth(120 * Theme.Scale);
        ImGui.InputDouble("##hours", ref this.hours, 0.5, 1, "%.2f");
        this.hours = Math.Clamp(this.hours, 0, 24);
        ImGui.SameLine();
        Label("Guests", 0);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100 * Theme.Scale);
        ImGui.InputInt("##guests", ref this.guests, 1, 1);
        this.guests = Math.Clamp(this.guests, 1, 99);
        ImGui.SameLine();
        ImGui.Checkbox("VIP", ref this.vip);

        Label("Room", labelWidth);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        if (cfg.Rooms.Count == 0)
        {
            ImGui.InputText("##room", ref this.location, 60);
        }
        else if (ImGui.BeginCombo("##room", this.location.Length > 0 ? this.location : "Choose room"))
        {
            foreach (var r in cfg.Rooms)
            {
                if (ImGui.Selectable(r, r == this.location))
                    this.location = r;
            }

            ImGui.EndCombo();
        }

        // what the patron pays, by the prices in the site's Settings
        var price = Venue.ReceptionTotal(cfg, this.rpType, this.hours, this.guests, this.vip);
        Label("Patron pays", labelWidth);
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(Theme.GoldBright, price != null ? Venue.Gil(price) : "-");

        var missing = this.Missing();
        ImGui.Dummy(new System.Numerics.Vector2(labelWidth - ImGui.GetStyle().ItemSpacing.X, 0));
        ImGui.SameLine();
        ImGui.BeginDisabled(missing != null || state.Busy);
        if (ImGui.Button("Submit booking"))
        {
            var name = this.patron.Trim();
            var world = this.patronWorld.Trim();
            state.CreateBooking(
                this.employee, world.Length > 0 && name.Length > 0 ? $"{name}@{world}" : name,
                this.rpType, this.hours, this.guests, this.vip, this.location.Trim(), this.ClearForm);
        }

        ImGui.EndDisabled();
        if (missing != null)
        {
            ImGui.SameLine();
            Theme.Muted(missing);
        }
    }

    private void DrawActions(Booking b, DateTimeOffset now)
    {
        this.DrawTell(b, now);
        ImGui.SameLine();

        if (ImGui.SmallButton("Edit"))
        {
            this.editId = b.Id;
            this.editHours = b.Hours ?? 1;
            this.editGuests = (int)(b.Guests ?? 1);
            this.editVip = b.Vip;
            this.editLocation = b.Location;
            ImGui.OpenPopup("edit");
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Change the hours (to extend their time), guests, VIP or room.");
        this.DrawEditPopup(b, now);

        // removing takes two clicks, so a slip of the mouse can't lose a booking
        ImGui.SameLine();
        var confirming = this.confirmRemoveId == b.Id && now < this.confirmRemoveUntil;
        ImGui.BeginDisabled(state.Busy);
        if (confirming)
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Bad with { W = 0.45f });
        if (ImGui.SmallButton(confirming ? "Click again" : "Remove"))
        {
            if (confirming)
            {
                state.RemoveBooking(b);
                this.confirmRemoveId = "";
            }
            else
            {
                this.confirmRemoveId = b.Id;
                this.confirmRemoveUntil = now.AddSeconds(4);
            }
        }

        if (confirming)
            ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Takes this booking off the Reception page. It no longer counts towards tonight's total or the bonus.");
    }

    private void DrawEditPopup(Booking b, DateTimeOffset now)
    {
        if (!ImGui.BeginPopup("edit"))
            return;

        var cfg = state.Config;
        ImGui.TextColored(Theme.Gold, b.Patron.Length > 0 ? $"{b.Patron} with {(b.Employee.Length > 0 ? b.Employee : "no staff")}" : b.Employee);
        ImGui.Spacing();

        ImGui.SetNextItemWidth(120 * Theme.Scale);
        ImGui.InputDouble("Hours", ref this.editHours, 0.5, 1, "%.2f");
        this.editHours = Math.Clamp(this.editHours, 0.25, 24);
        ImGui.SameLine();
        if (ImGui.SmallButton("+30 min"))
            this.editHours = Math.Min(this.editHours + 0.5, 24);
        ImGui.SameLine();
        if (ImGui.SmallButton("+1 hour"))
            this.editHours = Math.Min(this.editHours + 1, 24);

        ImGui.SetNextItemWidth(120 * Theme.Scale);
        ImGui.InputInt("Guests", ref this.editGuests, 1, 1);
        this.editGuests = Math.Clamp(this.editGuests, 1, 99);
        ImGui.Checkbox("VIP", ref this.editVip);

        ImGui.SetNextItemWidth(200 * Theme.Scale);
        if (cfg.Rooms.Count == 0)
        {
            ImGui.InputText("Room", ref this.editLocation, 60);
        }
        else if (ImGui.BeginCombo("Room", this.editLocation.Length > 0 ? this.editLocation : "Choose room"))
        {
            foreach (var r in cfg.Rooms)
            {
                if (ImGui.Selectable(r, r == this.editLocation))
                    this.editLocation = r;
            }

            ImGui.EndCombo();
        }

        // what the change does, before it is saved
        ImGui.Spacing();
        var price = Venue.ReceptionTotal(cfg, b.RpType, this.editHours, this.editGuests, this.editVip);
        Theme.Muted("Patron pays");
        ImGui.SameLine();
        ImGui.TextColored(Theme.GoldBright, Venue.Gil(price));
        if (price is { } p && b.Total is { } was && Math.Abs(p - was) >= 1)
        {
            ImGui.SameLine();
            Theme.Muted($"({(p > was ? "+" : "-")}{Venue.Gil(Math.Abs(p - was))})");
        }

        if (b.StartsAt is { } start)
        {
            var left = start.AddHours(this.editHours) - now;
            Theme.Muted("Time left");
            ImGui.SameLine();
            if (left > TimeSpan.Zero)
                ImGui.TextUnformatted(Theme.Clock(left));
            else
                ImGui.TextColored(Theme.Bad, "already up");
        }

        ImGui.Spacing();
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.Button("Save"))
        {
            state.UpdateBooking(b, this.editHours, this.editGuests, this.editVip, this.editLocation.Trim());
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void DrawTell(Booking b, DateTimeOffset now)
    {
        if (b.Employee.Length == 0)
        {
            // keep Edit and Remove lined up on rooms rented without staff
            ImGui.BeginDisabled();
            ImGui.SmallButton("Tell: time's up");
            ImGui.EndDisabled();
            return;
        }

        var isMe = string.Equals(b.Employee, state.MyName, StringComparison.OrdinalIgnoreCase);
        var target = isMe ? null : this.FindCharacter(b.Employee, now);
        var timeUp = b.EndsAt is { } end && end <= now;

        ImGui.BeginDisabled(target == null);
        if (timeUp)
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Bad with { W = 0.45f });
        if (ImGui.SmallButton("Tell: time's up") && target is { } t)
            this.SendTimeUpTell(b, t.Name, t.World);
        if (timeUp)
            ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(target is { } found
                ? $"Sends a tell to {found.Name}@{found.World} saying their time is up."
                : isMe ? "This is your own booking." : $"{b.Employee} isn't nearby and hasn't been seen in game with the plugin tonight.");
        }
    }

    /// <summary>
    /// The character to send a staff member's tell to: the one their plugin last reported (it may be an alt),
    /// or failing that someone nearby with the name on the roster.
    /// </summary>
    private (string Name, string World)? FindCharacter(string employee, DateTimeOffset now)
    {
        var login = state.Roster.FirstOrDefault(r => string.Equals(r.Name, employee, StringComparison.OrdinalIgnoreCase))?.Login;
        if (login is { Length: > 0 } && state.Presence.TryGetValue(login, out var p) &&
            now - p.At < TimeSpan.FromHours(16) && p.Name.Length > 0 && p.World.Length > 0)
            return (p.Name, p.World);

        foreach (var obj in Plugin.ObjectTable.PlayerObjects)
        {
            if (obj is IPlayerCharacter pc && string.Equals(pc.Name.TextValue, employee, StringComparison.OrdinalIgnoreCase) &&
                pc.HomeWorld.ValueNullable?.Name.ExtractText() is { Length: > 0 } world)
                return (pc.Name.TextValue, world);
        }

        return null;
    }

    private void SendTimeUpTell(Booking b, string name, string world)
    {
        var who = b.Patron.Length > 0 ? $" with {b.Patron}" : "";
        var where = b.Location.Length > 0 ? $" in {b.Location}" : "";
        var message = $"[Apollo] Time is up{who}{where}.";
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                GameChat.SendTell(name, world, message);
                this.tellResult = $"Tell sent to {b.Employee}.";
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                this.tellResult = $"Could not send the tell: {e.Message}";
            }
        });
    }

    private string? Missing()
    {
        if (this.rpType.Length == 0)
            return "Choose the RP type.";
        if (this.employee.Length == 0 && this.rpType != "No Staff")
            return "Choose a staff member.";
        if (string.IsNullOrWhiteSpace(this.patron))
            return "Enter the patron's name.";
        return this.hours <= 0 ? "Enter how many hours." : null;
    }

    private void ClearForm()
    {
        this.employee = this.patron = this.patronWorld = this.rpType = this.location = "";
        this.hours = 1;
        this.guests = 1;
        this.vip = false;
    }

    private static void Label(string text, float width)
    {
        ImGui.AlignTextToFramePadding();
        Theme.Muted(text);
        if (width > 0)
            ImGui.SameLine(width);
    }
}
