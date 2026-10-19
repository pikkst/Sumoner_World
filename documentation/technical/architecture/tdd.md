# Sumoner World - Technical Design Document
Version: **0.1** (M0-05) · Date: 2026-10-19 · Owner: Mechanics Developer · Status: **Approved by stakeholder 2026-10-19** (with local Docker dev server, §3.1a)
Rules: R03, R11, R12, R17–R27, R30–R36, R48 · Related: ADR-001 (engine, layering), GDD v0.1, AI Bible v0.1, DEC-002

> The TDD fixes the technical budgets and contracts the M1 prototype must prove. Items marked **[M1 spike]** are the preferred choice but must pass a spike before they are locked; if a spike fails, the fallback listed is used and this document is updated (R50).

---

## 1. Target hardware (R22)
Godot 4.7 Forward+ renderer (Vulkan on Windows/Linux, D3D12 allowed). Targets are for **1920×1080**.

| | **Minimum** | **Recommended** | **Dedicated server (16 players)** |
|---|---|---|---|
| Quality / target | Low preset, FSR 2 Quality upscaling, **60 FPS** (floor 45 in heavy combat) | High preset, native 1080p, **stable 60 FPS** | 30 Hz tick at ≤ 60% load |
| CPU | 6 cores, 2019-class (Ryzen 5 3600 / Core i5-10400) | 6 cores, 2021-class (Ryzen 5 5600X / Core i5-12400) | 4 cores (2019-class), 1 core per tick thread + workers |
| GPU | 6 GB, Vulkan 1.2 (GTX 1660 Super / RX 5600 XT) | 8 GB (RTX 3060 / RX 6600 XT) | none (headless) |
| RAM | 16 GB | 16 GB | 8 GB |
| Storage | SSD, 20 GB | SSD, 20 GB | SSD, 10 GB + saves |
| OS | Windows 10/11 64-bit | Windows 10/11 64-bit | Windows or Linux x64 |
| Network | — | — | 50 Mbit/s upload |

- Steam Deck is a **stretch target after EA** (30 FPS, Low). Nothing in EA may make it impossible (no hard dependence on features the Deck lacks), but it is not a gate.
- These specs are checked against the Steam Hardware Survey at the M1 gate; the benchmark (M0-07) runs on a machine at or close to each spec.

### 1.1 Client frame budget (recommended spec, 16.6 ms)
| Main thread (CPU) | ms | GPU | ms |
|---|---|---|---|
| Input, prediction, Core client sim (own player) | 1.5 | Shadows (directional, 3 cascades) | 3.0 |
| Snapshot apply + interpolation | 1.0 | Opaque geometry (terrain, foliage, creatures) | 5.0 |
| Godot scene/presentation scripts | 3.0 | Volumetric fog + sky | 2.0 |
| Animation (creature rigs, ≤ 60 animated in view) | 2.0 | Transparent + magic VFX | 1.5 |
| Godot physics (presentation only, §2.4) | 1.0 | Post (glow, tonemap, SSAO/SSIL off on Low) | 1.5 |
| UI | 0.8 | UI | 0.3 |
| Render submission | 3.5 | | |
| **Headroom** | **3.8** | **Headroom** | **3.3** |

### 1.2 Memory budgets
| | Minimum | Recommended | Server |
|---|---|---|---|
| Process RAM | ≤ 6 GB | ≤ 8 GB | ≤ 4 GB (EA region, 16 players, 200+ active creatures) |
| VRAM | ≤ 4.5 GB | ≤ 6.5 GB | — |
| GC | 0 allocations/tick in steady state in Core; gen-0 GC pauses ≤ 1 ms | | server GC, concurrent |

### 1.3 Load times (updates GDD "< 3 s load")
- **Initial join** (connect + world snapshot + first chunks): **≤ 20 s** on minimum spec SSD.
- **Respawn / fast travel / in-world transitions:** **≤ 3 s**.
- **Chunk streaming while moving:** never visible as a hitch; no frame above 33 ms caused by streaming.

---

## 2. Simulation (R03, R11, R12)

### 2.1 Tick and time
- **Server simulation: fixed 30 Hz** (33.3 ms per tick). The world clock is a `ulong` tick counter saved with the world.
- **Server tick budget (≤ 20 ms = 60% of 33.3 ms):**

| Stage | Budget |
|---|---|
| Apply validated inputs and intents | 1.0 ms |
| Movement and collision queries | 4.0 ms |
| Creature AI (all tiers, AI Bible §9) | 4.0 ms |
| Combat, magic, status effects | 2.0 ms |
| Bonding, economy, crafting transactions | 1.0 ms |
| Ecosystem (amortized; T3 abstract sim) | 1.0 ms |
| Replication (snapshot build + send) | 4.0 ms |
| Persistence hand-off (copy dirty state) | 1.0 ms |
| Events, logging, metrics | 1.0 ms |
| **Total** | **19.0 ms** |

- **In-game day = 48 real minutes** (36 min day, 12 min night) = 86,400 ticks. AI Bible half-lives in "days" use this value. Time passes only while the server runs.
- Memory records use a `uint` tick relative to the world epoch (enough for 4.5 years of server uptime); the world tick is a `ulong`.

### 2.2 ECS layout (R03)
- **In-house sparse-set ECS** in `SumonerWorld.Core.Ecs` behind `IEcsWorld` (**ADR-002, accepted 2026-10-19**). The M1 spike measured Arch against it: both are far inside the budget, and the sparse set is 25–30× faster at structural changes and needs no dependencies. Scope limits are in ADR-002.
  - Core keeps referencing only the BCL (architecture test unchanged).
- **Entity:** 32-bit index + 32-bit generation. Persistent things also get a **64-bit `StableId`** (server-generated, saved, used in memories, network and saves). ECS entity ids are never saved or sent.
- **Components** (plain structs, no references to Godot):
  `Transform`, `Velocity`, `Collider`, `Health`, `Stamina`, `Mana`, `Faction`, `NetworkId`, `StableId`, `AiLodTier`, `PerceptionState`, `CreatureMindHandle`, `BondState`, `InventoryHandle`, `StatusEffects`, `Dirty` (persistence), `Relevance` (networking).
- Large, variable data (creature minds, inventories) lives in **pooled side stores** indexed by handle, not inside components (keeps archetypes small, R20, R21).
- **System schedule (fixed order every tick):**
  1. Apply inputs & intents → 2. Apply finished async jobs (by job id) → 3. Perception → 4. AI decision → 5. Behaviour trees → AI intents → 6. Movement → 7. Combat & magic → 8. Status effects & death → 9. Bonding / economy / crafting transactions → 10. Ecosystem → 11. Event dispatch → 12. Replication snapshot → 13. Persistence dirty-copy.
- Systems communicate through **events** in per-tick buffers (R10), not by calling each other.

### 2.3 Determinism strategy (R11, R25)
Two levels, chosen so a solo team can keep them:

| Domain | Guarantee | How |
|---|---|---|
| RNG, creature cognition, economy, crafting, bonding, world generation | **Bit-exact on every platform** | Integers / fixed-point only, seeded `IRandom` streams (`world.Fork(streamId)`), ordered iteration by `StableId`, no wall clock (analyzer-enforced). Currency is `long` minor units. |
| Movement, physics, combat geometry | **Deterministic for the same build on the same machine** (enough for replays and tests) | `float` allowed; server is authoritative so clients never need to match bit-for-bit. |

- **Replay recording:** the server can record inputs + physics query results per tick; replaying a recording reproduces the run. Used for bug reports (R44) and the M1 benchmark.
- **State hash:** a per-tick hash of all deterministic-domain state is logged in debug builds; the determinism test compares two runs and 1 vs. N worker threads.

### 2.4 Physics and navigation
- Core does **not** own a physics engine. It calls `IPhysicsQueries` (ray cast, shape sweep, overlap) and `INavigation` (path request, async). The Godot adapter implements them with Godot's physics server (**Jolt**, set in project settings) and `NavigationServer3D`.
- The **dedicated server is the Godot project running headless in server mode** (`--headless`, no rendering), so server and client share the same collision data and navmesh. Unit tests use fake implementations of both interfaces.
- Godot physics on the client is presentation-only (ragdolls, foliage, debris) and never changes gameplay state (R12, R38).

### 2.5 Threads
| Thread | Work |
|---|---|
| Main / sim | Godot main loop; the Core tick runs from `_PhysicsProcess` on a fixed 30 Hz accumulator |
| Job workers (N = cores − 2) | pathfinding, reputation aggregation, T3 ecosystem steps (R17) |
| Persistence | SQLite writes (§4) |
| Network | ENet service, packet encode/decode |

Results from other threads are applied only at tick step 2, in job-id order.

**Implemented in M1-12** (`JobScheduler`, `JobApplySystem`): fixed worker threads (0 = inline), a fixed-capacity job queue (full = error, not a drop), reusable `IJob` objects (`Execute` off-thread on job-owned data, `Apply` on the sim thread). Every job submitted in a tick is applied at step 2 of the next tick, waiting if needed, so results never depend on thread timing. The CI gate `Background_job_threads_do_not_change_the_result` runs B5 with 0 and 2 workers and compares all 9,900 tick hashes.

---

## 3. Network model (R30–R34)

### 3.1 Topology
- **Player-hosted dedicated server** (headless Godot build, free download) and **listen-server co-op** (host plays and serves). No official servers at EA (DEC-002).
- **Characters and creatures belong to one server's world.** There is no cross-server character transfer, so a host editing their own save cannot pollute other servers' economies.
- Transport: Godot `ENetConnection` used at **packet level** (not Godot RPCs or `MultiplayerSynchronizer`) behind an `ITransport` interface in `SumonerWorld.Net`. Steam Networking Sockets (NAT traversal, invites) can be added later as a second `ITransport`.
- Channels: `Unreliable` (snapshots, movement input), `ReliableOrdered` (intents, events, transactions, chat).

### 3.1a Development server: local Docker (stakeholder decision 2026-10-19)
During development the dedicated server runs as a **dev build in a local Docker container** (Linux engine) on the developer's machine. Game clients (Godot editor or Windows dev builds) connect to it on `localhost`.
- **Why:** the Linux x64 server path is exercised from day one, the server environment is identical on every run, and the same image can run in CI (ubuntu) for the M1 net and persistence spikes.
- **Image:** multi-stage build. Stage 1 builds the C# solution with the .NET SDK image; stage 2 is a slim Linux runtime with the **Godot 4.7.2-stable .NET Linux headless** server build (exported with a dedicated-server export preset; the export templates are pinned to 4.7.2). Godot and SDK versions are build arguments pinned to the versions in ADR-001 and `global.json`.
- **Compose (`docker-compose.dev.yml`):** one `server` service; UDP game port **7777** published to localhost only; world data on a named volume (`world.db` + backups) so restarts keep the world; `SW_SERVER_MODE=dev` enables the signed dev token auth (§3.3), verbose structured logs (§6) and the debug state hash.
- **Commands (planned):** `docker compose -f docker-compose.dev.yml up --build` starts it; `down` stops it and keeps the volume; `down -v` wipes the dev world.
- **Scope:** dev and test only. Player-facing distribution stays the free dedicated-server download (Windows/Linux); an official Docker image for hosts is a post-EA option.
- **When:** the Dockerfile and compose file are created in M1, together with the server entry point (there is nothing to run before that). Tracked as spike 6 in §8.

### 3.2 Intent → validate → replicate
```
Client                           Server (authoritative)                        All relevant clients
input (per tick, seq) ─────────► validate (ranges, NaN, rate) ─► simulate ──► snapshot (20 Hz, delta)
intent {IntentId, type, args} ─► authorize (owner, range, cooldown, cost)
                                 ├─ dedupe by IntentId (§3.4)
                                 ├─ apply atomically or reject with reason ─► event / ack {IntentId, result}
```
- Clients send **inputs and intents, never state** (no positions, no damage, no inventory contents) (R31).
- **Movement:** the client predicts its own character with the same Core movement code and reconciles to the server's acknowledged input sequence. Error above 0.5 m snaps over 100 ms; above 3 m teleports.
- **Remote entities:** interpolated with a 100 ms buffer.
- **Hits:** melee and projectile hits are resolved on the server with **lag compensation** (hitbox rewind up to 200 ms). Above 200 ms ping, the player is at a disadvantage rather than everyone else.
- **Snapshots:** 20 Hz, delta-compressed against the last acknowledged snapshot, quantized (position 1 cm, rotation 16-bit), with an **interest radius** of 150 m (400 m for bosses and large creatures) using the spatial grid and a priority accumulator.
- **Bandwidth budget per client:** ≤ 64 KB/s down average (128 KB/s burst), ≤ 8 KB/s up. Server total for 16 players ≤ 1.5 MB/s.
- Creature minds are never replicated. Clients get emote id, animation intent, bond level and, for the owner only, a Bond Lens summary (top 3 memories, AI Bible §6).

### 3.3 Validation and abuse prevention (R31, R32)
- Every message: size limit, schema version, field ranges, finite floats, rate limit per message type (token bucket). Violations are logged with a reason (R35); repeated violations kick, then temp-ban.
- **Authentication:** Steam session tickets validated by the server through the Steamworks game-server API **[M1 spike]**. Dev builds use a signed dev token. Server password, admin list and ban list are server config.
- No client-side anti-cheat. Server authority + validation is the anti-cheat. A host can cheat on their own server; that is accepted because characters cannot leave that server.

### 3.4 Idempotency and transactions (R27, R33, R34)
- Every state-changing intent carries an **`IntentId` = (client session id, monotonic sequence)**. The server keeps a **dedupe window** per player (last 1,024 ids or 10 minutes). A repeated id returns the original result and changes nothing.
- The dedupe record is **written in the same database transaction as the effect** (§4), so a crash and resend cannot apply an action twice.
- **Items** have a server-generated `StableId`. An invariant check (debug builds every tick, release builds on save) asserts that every item id exists exactly once.
- **Trades:** two-phase on the server: both offers are locked, both players confirm, then a single atomic commit swaps them. A disconnect before commit unlocks everything. Pacts, re-pacts, crafting and ownership changes use the same transaction path (AI Bible §10.1).

### 3.5 Latency, disconnect and reconnect
- Supported play up to **150 ms RTT**, 2% packet loss; degraded but playable to 250 ms.
- On disconnect the character **stays in the world for 30 s** (no combat logging in Hardcore), then logs out safely. Summoned creatures return to the Sanctum.
- **Reconnect within 2 minutes** with the session token resumes the session: the server sends a full snapshot, the client resends unacknowledged intents, and the dedupe window drops duplicates.
- Server restarts load the last committed state; clients reconnect automatically.

---

## 4. Persistence (R26, R27)

### 4.1 Storage
- **One SQLite database per world** (`world.db`, WAL mode) via `Microsoft.Data.Sqlite`, inside `SumonerWorld.Persistence`. SQLite gives atomic, crash-safe transactions without a database server, which suits player-hosted servers.
- Tables (v1): `meta` (schema version, world seed, world tick), `players`, `characters`, `creatures` (mind blob), `items`, `inventories`, `chunks` (changed world state only), `structures`, `transactions`, `intent_dedupe`, `reputation`.
- Record payloads are **MessagePack** with explicit integer keys (`MessagePack-CSharp`, MIT) **[M1 spike]**. Keys are never reused or renumbered. Fallback: hand-written versioned `BinaryWriter` serializers.
- Debug tool: dump any record to JSON (used in bug reports and fixture creation).
- Clients store only settings locally. All character data is on the server.

### 4.2 When state is written
- **Transactions** (trade, pact, ownership, crafting, rewards): committed **before** the client gets a success ack. The tick hands the transaction to the persistence thread; the ack is sent when the commit returns. Typical latency < 50 ms.
- **Everything else** (positions, needs, memories): dirty records are copied at the end of a tick and flushed every **30 s** on the persistence thread. Losing up to 30 s of position and mood after a crash is accepted; losing a trade is not.
- **Backups:** automatic copy every 30 min and on clean shutdown, keeping the last 10. A backup is always made before a migration.

### 4.3 Versioning and migration
- `meta.schema_version` (integer) for the database layout; `SchemaVersion` per record type for payloads (e.g. `CreatureMind v1`, AI Bible §4.6).
- Migrations are ordered `IMigration { From, To, Apply }` classes, run at server start inside one transaction after a backup. **Never downgrade:** a newer save on an older server is refused with a clear message.
- Every version keeps a **fixture save** in `tests/fixtures/saves/`; CI loads every fixture and migrates it to the latest version (R26, R43).
- Content ids (species, items, event types) are stable strings or ids that are never reused. Removed content is migrated to a replacement, never left dangling.

---

## 5. World structure and streaming (R24)

### 5.1 Chunks
- **Chunk = 64 m × 64 m.** EA region = **4 km × 4 km** = 64 × 64 chunks (three biomes + hidden spirit area, GDD).
- At 4 km no floating origin is needed (float precision ≈ 0.5 mm at the edge). Expansions that grow the world beyond 8 km need an ADR for origin shifting.
- **Terrain [M1 spike]:** Terrain3D (MIT, GDExtension) with heightmap + splat painting. Fallback: in-house chunked heightmap meshes with 4 LODs.
- World content is hand-authored on top of a **seeded generator** for scatter (rocks, plants, resource nodes) so it is reproducible (R25).

### 5.2 Client streaming radius
| Ring | Radius | Content |
|---|---|---|
| Full | 2 chunks (~128 m) | full meshes, collision, creatures T0/T1 visible |
| Medium | 6 chunks (~384 m) | LOD1–2 meshes, simplified foliage, distant creatures as LOD meshes |
| Far | to ~1.5 km | terrain LOD + impostors; fog hides the edge |

Loading is asynchronous (Godot `ResourceLoader` threaded requests, pooled scene instances, R20).

### 5.3 Server simulation radius
- Chunks within **3 chunks (~192 m)** of any player are loaded and simulated; so are chunks with an online player's Sanctum or summoned creatures.
- Other chunks are **abstract** (AI Bible §9.4, tier T3). Loading a chunk instantiates creatures from the population model.
- Networking interest radius (150 m) sits inside the simulation radius, so clients never see unsimulated creatures.

---

## 6. Diagnostics, logging, errors (R35, R36)
- Core defines `ILog` and `IMetrics` interfaces (no logging library in Core). The Game layer implements them with structured logs (JSON lines on the server, Godot output on the client).
- Every rejected intent logs player, intent type and reason. Every failed transaction logs its id and rollback.
- Metrics per tick: stage times (§2.1), entity counts per AI tier, bytes sent per client, GC allocations. Shown in a dev overlay and written to a CSV by the benchmark (M0-07).
- Exceptions are never swallowed (CA1031 is an error). A system that throws during a tick is logged with the entity id and the tick is marked failed in metrics; the server keeps running only if the failure is contained to that entity.

---

## 7. Third-party dependency policy
Every dependency must be: permissive licence (MIT/BSD/Apache-2.0/zlib), actively maintained, pinned to an exact version, listed here, and wrapped behind an interface when it touches Core.

| Dependency | Use | Status |
|---|---|---|
| Godot 4.7.2 .NET (MIT) | engine, physics (Jolt), navigation, ENet | adopted (ADR-001) |
| Arch (Apache-2.0) | ECS spike comparison | **Not adopted** (ADR-002). Referenced only by the benchmark project |
| BenchmarkDotNet (MIT) | benchmarks (`tests/SumonerWorld.Benchmarks`) | adopted for tooling only |
| Microsoft.Data.Sqlite (MIT) | save database | M1 spike |
| MessagePack-CSharp (MIT) | record payloads | M1 spike |
| Terrain3D (MIT) | terrain | M1 spike |
| Steamworks.NET (MIT) | auth tickets, server browser | M1 spike |
| xUnit, Microsoft.CodeAnalysis.BannedApiAnalyzers | tests, rule enforcement | adopted |

---

## 8. M1 technical spikes (all must pass before the M1 gate)
1. **ECS:** 200 creatures + 16 simulated players run the full schedule at ≤ 19 ms/tick on the server spec; 0 steady-state allocations/tick.
2. **Net:** 16 bot clients at 150 ms / 2% loss stay within bandwidth budget; reconnect test passes; duplicate intent test applies once.
3. **Persistence:** kill the server process during 1,000 random trades, restart, and verify no item is lost or duplicated.
4. **Terrain & streaming:** fly across the 4 km region at sprint speed with no frame above 33 ms on the recommended spec.
5. **Client frame:** 60 creatures animated in view at 60 FPS on the recommended spec.
6. **Dev server in Docker (§3.1a):** `docker compose up` starts the headless server, a Windows client connects on localhost, the world survives a container restart, and CI builds the same image.

## 9. Open technical questions
1. Steam Deck support level after EA.
2. Should the dedicated server also ship as a pure .NET process (no Godot) later? Only if physics/navigation can be replaced; not before 1.0.
3. Mod support (Steam Workshop after 1.0, DEC-002): data-only mods first; scripting would need a sandbox ADR.

## Changelog
- **0.1 (2026-10-19):** First version. Hardware targets and budgets, 30 Hz sim, ECS layout, two-level determinism, network model, persistence and migrations, chunking and streaming, M1 spikes.
- **0.1 approved (2026-10-19):** Stakeholder approved via PR #3, with one addition: the dev server runs in local Docker (§3.1a, spike 6). Server-bound characters, headless-Godot server and the revised load targets confirmed.
