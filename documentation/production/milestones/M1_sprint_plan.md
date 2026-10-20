# M1: Core Tech Prototype - risk burn-down (Sprint Plan)
Window: **2026-10-20 → 2027-03-31** (gate) · Owner: Producer Agent · Started: 2026-10-19 (stakeholder: "merge and start M1")
Velocity: solo-adjusted, about 1 task per 3–5 working days. 24 tasks over ~23 weeks, with the 6 weeks gained in M0 as buffer.

All tasks follow the Definition of Done (`development_rules_enforcement.md` §3). Each task is one PR (or a few small ones), reviewed by the stakeholder before merge (R41, R56).

## M1 goal
Prove the four things that can kill the project, before any content is built (R57):
1. **Creatures that remember** are readable to players (USP playtest ≥ 70%, M0-07 Part B).
2. **200 active creatures at 60 FPS** with AI ≤ 4 ms/tick (TDD §1, AI Bible §9).
3. **Server-authoritative multiplayer** with reconnect, no duplication, crash-safe saves (TDD §3–4).
4. **Deterministic, chunked open world** that streams without hitches (TDD §2.3, §5).

The gate on 2027-03-31 is **GO / RE-SCOPE**, using the evidence in M0-07 Part C.

---

## Phase A: Simulation foundation (weeks 1–3)
| Task | Delivers | Registry interfaces | Rules | Est |
|---|---|---|---|---|
| **M1-01** Simulation core | Fixed-step clock (30 Hz, in-game day), 13-stage tick schedule, per-tick event buffers, fixed-step accumulator for hosts | `ISimulationClock`, `ISimulationSystem`, `IEventBus` | R10, R11, R21, R48 | 2d |
| **M1-02** ECS spike → ADR-002 | Arch vs. in-house sparse set behind `IEcsWorld`; 200-creature iteration benchmark; decision ADR | `IEcsWorld`, `IStableIdAllocator` | R03, R23, R47 | 3d |
| **M1-03** Spatial index | Hash grid (16 m cells), radius/cone queries into caller buffers, 0 allocations | `ISpatialIndex` | R19, R21 | 2d |
| **M1-04** Game data registry | Versioned JSON data loading + validation (species, memory event types, goals) | `IDataRegistry` | R08, R48 | 3d |
| **M1-05** Sim-only benchmark B5 in CI | Allocation gate (0 B/tick), determinism hash test, stage timers | `IMetrics` (`ILog` moved to M1-14) | R11, R22, R23 | 2d |

## Phase B: Creature mind (weeks 3–9) - AI Bible v0.1
| Task | Delivers | Registry interfaces | Est |
|---|---|---|---|
| **M1-06** Memory store | 32-byte records, integer lazy decay (LUT), consolidation, capacity/eviction, core memories; property tests | `ICreatureMindStore` | 4d |
| **M1-07** Opinions, appraisal, emotes | Trust/Fear/Affection/Respect, emotions, needs, emote selection | `IOpinionModel`, `IAppraisal` | 3d |
| **M1-08** Utility decision | Goals, considerations, response curves from data, momentum, deterministic tie-break | `IUtilityEvaluator` | 3d |
| **M1-09** Behaviour trees | Data-defined, flyweight trees, blackboard, intents out | `IBehaviourTreeRunner` | 4d |
| **M1-10** Perception + AI LOD | Sight/hearing via spatial index, tiers T0–T3, time slicing | `IPerception`, `IAiLodPolicy` | 3d |
| **M1-11** Bonding Contract v1.0.0 | Ownership states, Pact evaluator, obedience responses; contract tests | `IBondingRules`, `IPactEvaluator`, `IObedienceModel` | 4d |
| **M1-12** Group memory | Rumour sharing, server reputation (async job) | `IReputationService`, `IJobScheduler` | 3d |

## Phase C: Server, network, persistence (weeks 7–14) - TDD §3–4, spikes 2, 3, 6
| Task | Delivers | Est |
|---|---|---|
| **M1-13** Net project | `SumonerWorld.Net` with layering tests; `ITransport` + loopback network, `IMessageCodec` (the Godot ENet adapter moves to M1-14, the Persistence project to M1-17 where it gets real content) | 3d |
| **M1-14** Headless server + Docker dev server | `ServerHost`/`ClientHost`, Dockerfile + `docker-compose.dev.yml` (spike 6) | 3d |
| **M1-15** Authoritative loop | Intent validation, `IntentId` dedupe, transaction processor, movement prediction/reconciliation, 2 clients | 5d |
| **M1-16** Sessions | Join, 30 s linger, 2 min reconnect, dev-token auth | 3d |
| **M1-17** World store | SQLite + MessagePack + migrations + backups; crash-during-1,000-trades test (spike 3) | 5d |
| **M1-18** Bots + B2 | 16 bot clients, `tc netem` 150 ms / 2% in Docker, bandwidth metrics (spike 2) | 3d |

## Phase D: World and presentation (weeks 12–17) - spikes 4, 5
| Task | Delivers | Est |
|---|---|---|
| **M1-19** Terrain + streaming | Terrain3D spike (fallback: in-house), 8×8-chunk test region, client streaming rings; B3 (spike 4) | 5d |
| **M1-20** Godot adapters | Jolt physics queries, navigation, `GodotLog` | 3d |
| **M1-21** Creature presentation | Entity views, `IEmotePresenter`, grazer + predator placeholder models with 6 emotes (style guide §4) | 6d |
| **M1-22** Dev tools | Dev overlay (stage timings), AI Inspector | 3d |

## Phase E: Gate evidence (weeks 17–23)
| Task | Delivers | Est |
|---|---|---|
| **M1-23** "Memory Grove" playtest | Playtest build (M0-07 B2), telemetry, pilot (Feb 2027), gate run ≥ 20 testers (Mar 2027), report | 8d + sessions |
| **M1-24** Gate benchmarks + report | B1–B5 on all hardware profiles, gate report with GO / RE-SCOPE recommendation | 4d |

---

## Dependencies
M1-01 → (M1-02, M1-03, M1-04) → M1-05 → Phase B. M1-13 can start after M1-02. M1-21 needs M1-07 (emotes) and M1-20. M1-23 needs Phases B–D.

## Risks watched in M1
| Risk | Signal | Response |
|---|---|---|
| AI budget > 4 ms/tick | B5 trend after M1-10 | Lower T1/T2 rates, reduce goal set, profile before optimizing (R23) |
| Solo velocity | > 2 weeks behind at end of Phase B | Cut M1-12 rumours to the gate minimum; keep the 6-week buffer for Phase E |
| Terrain3D unsuitable | spike 4 fails | In-house chunked heightmap (TDD §5.1 fallback) |
| USP not readable | pilot < 55% | Spend buffer on animation/staging before the gate run |

## Status
| Task | Status |
|---|---|
| M1-01 | **Done** 2026-10-19 (stakeholder approved; merged as PR #10) |
| M1-02 | **Done** 2026-10-19: ADR-002 accepted (in-house sparse-set ECS), PR #11 |
| M1-02b | **Done** 2026-10-19 (stakeholder approved, PR #12): `EcsWorld`, `StableIdAllocator`, 18 tests, benchmarks |
| M1-03 | **Done** 2026-10-19 (stakeholder approved, PR #13): `SpatialGrid`, 10 tests, perception benchmark |
| M1-04 | **Done** 2026-10-19 (stakeholder approved, PR #14): data registry, 18 memory event types, 3 placeholder species, 15 tests |
| M1-05 | **Done** 2026-10-19 (stakeholder approved, PR #15): B5 scenario, CI gates (0 B/tick, identical runs), `b5` CSV tool |
| M1-06 | **Done** 2026-10-20 (stakeholder approved, PR #16): memory store, decay, core memories, personality drift; in B5 gates |
| M1-07 | **Done** 2026-10-20 (stakeholder approved, PR #17): opinions, emotions, emotes, needs; in B5 gates |
| M1-08 | **Done** 2026-10-20 (stakeholder approved, PR #18): utility AI, goals data, interrupts; in B5 gates |
| M1-09 | **Done** 2026-10-20 (stakeholder approved, PR #19): behaviour trees, 10 trees in data, intents; in B5 gates |
| M1-10 | **Done** 2026-10-20 (merged under standing approval, PR #20): perception, focus, AI LOD; B5 has 2 stand-in players |
| M1-11 | **Done** 2026-10-20 (merged under standing approval, PR #21): Bonding Contract v1.0.0, Pact evaluator, obedience; B5 players offer Pacts |
| M1-12 | **Done** 2026-10-20 (merged under standing approval, PR #22): rumours, reputation, job scheduler; Phase B complete |
| M1-13 | **Done** 2026-10-20 (merged under standing approval, PR #23): Net project, transport + loopback, message codec |
| M1-14 | **Done** 2026-10-20 (merged under standing approval, PR #24): Docker dev server, ENet adapter, server/client hosts, handshake |
| M1-15a | **Done** 2026-10-20 (merged under standing approval): creature systems promoted to `Core.World.CreatureWorld`; B5 is a thin scenario, state hash unchanged |
| M1-15b | **Done** 2026-10-20 (merged under standing approval): movement model, IntentId dedupe, transaction processor, OfferPact transaction (Core) |
| M1-15c | **Done** 2026-10-20 (merged under standing approval): protocol 2, authoritative server loop, prediction + reconciliation, 2 clients over loopback, Godot hosts; M1-15 complete |
| M1-16 | **Done** 2026-10-20 (merged under standing approval): suspend/resume sessions (2 min), 30 s avatar linger, one session per account, intent resend on resume, client auto-reconnect |
| M1-17a | **Done** 2026-10-20 (merged under standing approval): Persistence project, SQLite world store, migrations, backups, record codec, fixture save; **spike 3 passed** |
| M1-17b | **Done** 2026-10-20 (commit 32da085, **pushed to `main` without a PR by mistake**; CI and the server-image workflow passed on the push; reported to the stakeholder): world save/load (players, creature minds and bonds, reputation), checkpoint thread, graceful shutdown; **spike 6 passed** |
| M1-17c | **Done** 2026-10-20 (merged under standing approval): transaction journal, acks after the durable commit, session ids unique across restarts; M1-17 complete |
| M1-18a | **Done** 2026-10-20 (merged under standing approval): `MeteredTransport` + `BandwidthMeter`, `BotFleet` with recorded inputs and the reconnect / duplicate checks; in-process B2 within budget (18.8 KiB/s per client); delta compression deferred with evidence |
| M1-18b | **Done** 2026-10-20 (merged under standing approval): B2 in Docker (ENet bots, `tc netem` 150 ms / 2 %, server 4 CPU / 8 GB), `ServerBenchmark`, CI job `b2`; **spike 2 passed** (run 1 of 3: 18.9 KiB/s per client, tick p99 2.55 ms); ENet peer-slot fix for fast resume. valid 3-run set (runs 2–4, build `0b1e1fa`) added the same day: median tick p99 2.92 ms, 18.9 KiB/s per client; M1-18 complete |
| M1-19a | **Done** 2026-10-20 (merged under standing approval): streaming planner, heightfield, chunk scatter (Core); Terrain3D 1.0.2 integration with fallback, chunk streamer, `--region` test region (8 × 8 and 4 km) |
| M1-19b | **Done** 2026-10-20 (merged under standing approval): B3 fly-route benchmark; 3 runs on 4 km: median p99 3.55 ms, 0 frames > 33 ms, 0 streaming hitches; **spike 4 passed on the dev PC** (indicative; one run had a non-reproducible 1 s stall burst, reported as measured); Terrain3D adopted; M1-19 complete |
| M1-20a | **Done** 2026-10-20 (merged under standing approval): `IPhysicsQueries` + `HeightfieldPhysics`, `INavigation` + `PathPlanner` (async, ordered), creatures follow paths, players and creatures walk on the shared heightfield |
| M1-20b | **Done** 2026-10-20 (merged under standing approval): Jolt enabled, `GodotPhysicsQueries`, `GodotNavigation` (mesh baked from the heightfield), server / client / bots on the shared ground; Docker self-check: 64 rays, 0 missed, largest difference to the heightfield 0.0 cm; M1-20 complete (`GodotLog` exists since M1-14) |
| M1-21a | **Done** 2026-10-20 (merged under standing approval): protocol 4 (species in snapshots), `RemoteInterpolation` (100 ms buffer, dead-zone clock, short extrapolation), dev server with grazers and predators |
| M1-21b | **Done** 2026-10-20 (merged under standing approval): `EntityViewSync`, `IEmotePresenter` + quadruped presenter, grazer and predator placeholder rigs with six emotes, emote showcase, client renders the region and server creatures with a follow camera; M1-21 complete |
| M1-22a | **Done** 2026-10-20 (merged under standing approval, PR #39): protocol 5 dev messages, server tick and stage metrics, read-only `CreatureInspector` (traits, needs, emotions, top memories, opinions, goal scores, running tree node), dev tools only with `--dev-tools` / `SW_DEV_TOOLS` (on in the Docker dev server); inspecting every creature every tick leaves the B5 state hash unchanged |
| M1-22b | **Done** 2026-10-20 (merged under standing approval): client dev overlay (F3: frame stats, server tick / stage / AI timings against budget, tiers, bandwidth) and AI Inspector panel (F4, click, Tab; ring marker), `--inspect=click|tab` self-test through real input events; usage in `documentation/technical/dev_tools/`; M1-22 complete |
| M1-23 … M1-24 | Planned |
