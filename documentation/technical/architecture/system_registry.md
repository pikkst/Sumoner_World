# Sumoner World - System Registry
Version: **0.1** (M0-06) · Date: 2026-10-19 · Owner: Mechanics Developer · Status: **Delivered, review pending (stakeholder)**
Rules: R02, R07, R12, R47, R49 · Related: ADR-001 (layers), TDD v0.1, AI Bible v0.1

> **One responsibility, one system, one interface.** Before you add a manager, service, utility or system, find it here (R47). If it exists, extend it. If it does not, add a row in the same PR as the code.
> **Enforced:** `ArchitectureRegistryTests` fails the build if a public interface in `SumonerWorld.Core` is not listed in this file.

## How to read this file
- **Layer:** `Core` (pure C#, no Godot), `Net`, `Persistence`, `Game` (Godot). Dependencies point down only (ADR-001).
- **Interface:** the single public contract for the system. Implementations may change; the interface name is reserved.
- **Status:** `Implemented`, `Planned (Mx)` = milestone it is first needed in.
- **Spec:** where the behaviour is defined. Code must match the spec (R50).

---

## 1. Core: foundation
| System | Namespace | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|---|
| Random numbers | `Core.Randomness` | `IRandom` | Seeded, forkable deterministic RNG streams | ADR-001, TDD §2.3 | **Implemented** |
| Simulation clock | `Core.Simulation` | `ISimulationClock` | World tick, in-game time of day and day count (`SimulationClock`, `FixedStepAccumulator`) | TDD §2.1 | **Implemented** (M1-01) |
| Tick schedule | `Core.Simulation` | `ISimulationSystem` | One stage of the fixed 13-step tick order (run by `SimulationScheduler`) | TDD §2.2 | **Implemented** (M1-01) |
| ECS facade | `Core.Ecs` | `IEcsWorld` | Entities, components, views (in-house sparse set, `EcsWorld`) | TDD §2.2, ADR-002 | **Implemented** (M1-02b) |
| Stable ids | `Core.Ecs` | `IStableIdAllocator` | Server-generated 64-bit ids for persistent things (`StableIdAllocator`) | TDD §2.2 | **Implemented** (M1-02b) |
| Event buffers | `Core.Events` | `IEventBus` | Per-tick typed event buffers between systems (R10) (`EventBus`) | TDD §2.2 | **Implemented** (M1-01) |
| Async jobs | `Core.Jobs` | `IJobScheduler` | Off-tick jobs, results applied at step 2 in job-id order (`JobScheduler`, `JobApplySystem`); same result with 0 or N workers | TDD §2.5 | **Implemented** (M1-12) |
| Job contract | `Core.Jobs` | `IJob` | `Execute` off-thread on job-owned data, `Apply` on the simulation thread; reusable (no allocation per submit) | TDD §2.5 | **Implemented** (M1-12) |
| Spatial index | `Core.Spatial` | `ISpatialIndex` | Uniform grid over the world bounds for perception, proximity, targeting, interest (`SpatialGrid`) | TDD §3.2, AI Bible §9.3 | **Implemented** (M1-03) |
| Physics port | `Core.Physics` | `IPhysicsQueries` | Ray cast, sweep, overlap (implemented by Godot adapter) | TDD §2.4 | Planned (M1) |
| Navigation port | `Core.Navigation` | `INavigation` | Async path requests (implemented by Godot adapter) | TDD §2.4 | Planned (M1) |
| Game data | `Core.Data` | `IDataRegistry` | Loads and validates versioned data files; read-only lookups (`DataRegistryLoader`, `DataRegistry`) | TDD §7, AI Bible §12 | **Implemented** (M1-04): memory event types, species |
| Logging | `Core.Diagnostics` | `ILog` | Structured log sink (implemented in Game as JSON lines) | TDD §6 | **Implemented** (M1-14) |
| Metrics | `Core.Diagnostics` | `IMetrics` | Tick/stage boundaries from the scheduler; timing done by the implementation outside Core | TDD §6 | **Implemented** (M1-05) |
| State hash | `Core.Diagnostics` | `StateHash` (struct) | FNV-1a 64 hash of deterministic state for determinism checks and replays | TDD §2.3 | **Implemented** (M1-05) |
| Server rules | `Core.Rules` | `IServerRules` | Server preset values (Normal / Hardcore / Custom) | GDD §9 | Planned (M1) |

## 2. Core: player actions and authority
| System | Namespace | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|---|
| Intent validation | `Core.Transactions` | `ITransaction` (`Validate`) | Authorize an intent (owner, range, cooldown, cost) without changing state; the first phase of every transaction, so validation and commit can never disagree (`CooldownTracker` in `Core.Intents`). Was planned as `IIntentValidator`; merged into `ITransaction` (rule 4) | TDD §3.2–3.3 | **Implemented** (M1-15b) |
| Intent dedupe | `Core.Intents` | `IIntentDeduplicator` | `IntentId` window (last 1,024 per session, idle sessions pruned after 10 min); repeated ids return the first result, lower unseen ids are stale and never applied (`IntentDeduplicator`) | TDD §3.4 | **Implemented** (M1-15b) |
| Transactions | `Core.Transactions` | `ITransactionProcessor` | **The only path** for atomic multi-entity changes: trades, crafting, pacts, ownership, rewards. Dedupe → validate → commit → record (`TransactionProcessor`; DB transaction from M1-17). First transaction: `OfferPactTransaction` (Core.World) | TDD §3.4 | **Implemented** (M1-15b) |
| Movement | `Core.Movement` | `IMovementModel` | Character movement shared by server and client prediction: one `MoveInput` per tick, diagonals not faster, clamped to bounds (`MovementModel`); terrain and collision with M1-20 | TDD §3.2 | **Implemented** (M1-15b) |

## 3. Core: gameplay
| System | Namespace | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|---|
| Stats | `Core.Stats` | `IStatModel` | Attributes (Vitality, Stamina, Focus, Will) and derived stats | GDD §5 | Planned (M1) |
| Combat | `Core.Combat` | `ICombatResolver` | Hits, damage, block/parry, stamina cost (shared contract, R28) | GDD §3 | Planned (M1) |
| Status effects | `Core.Combat` | `IStatusEffectSystem` | Timed effects (burn, freeze, stun) | GDD §3–4 | Planned (M2) |
| Magic | `Core.Magic` | `ISpellCatalog` | Rune-built spells, schools, Resonance | GDD §4 | Planned (M2) |
| Progression | `Core.Progression` | `IProgressionRules` | Talents, character advancement | GDD §5 | Planned (M2) |
| Items | `Core.Items` | `IItemCatalog` | Item definitions (data) | GDD §6 | Planned (M1) |
| Inventory | `Core.Items` | `IInventoryService` | Inventories and equipment; changes go through `ITransactionProcessor` | GDD §6, TDD §3.4 | Planned (M1) |
| Crafting | `Core.Crafting` | `ICraftingRules` | Recipes and crafting validation | GDD §6 | Planned (M2) |
| Trading | `Core.Economy` | `ITradeService` | Two-phase player trades and market stalls (uses `ITransactionProcessor`) | GDD §8, TDD §3.4 | Planned (M2) |
| Structures | `Core.Building` | `IStructureRules` | Sanctum building, placement, ownership | GDD §6 | Planned (M2) |
| Death | `Core.Rules` | `IDeathRules` | Player death, graves, creature death per preset | GDD §9 | Planned (M2) |

## 4. Core: creatures and world (AI Bible)
| System | Namespace | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|---|
| Perception | `Core.Cognition.Perception` | `IPerception` | Sight (view cone) and hearing via `ISpatialIndex`, nearest-first percepts, focus selection (`Perceiver`); scent and occlusion with M1-20 | AI Bible §2 | **Implemented** (M1-10) |
| Subject resolution | `Core.Cognition.Perception` | `ISubjectResolver` | Maps a perceived entity to the memory subject (player account / creature stable id); implemented by the host | AI Bible §4.1 | **Implemented** (M1-10, host side) |
| Creature minds | `Core.Cognition.Memory` | `ICreatureMindStore` | Saved mind state (bounded, pooled): personalities, memories, opinion baselines, needs: `CreatureMindStore`, `MemoryDecay`, `MemoryRules`, `Personality` | AI Bible §3–6 | **Implemented** (M1-06, M1-07) |
| Opinions | `Core.Cognition.Opinions` | `IOpinionModel` | Trust / Fear / Affection / Respect from memories + baseline (`OpinionModel`) | AI Bible §5 | **Implemented** (M1-07) |
| Appraisal | `Core.Cognition.Appraisal` | `IAppraisal` | Short-term emotions and emote selection (`AppraisalModel`); needs live in the mind store | AI Bible §6 | **Implemented** (M1-07) |
| Goal selection | `Core.Cognition.Decision` | `IUtilityEvaluator` | Utility scoring and goal selection (`UtilityEvaluator`, `ResponseCurve`, `GoalSet`) | AI Bible §7.1–7.2 | **Implemented** (M1-08) |
| Behaviour trees | `Core.Cognition.Behaviour` | `IBehaviourTreeRunner` | Data-defined trees that emit intents (`BehaviourTreeRunner`, `CreatureIntent`) | AI Bible §7.3 | **Implemented** (M1-09) |
| Cognition inputs | `Core.Cognition.Decision` | `InputSampler` (class) | The one place that reads needs, emotions, opinion, distance, health, time as 0–1000 inputs; shared by utility AI and tree conditions | AI Bible §7.2 | **Implemented** (M1-09) |
| Learning | `Core.Cognition.Learning` | `ILearningModel` | Tactic, habit and combo tables | AI Bible §8 | Planned (M2) |
| AI LOD | `Core.Cognition.Lod` | `IAiLodPolicy` | Tier assignment with hysteresis and per-tier time slicing (`AiLodPolicy`, `AiLodSettings`) | AI Bible §9 | **Implemented** (M1-10) |
| Bonding rules | `Core.Bonding` | `IBondingRules` | Ownership states and transitions, terms, leaving, bond levels, summoning (Bonding Contract v1.0.0, `BondingRules`) | AI Bible §10 | **Implemented** (M1-11) |
| Pact acceptance | `Core.Bonding` | `IPactEvaluator` | Pact Terms and acceptance score with full breakdown (`PactEvaluator`) | AI Bible §10.2 | **Implemented** (M1-11) |
| Obedience | `Core.Bonding` | `IObedienceModel` | Obey / Improvise / Hesitate / Refuse (`ObedienceModel`) | AI Bible §7.4 | **Implemented** (M1-11) |
| Population | `Core.Ecosystem` | `IPopulationModel` | T3 abstract populations, territory, migration | AI Bible §9.4 | Planned (M2) |
| Reputation | `Core.Ecosystem` | `IReputationService` | Server-wide (species, region, player) reputation from data `reputationDelta`, summed by a background job; seeds new creatures' opinions (`ReputationService`) | AI Bible §4.5 | **Implemented** (M1-12) |
| Rumours | `Core.Cognition.Memory` | `ICreatureMindStore.ShareRumours` (method) | Groupmates pass on their 3 strongest first-hand memories about players at half strength; never passed on again | AI Bible §4.5 | **Implemented** (M1-12) |
| Creature world | `Core.World` | `CreatureWorld` (class) | Composition root for one region's creature simulation: ECS components, spawn/despawn of creatures and player avatars, production creature systems in tick order, state hash | TDD §2.2 | **Implemented** (M1-15a) |
| Chunk map | `Core.World` | `IChunkMap` | 64 m chunk states: loaded / abstract | TDD §5 | Planned (M1) |
| World generation | `Core.World` | `IWorldGenerator` | Seeded scatter of resources and props | TDD §5.1 | Planned (M2) |
| Weather | `Core.World` | `IWeatherModel` | Deterministic weather and world events | GDD §7 | Planned (M2) |

## 5. Net (`SumonerWorld.Net`, project created in M1)
| System | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|
| Transport | `ITransport` | Channels (unreliable / reliable ordered), connect, poll, disconnect; `LoopbackTransport` in-process (tests, bots, listen server); Godot ENet adapter in the Game project (M1-14); Steam sockets later | TDD §3.1 | **Implemented** (M1-13): interface + loopback |
| Transport events | `ITransportHandler` | Receives connected / disconnected / data from `ITransport.Poll` on the sim thread | TDD §3.1 | **Implemented** (M1-13) |
| Message codec | `IMessageCodec` | Versioned (header: protocol version + type; **protocol 2** since M1-15c), size-limited (1,200 B) encoding with `MessageWriter`/`MessageReader`; every read error is a `MalformedMessageException`; fuzz-tested. Hello/Welcome/Rejected, `InputBatch` (newest ≤ 8 inputs, redundant), `IntentRequest`/`IntentAck`, `Snapshot` parts | TDD §3.3 | **Implemented** (M1-13, M1-15c) |
| Sessions | `ServerEndpoint` / `ClientEndpoint` (planned interface `ISessionManager`) | Handshake Hello → Welcome / Rejected with random session token and a server-assigned session id (M1-14, M1-15c); 30 s linger and 2 min reconnect in M1-16 | TDD §3.5 | **Partly implemented** (M1-14) |
| Session handlers | `ISessionMessageHandler`, `IServerSessionHandler` | Where the endpoints hand over after the handshake: session opened/closed (server) and in-session messages (both sides) | TDD §3.2 | **Implemented** (M1-15c) |
| Authoritative server | `GameServer` (class, Net.Server) | Server loop: poll → tick (player inputs at stage 1 via `PlayerInputBuffer`, creature systems, intents through `ITransactionProcessor` at stage 9) → 20 Hz snapshots; avatars follow sessions | TDD §3.2 | **Implemented** (M1-15c) |
| Game client | `GameClient` (class, Net.Client) | Session, own-character prediction, redundant input batches, intents and their results, latest snapshot entities | TDD §3.2 | **Implemented** (M1-15c) |
| Authentication | `IAuthenticator` | `DevTokenAuthenticator` (constant-time secret check) on the dev server; Steam tickets later | TDD §3.1a, §3.3 | **Implemented** (M1-14, dev) |
| Host options | `HostOptions` (Net.Hosting) | Server/client/game mode from arguments and `SW_*` environment variables; unknown arguments are errors | TDD §3.1a | **Implemented** (M1-14) |
| Rate limiting | `IRateLimiter` | Token buckets per message type | TDD §3.3 | Planned (M1) |
| Interest | `IInterestManager` | Relevance radius and priority per client | TDD §3.2 | **Partly implemented** (M1-15c: 150 m radius in `SnapshotSender`; priority accumulator with M1-18) |
| Replication | `ISnapshotBuilder` | 20 Hz snapshots (own avatar exact, others quantized to 1 cm, split into parts) | TDD §3.2 | **Partly implemented** (M1-15c: full snapshots; delta compression with M1-18) |
| Prediction | `IClientPrediction` | Own-character prediction and reconciliation: replay unacked inputs from the server position; ≤ 0.5 m corrected at once, 0.5–3 m smoothed over 100 ms, > 3 m teleport (`ClientPrediction`) | TDD §3.2 | **Implemented** (M1-15c) |
| Lag compensation | `ILagCompensation` | Hitbox rewind up to 200 ms | TDD §3.2 | Planned (M2) |

## 6. Persistence (`SumonerWorld.Persistence`, project created in M1)
| System | Interface | Responsibility | Spec | Status |
|---|---|---|---|---|
| World store | `IWorldStore` | SQLite world database, atomic commits | TDD §4.1–4.2 | Planned (M1) |
| Record codec | `IRecordCodec` | MessagePack payloads with integer keys | TDD §4.1 | Planned (M1) |
| Migrations | `IMigration` | One schema step `From → To` (run by the store at start-up) | TDD §4.3 | Planned (M1) |
| Backups | `IBackupService` | Timed and pre-migration backups | TDD §4.2 | Planned (M1) |

## 7. Game (Godot project: presentation and adapters)
| System | Type / interface | Responsibility | Spec | Status |
|---|---|---|---|---|
| Hosts | `Bootstrap`, `ServerHost`, `ClientHost` | `Main.tscn` entry; headless server runs ENet + session handshake + 30 Hz Core tick; client smoke mode (`--exit-on-welcome`) | TDD §2.5 | **Implemented** (M1-14) |
| ENet adapter | `GodotENetTransport : ITransport` | Godot `ENetConnection` at packet level (channel 0 unreliable, 1 reliable) | TDD §3.1 | **Implemented** (M1-14) |
| Physics adapter | `GodotPhysicsQueries : IPhysicsQueries` | Jolt queries for Core | TDD §2.4 | Planned (M1) |
| Navigation adapter | `GodotNavigation : INavigation` | `NavigationServer3D` paths | TDD §2.4 | Planned (M1) |
| Logging adapter | `GodotLog : ILog` | JSON lines on stdout (collected by Docker) | TDD §6 | **Implemented** (M1-14) |
| Entity views | `EntityViewSync` | Spawns/pools Godot nodes from snapshots; views never own state | ADR-001, R38 | Planned (M1) |
| Input | `IInputSource` | Maps devices to inputs and intents | TDD §3.2 | Planned (M1) |
| Chunk streaming | `ChunkStreamer` | Client rings: full / medium / far | TDD §5.2 | Planned (M1) |
| Emotes | `IEmotePresenter` | Emote id → animation per body type | AI Bible §6.2 | Planned (M1) |
| UI | `IUiScreen` | Data-driven screens, Bond Lens | GDD UI, R37 | Planned (M2) |
| Localization | `ILocalizer` | Externalized strings (R39) | GDD UI | Planned (M2) |
| Audio | `IAudioCues` | Creature mood vocals, SFX events | GDD Audio | Planned (M2) |
| Dev tools | `DevOverlay`, `AiInspector` | Metrics overlay, creature mind inspector (dev builds only) | TDD §6, AI Bible §13 | Planned (M1) |

## 8. Tools and test harness
| Tool | Location | Responsibility | Spec | Status |
|---|---|---|---|---|
| Quality gates | `source/scripts/verify.sh`, CI | Format, analyzers, build, tests | Enforcement doc | **Implemented** |
| Architecture tests | `tests/.../Architecture` | Layering, namespaces, this registry | ADR-001 | **Implemented** |
| Benchmark harness | `tests/SumonerWorld.Benchmarks` | BenchmarkDotNet micro-benchmarks; `b5` mode writes per-tick/per-stage CSV | M0-07, ADR-002 | **Implemented** (M1-02, M1-05) |
| Scenarios | `tests/SumonerWorld.Scenarios` | Repeatable simulation scenarios shared by CI gates and benchmarks (B5 now; a thin layer on `CreatureWorld` since M1-15a) | M0-07 | **Implemented** (M1-05) |
| Bot clients | `tools/BotClient` | 16 simulated clients for net spike | TDD §8 | Planned (M1) |
| Save dump | `tools/SaveDump` | Record → JSON for bug reports and fixtures | TDD §4.1 | Planned (M1) |
| Dev server image | `source/server/Dockerfile`, `source/docker-compose.dev.yml`, CI `server-image.yml` | Local Docker headless server + smoke client | TDD §3.1a | **Implemented** (M1-14) |

---

## Rules for changing this file
1. New system → new row in the same PR, with layer, interface, spec link and status.
2. Status moves to **Implemented** in the PR that adds the interface and its tests.
3. Renaming or removing an interface needs a reason in the PR and an update to every spec that names it.
4. Two rows must never share a responsibility. If a review finds overlap, merge them before writing code.

## Changelog
- **0.1 (2026-10-19):** First version. 76 systems across Core, Net, Persistence, Game and tools.
