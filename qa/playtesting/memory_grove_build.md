# Memory Grove playtest build (M1-23b)

The build for the USP playtest in `qa/test-plans/M1_benchmark_and_playtest.md` Part B. It runs the server and the client in one
process. The server listens on localhost only, so Windows shows no firewall prompt. Every session starts with a fresh grove.

## Start
```
Godot --path project-sumoner-worl -- --playtest
```
The world is kept in `user://playtest/` and deleted at the next start. On Windows that folder is
`%APPDATA%\Godot\app_userdata\Sumoner World\playtest`.

A packaged build for testers (a Godot export, so testers need no editor) and its distribution follow before the pilot. The
moderator script, consent form, questionnaire, coding guide and the report tool are in `pilot_kit/`.

## The grove
14 creatures within 60 m of the start. The layout and personalities are seeded, so every session starts the same.

| Creatures | Where | Note |
|---|---|---|
| 9 mossbacks (grazer herd) | herd 30 m north-east | one calf **snared** 12 m straight ahead |
| 4 bogfoxes (small predator pack) | pack 35 m west | one **snared** 25 m east |
| 1 hollow owl (solitary) | 40 m south | for now it looks like a small grazer (no avian model yet) |

## Controls (what the tester is told: only the keys)
| Key | Does | What the creature remembers about the player (the tester is never told) |
|---|---|---|
| W A S D | walk (relative to the camera) | |
| right mouse button + mouse, wheel | turn the camera around the character, zoom (3–25 m) | |
| E | feed the creature in reach | `FedBy`: trust grows. After about 8 meals it shows the Trust emote |
| F | strike at it with the basic attack (combat spec, M2-01: damage until its health runs out) | `AttackedBy`: fear, or anger in bold creatures. A timid creature low on health yields: left alone for 5 s it remembers `DefeatedButSpared`; hit again, `Mistreated`. On a kill, the same species within 30 m remember `KilledKin` (core) and tell their group |
| R | free it from a snare | `SavedLifeOf` (core) |
| P | offer a Pact | `PactOffered` / `PactSealed` / `PactRefused` |
| Q | shout | predators within 12 m remember `AttackedBy` and are frightened |

"In reach" means the nearest creature within 5 m. A line at the bottom names it and lists the keys. After each action a line says
only what the player did and saw ("You feed the mossback."), never what the creature thinks. A test checks the wording.
**No memory UI** is shown (QA plan B2, DEC-002). The dev overlay and AI Inspector exist only in debug builds.

## Self-test
`--playtest --self-test --screenshot=file.png` walks to the snared calf, frees it, feeds it and shouts, using in-engine key events,
then turns the camera a quarter round with a right-button drag. It logs each line and saves a frame. Last run on the dev PC (2026-10-21): calf in reach after 1.2 s; "You free the mossback from
the snare." / "You feed the mossback." / "You shout. Nothing answers."

## Telemetry (QA plan B6, M1-23c)
Every session writes one anonymous JSON-lines file to `user://telemetry/session-<UTC time>-<id>.jsonl`. On Windows that is
`%APPDATA%\Godot\app_userdata\Sumoner World\telemetry`. The file holds no account, name or machine data, only a random
session id. Each line has `t` (seconds since the start) and `type`:

| type | fields |
|---|---|
| `session_start` | `session`, `build`, `creatures` |
| `player_action` | `action` (feed, attack, help, pact, scare), `target_species`, `affected` |
| `memory_formed` | `event_type`, `salience`, `core`, `species` |
| `emote_shown` | `emote`, `in_view` (inside the tester's camera view), `distance`, `species` |
| `memory_behaviour_shown` | the same plus `memory_type` and `rumour`, when the creature remembers the player and is looking at them |
| `session_end` | `duration` and a count of each line type |

Self-test session from the dev PC (2026-10-21), excerpt:
```
{"t":10.37,"type":"player_action","action":"help","target_species":"mossback","affected":1}
{"t":10.37,"type":"memory_formed","event_type":"SavedLifeOf","salience":900,"core":true,"species":"mossback"}
{"t":10.43,"type":"memory_behaviour_shown","emote":"affection","in_view":true,"distance":5.2,"species":"mossback","memory_type":"SavedLifeOf","rumour":false}
{"t":12.6,"type":"memory_behaviour_shown","emote":"curiosity","in_view":true,"distance":6.2,"species":"mossback","memory_type":"SavedLifeOf","rumour":true}
```
The rescued calf showed Affection 0.06 s after it was freed. Two seconds later a herd-mate that had only heard about the rescue
(a rumour, at half strength) came closer, curious.

## Not in this build yet
- Avian and real creature models.
