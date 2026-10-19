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
| **M1-05** Sim-only benchmark B5 in CI | Allocation gate (0 B/tick), determinism hash test, stage timers | `IMetrics`, `ILog` | R11, R22, R23 | 2d |

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
| **M1-13** Net + Persistence projects | `SumonerWorld.Net`, `SumonerWorld.Persistence` with layering tests; `ITransport` (ENet), `IMessageCodec` | 3d |
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
| M1-04 | **Delivered** 2026-10-19, review pending: data registry, 18 memory event types, 3 placeholder species, 15 tests |
| M1-05 … M1-24 | Planned |
