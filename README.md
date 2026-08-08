# Sonic Snow

An AR snowboarding race. You ride a real slope while a beam of light marks the finish
line and domes mark the checkpoints, all anchored to real-world GPS coordinates. You
get timed, and your time goes on a shared leaderboard.

Built in Unity 6000.4.8f1 for Android / ARCore. The phone supplies GPS and tracking;
the route lives in Firebase so it can be changed without rebuilding the app.

---

## How placement works

This is the part worth understanding, because almost every "I can't see anything"
problem traces back to it.

ARCore puts world `(0,0,0)` wherever the phone was when tracking started, with `+Z`
pointing wherever it happened to be facing. Neither has any relationship to the route
or to true north, so the offset between the two frames has to be *measured*.

`GeoAnchor` does that two ways:

1. **The launch ritual (immediate).** You stand on the start line and point the phone
   at the finish while the app boots. That pins both unknowns at once — your position
   is the route origin, and the direction you're facing is the route bearing. It works
   with no GPS fix and no movement, so the beam is up before the race starts.
2. **The motion fit (better, but needs distance).** Every GPS fix gives the same point
   in two frames — ENU metres from the origin, and Unity camera position. Fitting a
   rigid transform between the two sets is closed-form, and re-running it on every fix
   also absorbs tracking drift. It needs about 20 m of travel before it beats the ritual.

**The ritual is only as good as you make it.** Point the phone at the finish and hold
still until the calibration screen clears. If you are 90° off, the beam is 90° off.

---

## Setting a route

Use the route editor — a map where you drop pins and push them to Firebase:

- **Hosted:** <https://tamari-gray.github.io/sonic-snow/> (needs GitHub Pages enabled
  on the `gh-pages` branch — Settings → Pages)
- **Local:** open `Tools/route-editor.html` in any browser

It works well on a phone, which is the point: walk the route and drop pins where you
stand.

1. **Locate me** centres the map on you.
2. Pick **Start line**, tap the map. Pick **Finish line**, tap again. Optionally
   **Add checkpoints** — that mode stays armed so you can tap several.
3. Drag any pin to nudge it. Reorder or delete checkpoints in the list.
4. **Init map** pushes it.

The push is a `PATCH` to the database root, so the leaderboard is left untouched. The
existing route is overwritten.

### Altitude

Leave it **off** for anything flat. The elevation API samples a ~30 m grid and rounds
to whole metres, so on a street the rounding is larger than the real relief and you are
feeding the game noise — which plants the finish beam in the air. **Check elevations**
tells you whether there is enough drop to be worth using.

### Route length

Keep start and finish at least ~25 m apart. The game's start and finish radii are 10 m
each, so on a shorter route the finish triggers the moment the countdown ends.

---

## Running a race

1. Stand on the start line, point the phone at the finish, and launch the app.
2. The calibration screen holds you there until four things are true — AR tracking,
   holding steady, route data loaded, and a GPS fix good to ±15 m. It shows which are
   still pending, and gives up and proceeds after 45 s rather than trapping you.
3. Once it clears, walk into the start gate. Within 10 m (and with a fix better than
   ±20 m) the world spawns: finish beam, checkpoint domes.
4. Enter a username, press **Play**, wait out the countdown.
5. Ride to the finish. Within 10 m of the finish coordinate the timer stops and your
   time is submitted.
6. The leaderboard reappears and the app returns to searching for the start.

Timing is decided by GPS, never by the visual beam. The beam is an estimate that
converges during the run; GPS is the source of truth.

---

## Troubleshooting

The on-screen log (top right) prints a status line once a second with everything needed
to diagnose a failed start:

```
Start line 34.2m away (need <10m) | GPS ±14.1m (need <20m) | anchor launch seed (2 samples, 8m/20m baseline)
```

| Symptom | Likely cause |
| --- | --- |
| Stuck on the calibration screen | Read the checklist — it names the failing condition. GPS is usually the slow one. |
| Never spawns, log shows distance | You are not inside the 10 m start radius yet. |
| Never spawns, log shows GPS ±X | Fix is too rough. Wait, or move away from buildings. |
| **Spawns but nothing is visible** | **Almost always the beam is behind you.** Check the route bearing in the editor, then turn to face it. If the ritual was off by 180°, so is the beam. |
| Beam floats above the ground | Altitudes are wrong. Zero them unless the route has real vertical drop. |
| Beam drifts during the run | Normal until the motion fit engages. It needs ~20 m of travel *and* fixes better than ±8 m — on a street it may never engage, leaving you on the launch seed the whole way. |

Anchor status in the log means: `unaligned` → nothing placed yet; `launch seed` → using
the ritual, only as good as your aim; `fitted conf 0.xx` → motion fit has taken over.

---

## Firebase

Realtime Database at `sonicar-7ea55`. The route is the root object; the leaderboard is a
sibling key of name → seconds.

```json
{
  "originLat": -45.0287, "originLng": 168.68715, "originAlt": 375,
  "finishLat": -45.02906, "finishLng": 168.6867, "finishAlt": 375,
  "checkpoints": [{ "lat": -45.0288, "lng": 168.687, "alt": 375 }],
  "leaderboard": { "tam": 205 }
}
```

`originAlt`/`finishAlt` of `0` mean "no surveyed altitude", and the game flattens
everything to start-gate height. Firebase drops empty arrays, so a route with no
checkpoints simply has no `checkpoints` key.

Rules are open for prototyping. Validation rules that keep the shape sane without
adding auth are worth pasting in — particularly requiring `leaderboard/$player` to be a
number, which rejects a name containing `/` writing a nested path.

---

## Project layout

| Path | What it is |
| --- | --- |
| `Assets/Scenes/Game.unity` | The only scene that matters |
| `Assets/Scripts/GeoAnchor.cs` | GPS ↔ AR alignment. The core of the whole thing |
| `Assets/Scripts/GameLogic.cs` | State machine: searching → init → racing → finished |
| `Assets/Scripts/CalibrationScreen.cs` | Startup gate that enforces the launch ritual |
| `Assets/Scripts/FinishLinePillar.cs` | Spawns the beam |
| `Assets/Scripts/CheckpointDomeSpawner.cs` | Spawns the domes |
| `Assets/Scripts/GpsUtils.cs` | Geodesy — ENU, haversine, bearing |
| `Assets/Scripts/LocationHandler.cs` | Owns `Input.location`, retries on failure |
| `Assets/Editor/CalibrationScreenSetup.cs` | Builds and wires the calibration UI |
| `Tools/route-editor.html` | The route editor |

Everything geo-placed parents to `GeoAnchor.Root` with its ENU vector as
`localPosition`. Never `Instantiate` into world space with an ENU vector — the XR rig is
never moved, so a calibration update slides the world rather than teleporting the player.

### Editor tooling

**Sonic Snow → Set Up Calibration Screen** rebuilds the calibration UI under the scene's
Canvas and wires all four serialized references. Safe to re-run; it re-wires rather than
duplicating.
