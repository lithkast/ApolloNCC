# ApolloNCC

A Dalamud plugin for FFXIV made specifically for the Apollo Night Club and Casino venue.
Staff only: it needs an Apollo web app login and a plugin key issued by an owner.

Open it in game with `/apollo` (`/apollo settings` jumps to Settings).

## What works today

| Tab | Who sees it | What it does |
| --- | --- | --- |
| HR | Everyone | Clock in / out, PF clock in / out (Party Finder and/or Shouts must be ticked), live pay and hours, your bookings with time left. Managers and assistant managers also get their department's staff; owners and co-owners get everyone. Early clock-in approval and collab pay live in that staff table. |
| Reception | Receptionists, Front of House managers, owners | Book staff for patrons, see paid total and the 20% reception bonus, send a "time's up" tell, end bookings. |
| Security | Everyone (blacklist); bouncers, Security managers, owners (the rest) | Blacklist search, current bookings, patron warnings (typed in or from target), banned-patron alerts. |
| Bar, Casino, Raffle | Everyone | Placeholders. Not built yet. |
| Settings | Everyone | Plugin key, web app address, Discord link code, alert switches, demo mode. |

Not built yet: shouts, macros, VIP lookup, bar buyouts, casino tracking, raffle, and
general Discord bot commands beyond account linking.

## The web app side

The plugin does nothing on its own. Every button calls the Apollo web app, which stays the
single source of truth and enforces who may do what. The endpoints the plugin expects are
written up in [docs/API.md](docs/API.md). **They are a proposal**: the plugin was written
without access to the web app's code, so the web app needs these routes added (or this
client adjusted to match routes it already has) before the plugin works for real.

Until then, tick **Settings > Demo mode** to click through the whole plugin with made-up
data, viewing it as any staff rank.

## Alts

The plugin key identifies the employee, not the character, and it is saved per game
install. Any character on that PC acts as the main: clocking in on an alt clocks in the
main. The plugin tells the web app which character is logged in so tells reach the right one.

## Building

Needs the .NET 10 SDK and Dalamud's dev files (installed by XIVLauncher, or pointed to
with `DALAMUD_HOME`).

```bash
dotnet build -c Release
```

The installable zip lands in `ApolloNCC/bin/x64/Release/ApolloNCC/latest.zip`. Every push
also builds on GitHub Actions; the zip is in the run's `ApolloNCC` artifact.

To load it in game for testing: `/xlsettings` > Experimental > Dev Plugin Locations, add
the path to the built `ApolloNCC.dll`, then enable it under `/xlplugins` > Dev Tools.

## Layout

```
ApolloNCC/
  Plugin.cs            entry point, /apollo command, per-frame tick
  Configuration.cs     saved settings (key, address, alert switches)
  Api/                 web app contract: models, IApolloApi, HTTP client, demo stand-in
  Services/            state + polling, permissions, alerts, venue time rules, chat
  Windows/             main window, black and gold theme, one file per tab
docs/API.md            the endpoints the web app must provide
```
