# ApolloNCC

A Dalamud plugin for FFXIV made specifically for the Apollo Night Club and Casino venue.
Staff only: you sign in with the same email and password you use on apolloncc.org.

## Installing

1. In game, type `/xlsettings` and open the **Experimental** tab.
2. Under **Custom Plugin Repositories**, paste this link, click the plus, and save:

   ```
   https://github.com/lithkast/ApolloNCC/releases/latest/download/repo.json
   ```

3. Type `/xlplugins`, search for **ApolloNCC** and install it. Updates arrive like any other plugin's.

Open it in game with `/apollo` (`/apollo settings` jumps to Settings).

## How it works

The plugin is a second front end to the Apollo web app (`lithkast/Apollo-web-app`). It signs
in to the site's Firebase project as the staff member and reads and writes the same rows the
site does, so the site's own database rules decide what each login may see and change.
Clocking in and out goes through the same code as Hermes' slash commands in Discord.

Nothing is stored by the plugin except the sign-in (a refresh token, never the password) and
two alert switches.

## What works today

| Tab | Who sees it | What it does |
| --- | --- | --- |
| HR | Every staff login | Clock in / out and PF clock in / out (party finder, shouts or both), with tonight's hours and pay worked out as the site does: shift pay, promotion pay, booking fees, reception bonus, bar buyout share. Your bookings with time left. Managers and assistant managers also see their department; owners and co-owners see everyone, Hermes' "to review" list, and the collab-night switch. |
| Reception | Logins that can edit Reception on the site | Add a booking (patron can be filled from your target), see the total and the reception bonus, put yourself down as tonight's receptionist, send a staff member a "time's up" tell. Edit a booking (extend the hours, change guests, VIP or room) or remove it. |
| Security | Everyone (Blacklist); bouncers, their managers, owners (the rest) | Blacklist search, current bookings, patron warnings (logins that can edit the Blacklist on the site can also remove one), alerts when a blacklisted player is nearby. |
| Bar | Every staff login | The menu by section: pick a drink to see its picture, price, in-game item, description and preparation macro. "Say it" announces the drink in /say; "Make it" plays the preparation macro in chat. Bar managers and owners record a bar buyout (it quotes what to collect and how the bartenders split it) and can remove one. |
| Casino | Logins that can see Casino on the site | A dealer picks a shift and a game, goes on shift, and enters the game's profit once (a reminder comes every two hours). Each shift's gross profit and dealer list on the site are worked out from those lines, and the dealer's share shows here and in their HR total. The dealer manager and owners assign dealers, correct games and profits, and remove lines. |
| Raffle | Owners and co-owners | Starting pot, add tickets (from target or typed, with an optional Discord name and whether the gil was collected), close and reopen, Raffle Pick (Hermes draws and announces the winner; the plugin yells it), and the announcement macro. |
| Settings | Everyone | Sign in / out, Discord link code for Hermes, alert switches. Owners, co-owners and Front of House managers also get greeter mode: while they are on the clock and inside the venue, each new arrival gets one welcome tell with a message they can edit. |

Not built yet: shouts, the dealer / reception / host macro sets, VIP lookup.

## What the web app needs

Reading everything, bookings, the collab switch, the bar, the casino and the Discord link
work against the site as it is. These need the `claude/bold-darwin-r1e7rd` branch of the web
app deployed:

- the clock buttons (a `pluginClock` function)
- patron warnings and telling the rest of security about a banned player (`warnings` and
  `sightings` collections)
- tells reaching someone who is on an alt (`presence` collection)
- the raffle's starting pot, close / reopen and Raffle Pick (`night/raffle`, a `rafflePick`
  function, and Hermes' `/raffle...` commands)

Until then the plugin says so where it matters instead of failing silently.

## Alts

The sign-in is saved per game install, not per character, so every alt uses the same staff
login: clocking in on an alt clocks in the person. The plugin tells the web app which
character is logged in so Reception's tells reach the right one.

## Building

Needs the .NET 10 SDK and Dalamud's dev files (installed by XIVLauncher, or pointed to
with `DALAMUD_HOME`).

```bash
dotnet build -c Release
```

Every push builds on GitHub Actions (the `Build` workflow), as a check.

## Releasing an update

On GitHub: Actions > **Release** > Run workflow, with a version number higher than the last
one (e.g. `0.1.1`) and a line about what changed. It builds that version and publishes it as
a GitHub release; the link above always serves the latest release, so staff get the update
from the plugin installer.

## Layout

```
ApolloNCC/
  Plugin.cs            entry point, /apollo command, per-frame tick
  Configuration.cs     the saved sign-in and alert switches
  Api/                 Firebase sign-in, Firestore and functions client; the web app's rows
  Services/            state and polling, who sees what, venue time and pay sums, alerts, chat
  Windows/             main window, the site's colours, one file per tab
```

`Services/Venue.cs` and `Services/Access.cs` mirror logic in the web app
(`functions/clock.js`, `public/index.html`). When pay rules or roles change there, change
them here too.
