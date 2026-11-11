# Plugin API

What the ApolloNCC plugin expects from the Apollo web app. This is a proposed contract,
written without sight of the web app's code. Change either side freely, but keep
`ApolloNCC/Api/HttpApolloApi.cs` and `ApolloNCC/Api/Models.cs` in step with it.

## Basics

- Base path: `{web app address}/api/plugin/`. HTTPS only.
- JSON in and out, camelCase property names. Enums are camelCase strings.
- Times are ISO 8601 UTC (`2026-11-08T23:00:00Z`). Money is whole gil.
- Unknown properties are ignored by the plugin, so adding fields is safe.

### Authentication

Every request carries:

```
Authorization: Bearer <plugin key>
X-Apollo-Character: First Last@World
```

The **plugin key** is a long random secret an owner issues per employee from the web app.
It maps to exactly one employee (their main character). Store only a hash of it, let
owners revoke and reissue it, and answer `401` for unknown or revoked keys.

`X-Apollo-Character` is whichever character is logged in, which may be an alt. Everything
is done as the key's employee regardless. Record it as the employee's last seen character:
bookings hand it back so reception can `/tell` the right character.

### Authorization

**The web app must enforce every rule itself.** The plugin hides tabs a role should not
see, but that is tidiness, not security: anyone with a key can call these routes directly.

| Scope | Allowed |
| --- | --- |
| Own HR data, own bookings, blacklist | Any employee |
| Department staff, approve clock-ins, set collab | Manager / assistant manager of that department, owner, co-owner |
| Approve a non-dancer collab job | Owner, co-owner |
| Reception | Receptionist, Front of House manager / assistant manager, owner, co-owner |
| Security bookings, warnings, sightings | Bouncer, Security manager / assistant manager, owner, co-owner |

### Errors

Any non-2xx reply should be `{"error": "Sentence shown to the staff member as is."}`.
`401` means the key is bad (the plugin signs out); `403` means the role is not allowed.

## Objects

**Profile**: `employeeId`, `name` (main character), `rank` (`employee`,
`assistantManager`, `manager`, `coOwner`, `owner`), `department`, `jobs` (string array),
`discordLinked`, `discordName`.

The plugin matches these exact names: departments `Front of House` and `Security`; jobs
`Dancer`, `Receptionist` and `Bouncer`.

**Meta**: `jobs` (`[{name, basePay}]`), `rooms` (string array), `collabMultiplier` (1.5).

**EmployeeStatus**: `employeeId`, `name`, `rank`, `department`, `job`, `clockedIn`,
`clockInAt`, `clockInPendingApproval`, `pfClockedIn`, `pfClockInAt`, `pfPartyFinder`,
`pfShouts`, `hourlyPay` (rate in effect now, collab included), `pfHourlyPay`,
`hoursWorked`, `pfHoursWorked` and `owed` (completed shifts this pay period only; the
plugin adds the open shift live from `clockInAt`), `collab` (`{active, job, pendingJob}`).

**Booking**: `id`, `staffId`, `staffName`, `staffCharacter`, `staffWorld` (last seen
character), `clientName`, `clientWorld`, `bookedByName`, `room`, `amount`, `startsAt`,
`endsAt`.

**BlacklistEntry**: `id`, `name`, `world` (null = any world), `reason`, `addedBy`, `addedAt`.

**PatronWarning**: `id`, `name`, `world`, `reason`, `issuedBy`, `issuedAt`.

**Sighting**: `id`, `name`, `world`, `reason`, `reportedBy`, `seenAt`.

## Routes

| Method and path | Body | Returns | Notes |
| --- | --- | --- | --- |
| `GET me` | | Profile | Polled every 20 s, so role changes show up quickly. |
| `GET meta` | | Meta | |
| `GET hr/me` | | EmployeeStatus | |
| `POST hr/clock-in` | | EmployeeStatus | Always saves. On Sunday before 8:53 PM Eastern, set `clockInPendingApproval` and hold pay until a manager approves. |
| `POST hr/clock-out` | | EmployeeStatus | |
| `POST hr/pf/clock-in` | `{partyFinder, shouts}` | EmployeeStatus | Reject unless at least one is true. |
| `POST hr/pf/clock-out` | | EmployeeStatus | |
| `GET hr/staff` | | EmployeeStatus[] | Department for managers, everyone for owners. |
| `POST hr/staff/{id}/approve-clock-in` | | EmployeeStatus | |
| `POST hr/staff/{id}/collab` | `{active, job}` | EmployeeStatus | Pay becomes 1.5x the job's base pay (Dancer: 187,500). A job other than `Dancer` from a non-owner is stored as `collab.pendingJob`; pay stays on the current job until approved. |
| `POST hr/staff/{id}/collab/decision` | `{approve}` | EmployeeStatus | Owner / co-owner only. |
| `GET bookings/mine` | | Booking[] | The caller's bookings still running or just ended. |
| `GET reception` | | `{bookings, staff: [{employeeId, name, job, clockedIn}], totalPaid, receptionBonus, bonusRate}` | `receptionBonus` is 20% of bookings. |
| `POST bookings` | `{staffId, clientName, clientWorld, room, minutes, amount}` | Booking | Apply the same booking rules as the web app's own form. |
| `POST bookings/{id}/end` | | | |
| `GET security/blacklist` | | BlacklistEntry[] | Any employee. |
| `GET security/bookings` | | Booking[] | |
| `GET security/warnings` | | PatronWarning[] | Newest first. |
| `POST security/warnings` | `{name, world, reason}` | PatronWarning | |
| `GET security/sightings` | | Sighting[] | Recent ones, newest first. Polled every 20 s by security staff, which is how everyone gets alerted. |
| `POST security/sightings` | `{blacklistId, name, world}` | | Sent when a plugin sees a blacklisted player nearby. A good place to also ping Discord. |
| `POST discord/link-code` | | `{code, expiresAt}` | Short-lived code the employee gives to the Discord bot to tie their Discord account to their employee record. |
