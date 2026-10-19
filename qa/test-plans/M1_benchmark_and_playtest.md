# Sumoner World - M1 Benchmark & Playtest Plan
Version: **0.1** (M0-07) · Date: 2026-10-19 · Owners: QA Agent (benchmark, protocol) + Data Scientist (metrics, analysis) · Status: **Delivered, review pending (stakeholder)**
Rules: R22, R23, R42, R43 · Related: TDD v0.1 (§1 budgets, §8 spikes), AI Bible v0.1 (§9 budgets, §13 tests), GDD success metrics

> This plan defines **how the M1 GO / RE-SCOPE gate (2027-03-31) is measured.** Every number the gate uses comes from a scenario in this document, run the way this document says. Results that were not produced this way do not count for the gate.

---

## Part A: Performance benchmark

### A1. Scenarios
All scenarios use a **fixed seed**, scripted input and a fixed duration, so two runs of the same build are comparable (R23).

| Id | Name | What runs | Duration | Proves |
|---|---|---|---|---|
| **B1** | Grove-200 | Client + local server, 200 creatures in an 8×8-chunk test region (512 m), 4 body types, herds and packs; a scripted camera path keeps the tier mix near **30 T0 / 70 T1 / 100 T2**; at tick 1,800 a predator pack attacks a herd (combat spike); at tick 3,600 the player commands two bonded creatures | 30 s warm-up + 5 min | Client 60 FPS with 200 creatures; AI ≤ 4 ms/tick (TDD §1.1, AI Bible §9.1) |
| **B2** | Server-16 | Headless server in the **local Docker dev container** (TDD §3.1a), 200 creatures + 16 bot clients replaying recorded inputs; network shaped to **150 ms RTT, 2% loss** with `tc netem` on the container interface | 30 s warm-up + 10 min | Server ≤ 19 ms/tick at ≤ 60% load; bandwidth budgets (TDD §2.1, §3.2) |
| **B3** | Stream-Run | Client flies a fixed route across the test region at sprint speed, then at 3× sprint | 5 min | No streaming hitch > 33 ms (TDD §1.3, §5) |
| **B4** | Client-60 | 60 creatures animated in view, emotes cycling, full lighting (fog, shadows, glow) | 30 s warm-up + 3 min | GPU/CPU frame budget (TDD §1.1) |
| **B5** | Sim-Only | Core simulation of B1 without Godot, run by the test harness (`tests/SumonerWorld.Scenarios`; **implemented M1-05** with stand-in systems until Phase B AI lands) | 9,000 ticks | Allocations/tick and determinism; runs in CI (`B5GateTests`); timings via `-- b5 out.csv` |

### A2. Metrics captured
| Metric | Source | Scenarios |
|---|---|---|
| Frame time p50 / p95 / p99 / max, % frames > 16.7 ms and > 33.3 ms | Godot `Performance` monitors, per frame | B1, B3, B4 |
| GPU frame time | Godot render-time monitors (GPU timestamps) | B1, B4 |
| Sim ms/tick p50 / p99, **per tick stage** (TDD §2.1) | `IMetrics` stage timers | B1, B2, B5 |
| AI ms/tick **per LOD tier**, creature count per tier | `IMetrics` | B1, B2, B5 |
| **Allocations/tick** on the sim thread (`GC.GetAllocatedBytesForCurrentThread` delta) and gen 0/1/2 collection counts | harness | B1, B2, B5 |
| Process RAM, VRAM | OS counters, Godot `RENDER_VIDEO_MEM_USED` | B1, B2, B4 |
| Bytes sent/received per client per second, snapshot size | `ITransport` counters | B2 |
| State hash per tick (deterministic domains, TDD §2.3) | debug build hash | B5 (and B2 replay) |

### A3. Pass thresholds (M1 gate)
| Check | Threshold | Scenario |
|---|---|---|
| Client frame rate, recommended spec | p99 ≤ 16.7 ms (60 FPS), ≤ 0.5% frames > 33.3 ms | B1, B4 |
| Client frame rate, minimum spec (Low + FSR) | p95 ≤ 16.7 ms, p99 ≤ 22 ms (floor 45 FPS in combat) | B1 |
| Creature AI | p99 ≤ 4.0 ms/tick for 200 creatures | B1, B2 |
| Server tick | p99 ≤ 19 ms, mean ≤ 14 ms | B2 |
| Allocations in steady state | **0 bytes/tick** on the sim thread after warm-up (B5 is a CI gate) | B5, B1 |
| Memory | client ≤ TDD §1.2 for the spec; server ≤ 4 GB | B1, B2 |
| Bandwidth | ≤ 64 KB/s per client average, ≤ 128 KB/s burst | B2 |
| Streaming | 0 frames > 33.3 ms caused by chunk loads | B3 |
| Determinism | two B5 runs, and 1 vs. N worker threads, give identical hashes on every tick | B5 |

A failed threshold is not "fixed" by changing the threshold. It goes to the gate as a finding, with a profile capture (R23, R46).

### A4. How to run (repeatability rules)
1. **Release export builds only** (not the editor). Build hash is recorded.
2. Machine prepared: high-performance power plan, latest stable driver recorded, no other heavy apps, 1920×1080 fullscreen, **V-Sync off**, frame cap off.
3. Each scenario runs **3 times**; the reported value is the **median of the three runs**. If run-to-run p99 differs by more than 10%, the set is invalid and is repeated.
4. Warm-up is excluded from every statistic.
5. The benchmark is started with a command-line flag (`--benchmark B1`, planned) so anyone can run it the same way, including outside testers on other hardware.
6. Output: one CSV per run in `qa/performance-logs/<yyyy-mm-dd>_<scenario>_<buildhash>_<machine>.csv` plus a `machine.json` (CPU, GPU, RAM, driver, OS). The Data Scientist produces the summary.

### A5. Hardware
| Profile | Target | Machine |
|---|---|---|
| Recommended | Ryzen 5 5600X / i5-12400, RTX 3060 / RX 6600 XT, 16 GB | **Open:** a second PC or volunteer testers running the `--benchmark` build. Dev-PC results count as indicative only. |
| Minimum | Ryzen 5 3600 / i5-10400, GTX 1660 Super / RX 5600 XT, 16 GB | **Dev PC** (`machines/dev-pc.json`): i7-8700K + RTX 2060 SUPER, 32 GB. CPU is minimum class, GPU slightly above minimum, so passing the minimum thresholds here is a fair test. |
| Server | 4 cores, 8 GB | Docker container limited with `--cpus=4 --memory=8g` on the dev PC |

### A6. Schedule
| When | What |
|---|---|
| From the first M1 sim build | B5 runs in CI on every PR (allocation and determinism gates); B5 timings are trend only (CI hardware varies) |
| Weekly during M1 | B1 + B2 on the dev PC; results appended to the trend sheet; a > 10% regression on any p99 opens a bug |
| 2027-03 (gate week) | Full set B1–B5 on every available hardware profile; gate report |

---

## Part B: USP playtest ("Does the creature remember me?")

### B1. Question and target
**Do players notice, without being told, that a creature remembers what they did, within 10 minutes?** Target: **≥ 70% of testers** (GDD success metric 1, M1 gate).

### B2. Test build: "Memory Grove"
A small M1 scenario (one biome patch, about 300 m across) built to give memory chances early, but never scripted to force them:
- 2 social species (a grazer herd, a curious small predator) and 1 solitary creature, about 15 creatures in total.
- Interactions available: feed, hit, help (a creature stuck in a snare), offer a simple Pact, scare away a predator.
- Expected memory moments (AI Bible §4.2, §6.2): a fed creature approaches next time (`FedBy` → `Trust` emote); a hit creature keeps its distance or growls (`AttackedBy` → `Fear`/`Grudge`); the herd moves away after the player kills one of them (`KilledKin` + rumours, §4.5); a rescued creature follows the player (`SavedLifeOf`).
- **No UI shows memories** in this build (the Bond Lens is off), so the test measures body-language readability only (DEC-002).

### B3. Participants
- **n ≥ 20** for the gate run, plus a **pilot of 3** to fix the protocol.
- Recruited from the core audience: 10+ hours in at least one of ARK, Valheim, Palworld, Enshrouded or a souls-like. Not developers, and not people who have read the design documents.
- Remote sessions are allowed (build key + screen recording).

### B4. Session protocol (≈ 30 min)
1. **Consent** (recording, anonymous telemetry, data deleted after 90 days).
2. **Neutral briefing**, read word for word: *"This is an early prototype. Explore and interact with the creatures however you like. Please think aloud if you can."* **Memory is never mentioned.**
3. **15 minutes of free play**, with screen, game audio and microphone recording. The moderator does not answer questions about creatures.
4. **Questionnaire**, in this order (unprompted before prompted):
   1. "Describe anything the creatures did that surprised you." (open)
   2. "Did any creature seem to treat you differently because of something you did earlier? If yes, describe it." (yes/no + open)
   3. "How much did you care about what happened to the creatures?" (1–7)
   4. "How much do you want to keep playing?" (1–7)
   5. "What was confusing?" (open)

### B5. Scoring
- A tester **noticed** creature memory if, in Q1 or Q2 (or in think-aloud), they describe a **specific** creature reaction and **correctly link it to their own earlier action**, and the recording shows that reaction happened **at or before 10:00** of play.
- **Time-to-notice** = first think-aloud mention, or the first clear change in the player's behaviour in response (from the video), whichever is earlier.
- Two raters code every session independently. Disagreements go to a third rater. Agreement must be **Cohen's κ ≥ 0.7**, otherwise the coding guide is fixed and sessions are re-coded.

### B6. Telemetry (dev builds only, anonymous)
Local JSON-lines file per session, no personal data:
`session_start`, `player_action {type: feed|attack|help|pact|scare, target_species}`, `memory_formed {event_type, salience}`, `memory_behaviour_shown {memory_type, emote, in_view, distance}`, `emote_shown {emote, in_view}`, `session_end`.
Used to check that memory moments **happened and were on screen**: if testers did not notice a behaviour that was never shown in view, the problem is staging, not readability.

### B7. Decision rule (Data Scientist)
| Result (gate run, n ≥ 20) | Meaning | Action |
|---|---|---|
| **≥ 70%** noticed (e.g. ≥ 14 of 20) | USP readable | GO on this criterion |
| 55–69% | Partly readable | Fix staging/animation from the findings, retest with ≥ 10 new testers before the gate |
| < 55% | Not readable | Gate finding: RE-SCOPE discussion on how memory is shown |
- The report includes the 95% Wilson interval (for 14/20 it is about 48–85%) so the stakeholder can see the uncertainty.
- Secondary results: median time-to-notice, % of memory moments that were in view, Q3/Q4 scores, top 5 confusion points.

### B8. Schedule
| When | What |
|---|---|
| 2027-02 | Pilot (3 testers); fix build staging, questionnaire and coding guide |
| 2027-03, weeks 1–3 | Gate run (≥ 20 testers) |
| 2027-03, week 4 | Coding, analysis, report in `qa/playtesting/M1_usp_report.md` |

---

## Part C: Gate summary (input to the stakeholder's GO / RE-SCOPE decision)
| M1 success criterion (project-config) | Evidence from this plan | Owner |
|---|---|---|
| 60 FPS with 200 active creatures on target hardware | A3: B1 + B4 on recommended (and minimum) spec | QA |
| Playtesters notice creature memory within 10 min | B7 result | Data Scientist |
| Server-authoritative loop with reconnect works | B2 + TDD §8 spikes 2, 3, 6 (reconnect, duplicate-intent and crash-during-trade tests, R43) | QA + Mechanics Developer |
| Deterministic seeded sim | A3 determinism row (B5) | Mechanics Developer |

## Open items
1. A recommended-profile machine (A5). The dev PC covers the minimum profile.
2. Recruitment channel for 20+ core-audience testers (Discord communities, Reddit r/survivalgaming, Steam playtest after the Steam page is live in M2; before that, direct invites).

## Changelog
- **0.1 (2026-10-19):** First version. Benchmark scenarios B1–B5 with metrics, thresholds and run rules; blind USP playtest protocol with scoring and decision rule; gate summary.
