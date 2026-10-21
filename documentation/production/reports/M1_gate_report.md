# M1 gate report: Core Tech Prototype
Date: 2026-10-21 · Author: Producer Agent (with QA) · Gate: **GO / RE-SCOPE** (planned 2027-03-31, taken early)
Decision: **GO, M1 closed 2026-10-21; M2 starts** (stakeholder, 2026-10-21: "close M1 now and start M2")

M1 had to prove the four things that could kill the project (M1 sprint plan). All planned work is done or deferred by the
stakeholder (DEC-003), 23 weeks ahead of the gate date. Evidence below follows QA plan Part C. Every number comes from the dev PC
(minimum-class machine, `qa/performance-logs/machines/dev-pc.json`) or CI.

## Gate criteria
| M1 success criterion | Evidence | Result |
|---|---|---|
| Server-authoritative loop with reconnect works | Authoritative server with intent validation, IntentId dedupe and transaction processor (M1-15); sessions with 2 min resume and auto-reconnect (M1-16); SQLite world store with journal, acks after durable commit and a crash-during-1,000-trades test (M1-17, **spike 3 passed**); Docker dev server survives restart (**spike 6 passed**, CI `server-image` job on every PR) | **Pass** |
| Network budget with 16 players | B2 in Docker, 16 bots at 150 ms RTT / 2 % loss: tick p99 2.92 ms median of 3 runs (budget 19 ms), creature AI p99 0.96 ms (budget 4 ms), 18.9 KiB/s per client (budget 64), 200 MiB server memory (**spike 2 passed**, `2026-10-20_M1-18_b2_server16_dev-pc.md`); short B2 runs in CI | **Pass** |
| Deterministic seeded sim | B5 in CI on every PR: identical state hash across runs, 0 B allocated per tick; the AI Inspector reading every creature every tick leaves the hash unchanged (M1-22a) | **Pass** |
| Chunked world streams without hitches | B3 on the 4 km region: 3 runs, median p99 3.55 ms, 0 frames > 33 ms, 0 streaming hitches (**spike 4 passed on the dev PC**, indicative: not a release export, one non-reproducible stall burst reported) | **Pass (indicative)** |
| 60 FPS with 200 active creatures on target hardware | B1/B4 on the recommended spec were not run: **not measured (DEC-003)**. Indicators only: server-side AI for 200 creatures fits the budget (B2, B5); B3 frame times on the minimum-class dev PC | **Not measured** |
| Playtesters notice creature memory within 10 min | The formal playtest was **deferred (DEC-003)**. Evidence that the behaviour exists and is staged: Memory Grove build, telemetry showing a rescued calf's Affection on screen 0.06 s after rescue and rumour-driven curiosity in its herd (M1-23c); pilot kit and report tool ready for a later public test (M1-23d) | **Not measured** |

## What M1 built (for M2 to stand on)
- **Creature mind** (AI Bible v0.1): memory store with decay and core memories, opinions, appraisal and emotes, utility AI,
  behaviour trees, perception and AI LOD, Bonding contract v1.0.0 (Pacts, obedience), group rumours and reputation.
- **Server and network:** in-house ECS, 30 Hz deterministic simulation, ENet transport, sessions, persistence with migrations and
  backups, bots and benchmarks, Docker dev server.
- **World and presentation:** Terrain3D streaming region, Jolt physics queries and navigation, creature views with six emotes,
  personal space, dev overlay and AI Inspector, camera-relative movement with an orbit camera.
- **Memory Grove:** five interactions (feed, hit, help, Pact, scare), playtest build, telemetry, family test package.
- **Quality:** 425 tests, analyzers as errors, format check, B2/B5 gates and the server image smoke test in CI.

## Risks carried into M2
| Risk | Why it is open | Plan |
|---|---|---|
| Client FPS with 200 creatures on target hardware unknown | No recommended-spec PC (DEC-003) | Watch the dev overlay during M2; measure B1/B4 before any public release |
| USP readability not measured with the target audience | Family testers only (DEC-003) | Family sessions give early signals; QA plan Part B before any public release |
| Placeholder art and the capsule player | M1 was a tech milestone | M2-04 player character; style guide shapes |
| Solo scope | M2 adds combat, companions, magic, crafting | M2 plan risk table: keep the loop whole and cut features |

## Recommendation
**GO.** Every technical risk that could be tested on the available hardware passed. The two unmeasured criteria were
deferred by the stakeholder's decision, not failed. They stay on the list for before any public release.

## Changelog
- **0.1 (2026-10-21):** M1-25. GO recorded from the stakeholder's decision of the same day.
