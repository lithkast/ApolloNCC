using System;
using System.Linq;
using ApolloNCC.Api;
using ApolloNCC.Services;
using Dalamud.Bindings.ImGui;

namespace ApolloNCC.Windows.Tabs;

public sealed class ReceptionTab(ApolloState state)
{
    private string staffId = "";
    private string clientName = "";
    private string clientWorld = "";
    private string room = "";
    private int minutes = 30;
    private int amount;
    private string? tellResult;

    public void Draw(Profile profile)
    {
        var summary = state.Reception;
        if (summary == null)
        {
            Theme.Muted("Loading reception...");
            return;
        }

        var now = DateTimeOffset.UtcNow;

        Theme.Section("Tonight");
        Theme.Stat("Bookings paid", Theme.Gil(summary.TotalPaid));
        Theme.Stat($"Reception bonus ({summary.BonusRate:P0})", Theme.Gil(summary.ReceptionBonus));
        Theme.Stat("Active bookings", summary.Bookings.Count(b => b.EndsAt > now).ToString());
        Theme.EndStats();

        this.DrawForm(summary);

        Theme.Section("Bookings");
        BookingTable.Draw(
            "reception", summary.Bookings, now,
            BookingTable.Columns.Staff | BookingTable.Columns.Client | BookingTable.Columns.BookedBy | BookingTable.Columns.Amount,
            b => this.DrawActions(profile, b, now));
        if (this.tellResult != null)
            Theme.Muted(this.tellResult);
    }

    private void DrawForm(ReceptionSummary summary)
    {
        Theme.Section("New booking");
        var labelWidth = 90 * Theme.Scale;
        var selected = summary.Staff.Find(s => s.EmployeeId == this.staffId);

        Label("Staff", labelWidth);
        ImGui.SetNextItemWidth(330 * Theme.Scale);
        if (ImGui.BeginCombo("##staff", selected != null ? $"{selected.Name} ({selected.Job})" : "Choose staff member"))
        {
            // People on the clock first: they are the ones who can actually take a booking.
            foreach (var s in summary.Staff.OrderByDescending(s => s.ClockedIn).ThenBy(s => s.Name))
            {
                var label = $"{s.Name} ({s.Job}){(s.ClockedIn ? "" : "  - off the clock")}";
                if (ImGui.Selectable(label, s.EmployeeId == this.staffId))
                    this.staffId = s.EmployeeId;
            }

            ImGui.EndCombo();
        }

        Label("Client", labelWidth);
        TargetPicker.Draw("client", ref this.clientName, ref this.clientWorld);

        Label("Room", labelWidth);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        if (ImGui.BeginCombo("##room", this.room.Length > 0 ? this.room : "Choose room"))
        {
            foreach (var r in state.Meta.Rooms)
            {
                if (ImGui.Selectable(r, r == this.room))
                    this.room = r;
            }

            ImGui.EndCombo();
        }

        Label("Minutes", labelWidth);
        ImGui.SetNextItemWidth(120 * Theme.Scale);
        ImGui.InputInt("##minutes", ref this.minutes, 15, 30);
        this.minutes = Math.Clamp(this.minutes, 0, 24 * 60);

        Label("Client pays", labelWidth);
        ImGui.SetNextItemWidth(200 * Theme.Scale);
        ImGui.InputInt("##amount", ref this.amount, 50_000, 100_000);
        this.amount = Math.Max(0, this.amount);
        ImGui.SameLine();
        Theme.Muted("gil");

        var missing = this.Missing();
        ImGui.Dummy(new System.Numerics.Vector2(labelWidth, 0));
        ImGui.SameLine();
        ImGui.BeginDisabled(missing != null || state.Busy);
        if (ImGui.Button("Submit booking"))
        {
            state.CreateBooking(
                new CreateBookingRequest
                {
                    StaffId = this.staffId,
                    ClientName = this.clientName.Trim(),
                    ClientWorld = this.clientWorld.Trim() is { Length: > 0 } w ? w : null,
                    Room = this.room,
                    Minutes = this.minutes,
                    Amount = this.amount,
                },
                this.ClearForm);
        }

        ImGui.EndDisabled();
        if (missing != null)
        {
            ImGui.SameLine();
            Theme.Muted(missing);
        }
    }

    private void DrawActions(Profile profile, Booking b, DateTimeOffset now)
    {
        var timeUp = b.EndsAt <= now;
        var isMe = b.StaffId == profile.EmployeeId;
        var canTell = !isMe && b.StaffCharacter is { Length: > 0 } && b.StaffWorld is { Length: > 0 };

        ImGui.BeginDisabled(!canTell);
        if (timeUp)
            ImGui.PushStyleColor(ImGuiCol.Button, Theme.Bad with { W = 0.55f });
        if (ImGui.SmallButton("Tell: time's up"))
            this.SendTimeUpTell(b);
        if (timeUp)
            ImGui.PopStyleColor();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(canTell
                ? $"Sends a tell to {b.StaffCharacter}@{b.StaffWorld} saying their time is up."
                : isMe ? "This is your own booking." : $"{b.StaffName} has not been seen in game with the plugin yet.");
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.SmallButton("End"))
            state.EndBooking(b);
        ImGui.EndDisabled();
    }

    private void SendTimeUpTell(Booking b)
    {
        var message = $"[Apollo] Time is up with {b.ClientName} in {b.Room}.";
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            try
            {
                GameChat.SendTell(b.StaffCharacter!, b.StaffWorld!, message);
                this.tellResult = $"Tell sent to {b.StaffName}.";
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException)
            {
                this.tellResult = $"Could not send the tell: {e.Message}";
            }
        });
    }

    private string? Missing()
    {
        if (this.staffId.Length == 0)
            return "Choose a staff member.";
        if (string.IsNullOrWhiteSpace(this.clientName))
            return "Enter the client's name.";
        if (this.room.Length == 0)
            return "Choose a room.";
        return this.minutes <= 0 ? "Enter how many minutes." : null;
    }

    private void ClearForm()
    {
        this.staffId = "";
        this.clientName = "";
        this.clientWorld = "";
        this.room = "";
        this.amount = 0;
    }

    private static void Label(string text, float width)
    {
        ImGui.AlignTextToFramePadding();
        Theme.Muted(text);
        ImGui.SameLine(width);
    }
}
