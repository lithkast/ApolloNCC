# ApolloNCC

A Dalamud plugin for FFXIV made specifically for the Apollo Night Club and Casino venue.
Staff only: you sign in with the same email and password you use on apolloncc.org.

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
| Reception | Logins that can edit Reception on the site | Add a booking (patron can be filled from your target), see the total and the reception bonus, put yourself down as tonight's receptionist, send a staff member a "time's up" tell. |
| Security | Everyone (Blacklist); bouncers, their managers, owners (the rest) | Blacklist search, current bookings, patron warnings, alerts when a blacklisted player is nearby. |
| Bar, Casino, Raffle | Everyone | Placeholders. Not built yet. |
| Settings | Everyone | Sign in / out, Discord link code for Hermes, alert switches. |

Not built yet: shouts and macros, VIP lookup, bar buyouts, casino tracking, raffle.

## What the web app needs

Reading everything, bookings, the collab switch and the Discord link work against the site
as it is. Three things need the `claude/bold-darwin-r1e7rd` branch of the web app deployed
(`functions/index.js`, `functions/clock.js`, `firestore.rules`):

- the clock buttons (a `pluginClock` function)
- patron warnings and telling the rest of security about a banned player (`warnings` and
  `sightings` collections)
- tells reaching someone who is on an alt (`presence` collection)

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

Every push also builds on GitHub Actions. The run's `ApolloNCC` artifact is a ready-to-use
folder: `ApolloNCC.dll`, its manifest, and `images/icon.png` (the logo).

To load it in game: unzip the artifact into a folder that stays put, then in
`/xlsettings` > Experimental > Dev Plugin Locations add the full path to `ApolloNCC.dll`,
and enable it under `/xlplugins` > Dev Tools > Installed Dev Plugins.

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
