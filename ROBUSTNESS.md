# The resolution contract

The goal: installing MDSAPI keeps the mods that depend on it working across game updates.

This file is the rule every wrapper follows to get as close to that as is honest. It is
binding on stage 2 — a wrapper that resolves a game member by one hard-coded name is not
finished.

## What this can and cannot do

**Survivable** — these are most update breakage, and MDSAPI should absorb all of them:

- a member is renamed (`ChangeCashBalance` → `AdjustCashBalance`)
- a member changes shape (field → property, as `onPreLoad` already does between runtimes)
- a member gains or loses a parameter
- a type moves namespace
- a manager changes singleton base

**Not survivable, by anyone** — be honest about these rather than implying coverage:

- the member still exists and now *means* something different (same name, new behaviour)
- a value changes units or scale (cash in cents rather than units)
- **an enum's values are reordered or a value is inserted.** This one is the most dangerous
  in the list because it is silent and it corrupts saves. `EQuality.Premium` is 3 at
  0.4.7f12; insert one value above it and every stored 3 now means something else.
  Resolution by name does not help — the name still resolves, to a different number.
  Rule: never persist an enum's numeric value. Store the *name* and parse it back, and
  treat a name that no longer exists as missing data rather than defaulting silently
- anything looked up by scene path or prefab name. A renamed GameObject resolves to null
  and the only symptom is a feature quietly doing nothing
- a system is removed from the game outright
- the mod's design assumption changes (a mod about curfew, when curfew is reworked)
- IL2CPP positional names (`Method_Private_Void_0`) — unstable by construction, never
  build on them

So the promise is not "mods never break". It is: **a game update breaks MDSAPI, loudly, in
one place — rather than your mods, silently, in several.** That is worth far more than it
sounds, and claiming more than it would be a lie that costs trust the first time it fails.

## The rules

**1. Every game member resolves through candidates, never one name.**

```csharp
// Ordered: current name first, then names it has had or might plausibly take.
private static readonly MemberSpec Balance =
    MemberSpec.Property("Money.MoneyManager", "Balance", "CashBalance", "cashBalance");
```

The first candidate that exists wins. Order is current-first so the common path is one
lookup.

**2. Shape is discovered, not assumed.** Property, field, or method — try in that order and
record which matched. This is the rule that already makes the lifecycle hooks work on both
runtimes from one binary.

**3. Resolve once, cache, invalidate on scene unload.** Reflection per call is how an API
layer becomes the reason a mod runs at 30fps.

**4. A missing member disables one capability, never the API.** If `Money` cannot resolve,
`Money.IsAvailable` goes false and every other module carries on. A mod can check before it
relies on something. Nothing throws at startup because one wrapper did not bind.

**5. Say what happened, once, at startup.** One line listing modules that bound and modules
that did not. This is the line that turns "my mod stopped working" into a diagnosis:

```
MDSAPI 0.2.0 — 9 of 11 modules bound. Unavailable: Growing, Law (game 0.4.8f1 moved them).
```

**6. Diagnostics are a command, not just a log line.** The startup line says what bound;
a player reporting a bug three hours later has scrolled past it. Register a console command
(`ScheduleOne.Console.AddCommand` exists) that prints the current binding state per module
— what resolved, what fell back to a non-primary candidate, what is missing — in a form
that can be pasted into a bug report. Borrowed straight from how Polyfill exposes
`polyfilllist` / `polyfillexport`, and it is the difference between a useful report and
"it doesn't work".

**7. Version-keyed overrides ship as data, not code.** When a rename is known, it goes in a
table keyed by game version, so a compatibility fix is a patch release rather than a
rewrite. Borrowed from how Polyfill thinks, scoped to a surface we control.

**8. Never silently substitute.** If a candidate other than the first one matched, log it at
startup. A wrapper quietly binding to a fallback for six months, then breaking, is worse
than failing immediately — the cause is long out of view by then.

## What this is not

This is not Polyfill. Polyfill patches the *game* so already-compiled mods — anyone's —
keep running, backed by a compatibility index fed by player telemetry. That is a different
product with a much larger maintenance surface, and MDSAPI mods do not need it because they
never name a game type in the first place.

The two coexist fine. A player can run both.
