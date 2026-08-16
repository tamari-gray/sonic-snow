# Sonic Snow — User Flow

How the app behaves today, screen by screen, on the `xreal-glasses` branch. For route
setup, Firebase, and project layout see `README.md` — this file is just the runtime
flow: what the rider sees, in what order, and what has to be true for each step to
advance to the next.

---

## Current build scope

Two things are deliberately switched off right now, both toggled from a single field on
`GameLogic` in the Inspector (`Debug / Scope → Skip Username Entry`, default **on**):

- **Username entry is skipped.** The start gate goes straight into the countdown under
  the name `"Player"`, and the finish line returns straight to searching with **no
  Firebase submission** — so repeated test runs don't spam the leaderboard with
  placeholder entries.
- **The leaderboard screen is scoped down to just a distance-to-start readout.** No
  rider list, no ribbon, no header — it still shows at the same point in the flow, just
  stripped to the one number that's useful while iterating on the race mechanics.
- **Screen recording has been removed entirely** (it crashed on Android 14+'s
  foreground-service permission model and added more instability than it was worth).
  There is no recording of any kind in the current build.

The intent, in the user's own words, is to "focus purely on the game mechanics of
racing and collecting checkpoints and getting to the end" — everything below reflects
that.

Set `Skip Username Entry` to **off** to restore the original flow (typed name, real
leaderboard, Firebase submission on finish) — see [Toggling back](#toggling-back-to-the-full-flow)
at the bottom.

---

## The flow, step by step

### 1. Launch

`GameLogic.Start()` loads the route config from Firebase, then blocks on the
calibration screen before anything else runs. Nothing in the game — not the search for
the start line, not the countdown, not a checkpoint spawn — can happen before
calibration finishes. (This wasn't always true; see [The armed gate](#the-armed-gate)
below for why it matters.)

### 2. Calibration screen (blocking)

Stand on the start line, point the device at the finish, and hold still. The screen
tracks four conditions independently and shows which are still pending:

| Condition | Threshold |
| --- | --- |
| AR tracking | `ARSession.state == SessionTracking` |
| Holding steady | yaw drift ≤ 12°/s for 1.5s straight (own 12s timeout so a shaky hold can't hang forever) |
| Route data | Firebase config loaded |
| GPS fix | accuracy ≤ 15m |

Once all four are true, it holds the "Calibrated" confirmation up for 1.2s, latches the
launch pose (the position/heading everything else gets aligned against), and clears.

**Failsafe:** if it's still stuck after **45 seconds**, it proceeds anyway with a
warning logged — placement will be rough, and if GPS never arrives, the start line
won't trigger at all until one does. This exists so a bad fix or a denied permission
can't trap the app on this screen permanently.

### 3. Searching for start

The leaderboard appears (distance-to-start only, per the current scope) and the game
polls GPS distance to the route origin every frame. Nothing else is spawned yet.

### 4. Start gate

Triggers when **both** are true:

- within **10m** of the start coordinate
- GPS accuracy better than **20m**

At that instant: the vertical ground plane is pinned from the current camera height,
the checkpoint domes and finish beam spawn, and the leaderboard hides.

### 5. Countdown

With username entry skipped, this happens immediately — no typing, no Play button.
`CountdownTimer` runs three lights filling one at a time plus a big number: **3 → 2 →
1 → GO!**, one beat per second (~4s total, real wall-clock time, not tied to frame
rate). The race clock is still stopped during this.

*(If username entry is switched back on, a username panel appears here instead — see
[Toggling back](#toggling-back-to-the-full-flow).)*

### 6. Racing

On GO: the race clock starts, and the launch pose is marked "spent" (later GPS fits
stop trusting the ritual position and start trusting movement instead — the rider has
actually left the gate now). Each frame checks:

- **Checkpoints** — within **10m** of a checkpoint's real GPS coordinate retires its
  dome. Unordered: reaching checkpoint 2 first still collects checkpoint 2. Scoring is
  always against the real coordinate, never the dome's on-screen position, since the
  dome is a shifting estimate and the coordinate is ground truth.
- **Finish line** — within **10m** of the finish coordinate, with GPS accuracy better
  than **20m**, stops the clock and ends the race.

Timing is decided by GPS distance, never by whether the beam looks close — the beam is
a visual estimate that keeps refining during the run.

### 7. Finish

Race clock stops. With username entry skipped: **no submission**, straight back to
step 3 (searching for start) so the leaderboard is never polluted with placeholder
"Player" times. The user currently relaunches the app for every run rather than relying
on this loop — see [Known limitations](#known-limitations).

*(With username entry on, the run instead submits `{username: elapsedSeconds}` to
Firebase, then returns to searching.)*

---

## State machine

```
SearchingForStart ──(at start gate, good fix)──▶ PlayerInit ──(countdown ends)──▶ Racing ──(at finish, good fix)──▶ FinishedRace
        ▲                                                                                                                │
        └────────────────────────────────────(ReturnToSearching)───────────────────────────────────────────────────────┘
```

`GameLogic.CurrentState` is the single source of truth; `Update()` branches on it every
frame to decide which proximity checks are even live.

---

## The armed gate

Worth calling out on its own because it was a real bug, fixed this session.
`GameLogic.Start()` (a coroutine) waits for the route load and calibration before doing
anything — but `Update()` didn't wait for that same coroutine, and `CurrentState`
defaults to `SearchingForStart`. So if the app launched while already standing at the
gate with a usable fix, the start-line proximity check was live from frame one and the
race began **underneath the calibration screen** — countdown, race clock, domes and
pillar all firing while the screen was still telling the rider to hold still.

Fixed with an `armed` flag (`GameLogic.IsArmed`), set only once route load *and*
calibration both finish. `Update()` now returns immediately if `!armed`. Verified with
an offline flow test that reproduces the exact symptom when the gate is removed — see
below.

---

## Testing the flow without a hill

`Assets/Scripts/RaceFlowSelfTest.cs` + `Assets/Editor/RaceFlowTestBatch.cs` drive a
virtual rider down a synthetic route on mock GPS fixes and assert the whole sequence
above happens in order — inert unless `SONIC_SNOW_FLOW_TEST=1` is set, so it never runs
in a normal session or a device build:

```powershell
$env:SONIC_SNOW_FLOW_TEST = '1'
& 'C:\Program Files\Unity\Hub\Editor\6000.4.8f1\Editor\Unity.exe' -batchmode `
  -projectPath 'C:\Users\tamar\code\sonic-snow' `
  -executeMethod RaceFlowTestBatch.Run -logFile flowtest.log
```

Grep the log for `[FlowTest]`. It checks ordering and logs step timings, but does
**not** cover AR tracking, GeoAnchor's motion fit, rendering, or placement accuracy — a
pass means the flow is right, not that content lands in the right spot on the hill.

---

## Known limitations

- **The Beam Pro has no GNSS.** Every GPS-gated step above (calibration's fix
  requirement, the start gate, checkpoints, the finish line) needs a real fix to ever
  fire, and the glasses have no chip to produce one on their own. Field testing has
  relied on an external Bluetooth GPS bridge; this is a known, unresolved design
  question, not a bug — see the `xreal-port` memory note for the current status.
- **No in-app restart loop is really exercised yet.** Step 7 technically returns to
  step 3, but the user currently relaunches the whole app between runs rather than
  standing at a returned-to-searching screen and walking back to the gate — so that
  loop is logic-tested (via the flow test) but not field-tested.

---

## Toggling back to the full flow

Select the `GameLogic` component in the scene, and under **Debug / Scope**, uncheck
**Skip Username Entry**. That restores:

- The username panel at step 5 (retro on-screen keyboard input; also has its own idle-typing
  auto-start — pauses 1s after typing 2+ characters with nothing typed, then counts
  "3, 2, 1, GET READY" and presses Play automatically, since the Play button isn't
  reachable via touch on this canvas).
- A real Firebase submission at step 7, and the full rider-list leaderboard instead of
  the distance-only version (re-enabling the rider list itself requires restoring the
  `BuildRibbon`/`BuildHeader` calls in `RetroLeaderboardUI.cs`, which are currently
  present but unused, not deleted).
