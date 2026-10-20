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
- **Creature world (M1-15a):** `Core.World.CreatureWorld` owns the ECS world, mind store, AI services, spatial grid, job scheduler and reputation for one region, plus the production creature systems (`CreatureSystems`: job apply, LOD, perception, decision, behaviour, intents, movement, reputation flush). Spawning and despawning creatures and player avatars, and the state hash, go through it. The server host and the B5 scenario both build on it; B5 only adds stand-ins (patrol route, crowding stress, Pact offers, churn). The B5 state hash was unchanged by the move.

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
- **Implemented in M1-13:** `SumonerWorld.Net` has **no Godot reference** (so all network logic is unit-testable and runs in the Docker server without the engine's render path). It holds `ITransport` / `ITransportHandler`, a deterministic in-process `LoopbackNetwork` (fixed latency in steps, seeded loss for unreliable messages, reliable ordered delivery; used by tests, the M1-18 bots and listen-server co-op) and the message codec (`MessageWriter` / `MessageReader`, header = protocol version + type, 1,200-byte limit, every read error is a `MalformedMessageException`; fuzz-tested). The **Godot ENet adapter** implements `ITransport` in the Game project with the server host (M1-14).

### 3.1a Development server: local Docker (stakeholder decision 2026-10-19)
During development the dedicated server runs as a **dev build in a local Docker container** (Linux engine) on the developer's machine. Game clients (Godot editor or Windows dev builds) connect to it on `localhost`.
- **Why:** the Linux x64 server path is exercised from day one, the server environment is identical on every run, and the same image can run in CI (ubuntu) for the M1 net and persistence spikes.
- **Image:** multi-stage build. Stage 1 builds the C# solution with the .NET SDK image; stage 2 is a slim Linux runtime with the **Godot 4.7.2-stable .NET Linux headless** server build (exported with a dedicated-server export preset; the export templates are pinned to 4.7.2). Godot and SDK versions are build arguments pinned to the versions in ADR-001 and `global.json`.
- **Compose (`docker-compose.dev.yml`):** one `server` service; UDP game port **7777** published to localhost only; world data on a named volume (`world.db` + backups) so restarts keep the world; `SW_SERVER_MODE=dev` enables the signed dev token auth (§3.3), verbose structured logs (§6) and the debug state hash.
- **Commands (planned):** `docker compose -f docker-compose.dev.yml up --build` starts it; `down` stops it and keeps the volume; `down -v` wipes the dev world.
- **Scope:** dev and test only. Player-facing distribution stays the free dedicated-server download (Windows/Linux); an official Docker image for hosts is a post-EA option.
- **When:** the Dockerfile and compose file are created in M1, together with the server entry point (there is nothing to run before that). Tracked as spike 6 in §8.
- **Spike 6 passed (M1-17b):** the world (tick, creatures, players) survives `docker compose restart`. **Implemented in M1-14:** `source/server/Dockerfile` (base `mcr.microsoft.com/dotnet/sdk:9.0` + .NET 8 runtime + **Godot 4.7.2 .NET Linux, SHA-256 pinned**) builds the C# solution and runs the project with `godot --headless -- --server`. For a dev image this runs the engine binary directly instead of an exported dedicated-server build, so no export templates are needed; an exported slim image is a post-EA option. `source/docker-compose.dev.yml` publishes UDP 7777 on localhost, keeps a `world-data` volume (used from M1-17) and has a `smoke` profile client. Verified: the Docker smoke client and a **Windows client on localhost** were both welcomed, and the server came back after `docker compose restart`. CI workflow `server-image.yml` repeats this on every PR. *World data surviving a restart* is completed in M1-17 (persistence).

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

- **Implemented in M1-15c (protocol 2):** `Net.Server.GameServer` runs poll → tick → snapshot. Clients send an unreliable `InputBatch` every tick holding their newest ≤ 8 inputs (≈ 50 B, ≈ 1.5 KB/s up), so a lost datagram costs nothing; the server applies each input once in sequence order, one per tick (two while more than 4 wait), ignores copies and drops inputs more than 32 ahead. Intents (`IntentRequest`, reliable) are queued per player (more than 32 in one tick is a violation and drops the connection) and run through the `TransactionProcessor` at stage 9; the `IntentAck` carries the result. Snapshots (20 Hz) carry the receiver's own avatar exactly (floats) with the last applied input, so reconciliation is exact, and every entity within 150 m quantized to 1 cm and 1/256 turn, in parts of at most 1,200 B. `Net.Client.ClientPrediction` replays unacked inputs from the server position: errors ≤ 0.5 m are corrected at once, 0.5–3 m smoothed over 100 ms, > 3 m teleport. Tested with 2 clients over the loopback network (3-step latency, 0 % and 20 % loss): predictions end exactly at the server positions, with no corrections at 0 % loss. Not yet: delta compression and priority (M1-18), remote interpolation buffer (M1-21), rate-limit buckets (M1-16/18).
- **M1-18a (bots and metering):** `Net.Transport.MeteredTransport` counts payload bytes and messages per connection; `BandwidthMeter` turns that into per-client KiB/s each second, adding an upper-bound header estimate of 44 B per message (IPv4 + UDP + ENet, one datagram per message), and checks it against `BandwidthBudget` (the numbers above). `Net.Bots.BotFleet` runs bot clients that replay seeded recorded inputs (`BotScript`: 1–5 s walks in 8 directions with pauses, kept within 40 m of the start) and offer a Pact to the nearest creature they see every 10 s. In-process B2 (CI test, 200 creatures, 16 bots, 2 % loss, ≈ 150 ms RTT): **18.8 KiB/s down per client on average, 21.2 KiB/s in the busiest second, 2.1 KiB/s up, 301 KiB/s server total** — under a third of every budget line with full (non-delta) snapshots. **Decision (R23, measure before optimizing):** delta compression and the priority accumulator are deferred until a measurement needs them (M2 creature densities); the design above stays the target. - **M1-18b (B2 in Docker):** `--bots=N --connect=host:port` runs a `BotFleet` over ENet in one headless Godot process. `docker-compose.b2.yml` runs the server (4 CPUs, 8 GB) and the bots in two containers whose entrypoint applies `SW_NETEM` (`tc netem delay 75ms loss 2%` on each side = 150 ms RTT); `scripts/run-b2.sh` runs it and checks the result; CI job `b2` repeats a 60 s version on every PR. The server's `ServerBenchmark` (`--benchmark-out`) waits for all players, warms up 30 s and measures whole-tick and AI time, allocations, memory, per-client bandwidth and the NIC byte counters. **The ENet host is sized at twice the player limit:** with exactly the limit, a client whose connection dropped could not get a peer slot until ENet timed the old one out, so a resume took 16 s instead of 0.4 s. On shutdown the server tells every peer goodbye before saving, so clients do not wait for a timeout.

### 3.3 Validation and abuse prevention (R31, R32)
- Every message: size limit, schema version, field ranges, finite floats, rate limit per message type (token bucket). Violations are logged with a reason (R35); repeated violations kick, then temp-ban.
- **Authentication:** Steam session tickets validated by the server through the Steamworks game-server API **[M1 spike]**. Dev builds use a signed dev token. Server password, admin list and ban list are server config.
- No client-side anti-cheat. Server authority + validation is the anti-cheat. A host can cheat on their own server; that is accepted because characters cannot leave that server.

### 3.4 Idempotency and transactions (R27, R33, R34)
- Every state-changing intent carries an **`IntentId` = (client session id, monotonic sequence)**. The server keeps a **dedupe window** per player (last 1,024 ids or 10 minutes). A repeated id returns the original result and changes nothing.
- The dedupe record is **written in the same database transaction as the effect** (§4), so a crash and resend cannot apply an action twice.
- **Items** have a server-generated `StableId`. An invariant check (debug builds every tick, release builds on save) asserts that every item id exists exactly once.
- **Trades:** two-phase on the server: both offers are locked, both players confirm, then a single atomic commit swaps them. A disconnect before commit unlocks everything. Pacts, re-pacts, crafting and ownership changes use the same transaction path (AI Bible §10.1).

- **Implemented in M1-15b (Core):** `IntentId`, `IntentDeduplicator` (ring of the last 1,024 ids per session; because sequences only increase, an id not in the ring and not above the highest seen is *stale* and never applied), `TransactionProcessor` (dedupe → `ITransaction.Validate` → `Commit` → record; validation is a transaction's first phase, not a separate service) and the first transaction, `OfferPactTransaction` (avatar exists, terms in catalogue, creature exists and is wild, ≤ 10 m, 1 s cooldown; a refusal is applied because the creature remembers being asked). Network messages for intents arrive with M1-15c.

### 3.5 Latency, disconnect and reconnect
- Supported play up to **150 ms RTT**, 2% packet loss; degraded but playable to 250 ms.
- On disconnect the character **stays in the world for 30 s** (no combat logging in Hardcore), then logs out safely. Summoned creatures return to the Sanctum.
- **Reconnect within 2 minutes** with the session token resumes the session: the server sends a full snapshot, the client resends unacknowledged intents, and the dedupe window drops duplicates.
- Server restarts load the last committed state; clients reconnect automatically.
- **Implemented in M1-16 (protocol 3):** `ServerEndpoint` suspends a session on disconnect and resumes it when a Hello carries its token from the same account within 3,600 ticks (the `Welcome` says `Resumed`); a foreign or expired token gets a new session; a second login replaces the first (one session per account). `GameServer` keeps the avatar in the world for 900 ticks, then parks it with its last position and puts it back on resume; input buffer and queued intents are reset, the applied-input sequence and the dedupe window are kept. `GameClient` restarts prediction from the next snapshot and resends unanswered intents with their sequences only when resumed (a new session counts them as lost). The Godot client retries every 2 s for 2 min. The loopback network now closes gracefully like ENet's `PeerDisconnectLater` (data sent before a disconnect still arrives; data to a closed connection is lost). Sanctum return of summoned creatures comes with summoning; resume after a server restart needs the world store (M1-17).

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
- Migrations are ordered `IMigration { FromVersion, ToVersion, Apply }` classes, run at server start inside one transaction after a backup. **Never downgrade:** a newer save on an older server is refused with a clear message.
- Every version keeps a **fixture save** in `tests/fixtures/saves/`; CI loads every fixture and migrates it to the latest version (R26, R43).
- Content ids (species, items, event types) are stable strings or ids that are never reused. Removed content is migrated to a replacement, never left dangling.

### 4.4 Implementation status
- **M1-17a:** `SumonerWorld.Persistence` (references Core, Microsoft.Data.Sqlite and MessagePack only; layering-tested). `WorldStore` opens `world.db` with WAL and `synchronous=FULL` (an acknowledged trade must survive power loss, not just a process kill), runs `Migrations.All` in one transaction after a backup, and refuses newer saves. Schema v1: `meta`, `items` (id, owner, payload), `intent_dedupe` (last 1,024 ids per session, written in the same transaction as the effect). Payloads: one version byte + MessagePack with integer keys, read with untrusted-data security. Fixture save `tests/fixtures/saves/world-v1.db` (written by `CrashHarness fixture`); CI loads every fixture. **Spike 3 passes:** the harness process is killed three times during 1,000 random swaps/crafts and restarted with the last trades resent; the database then equals a single replay of every trade (no item lost or duplicated, every resend answered from the dedupe table). A mutation check (dedupe disabled) makes it fail.
- **M1-17b:** schema v2 adds `players`, `creatures` (payload with the full "CreatureMind v1": personality, memories, baselines, needs, bond) and `reputation`; species are saved by data id, never index. `WorldSnapshots.Capture/Restore` (Core) copy clock, random state, id counter, creatures and reputation; transient AI state (tier, focus, goal, emote, running trees) is rebuilt. A restored world runs deterministically but is a new starting point, not a bit-identical continuation (entity and mind slots are reassigned). **Checkpoints instead of dirty tracking for M1:** every 30 s the simulation hands a full snapshot (≈ 200 creatures) to the `PersistenceWriter` thread, which writes it in one transaction (only the newest waiting snapshot is written) and makes a timed backup every 30 min. The server loads the saved world on start (or creates and saves a new one), and on SIGTERM/SIGINT (`docker stop`) quits through the main loop to write a last checkpoint and a shutdown backup. Verified in Docker: tick, 200 creatures and the player's place survived `docker compose restart`. Fixture `world-v2.db` added; `world-v1.db` migrates with a pre-migration backup. Dirty tracking replaces full checkpoints when worlds grow (M2).
- **M1-17c:** transactions are committed before the ack. `GameServer` appends every processed intent to the `ITransactionJournal` (result, the creatures it changed, whether it is new) and sends the `IntentAck` only when the journal reports it durable; the `PersistenceWriter` writes each entry (creature rows + dedupe record) in one transaction, in order with the checkpoints. A repeat waits for its original and records nothing. Session ids continue above the highest one in the saved dedupe table, so they never collide after a restart. Sessions themselves do not survive a restart yet: after one, clients get a new session and count unanswered intents as lost (never applied twice).

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

**Implemented in M1-19a:**
- **Rings:** `Core.World.StreamingPlanner` (`IStreamingPlanner`) sets each chunk's ring from the distance between the viewer and the chunk's nearest point: full ≤ 128 m, medium ≤ 384 m, far ≤ 1,536 m. Each update returns every downgrade and at most 4 upgrades, nearest first. A downgrade only happens 16 m past the ring edge.
- **Ground:** `Core.World.Heightfield` generates seeded heights (fractal value noise, 40 m amplitude, 1 m spacing). It can be sampled from any thread, so the client terrain and, from M1-20, server movement share the same ground.
- **Props:** `ChunkScatter` places seeded props per chunk: 24 trees, 16 rocks and 60 bushes. The medium ring uses the first 40 of them.
- **Game layer:** `TerrainBuilder` creates a **Terrain3D 1.0.2** node from the heightfield. It is pinned by SHA-256 and installed by `scripts/fetch-terrain3d.sh`; it is not committed. Because the extension has no C# bindings, it is driven with `Call`/`Set`. If the extension is missing, an in-house mesh per chunk is built instead.
- **Streaming:** `ChunkStreamer` builds MultiMesh buffers for full and medium chunks on worker threads. The main thread uploads at most 2 chunks per frame into pooled views; the far ring is terrain only.
- **Dev mode:** `--region [--region-chunks=N] [--screenshot=file.png]` opens the region.
- **Load times (dev PC):** the 8 × 8 region is ready in 0.3 s and streams its 64 chunks; the 4 km region (64 × 64) is generated and imported in 5.3 s.
- **Not yet:** the `ResourceLoader` path is for authored chunk scenes in M2; generated placeholder content needs none.
- **M1-19b (B3):** `--benchmark=B3` (`FlyRouteBenchmark`) flies a fixed loop at eye height: a 5 s settle while already flying (not measured), then 150 s at sprint speed (assumed 9 m/s) and 150 s at 27 m/s. It records every frame's time, the chunk uploads and the upload time, and writes `summary.json` and `frames.csv`. On the 4 km region, uploads stay under 3.3 ms per frame and at most 12 chunks wait at once. **Terrain3D is adopted.**

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
| Microsoft.Data.Sqlite 8.0.11 (MIT) | save database | adopted (M1-17a) |
| MessagePack-CSharp 2.5.307 (MIT) | record payloads | adopted (M1-17a); ≥ 2.5.301 required (GHSA-wfr3-xj75-pfwh, found by NuGet audit on 2.5.187) |
| Terrain3D 1.0.2-stable (MIT) | terrain (GDExtension; supports Godot 4.4–4.7) | **adopted** (M1-19b: B3 / spike 4 passed on the dev PC); installed by `scripts/fetch-terrain3d.sh` (SHA-256 pinned, Windows + Linux x86_64 binaries only, not committed); in-house mesh fallback when missing |
| Steamworks.NET (MIT) | auth tickets, server browser | M1 spike |
| xUnit, Microsoft.CodeAnalysis.BannedApiAnalyzers | tests, rule enforcement | adopted |

---

## 8. M1 technical spikes (all must pass before the M1 gate)
1. **ECS:** 200 creatures + 16 simulated players run the full schedule at ≤ 19 ms/tick on the server spec; 0 steady-state allocations/tick.
2. **Net:** 16 bot clients at 150 ms / 2% loss stay within bandwidth budget; reconnect test passes; duplicate intent test applies once.
   **Passed (M1-18b, 2026-10-20, run 1 of 3 on the dev PC):** 18.9 KiB/s down and 2.2 KiB/s up per client, 269 KiB/s server total on the NIC; the hard-dropped bot resumed in 12 ticks; the repeated intent was answered from the dedupe window (1,009 intents sent = 514 applied + 495 rejected). Server tick p99 2.55 ms. Report: `qa/performance-logs/2026-10-20_M1-18_b2_server16_dev-pc.md`. Runs 2–3 still to be repeated (stopped because the host ran low on memory).
3. **Persistence:** kill the server process during 1,000 random trades, restart, and verify no item is lost or duplicated.
4. **Terrain & streaming:** fly across the 4 km region at sprint speed with no frame above 33 ms on the recommended spec.
   **Passed on the dev PC (M1-19b, 2026-10-20; indicative, the recommended-spec run is M1-24):** B3 `--benchmark=B3 --region-chunks=64`, 3 runs, 5.4 km at 9 and 27 m/s each. Median over the 3 runs: frame p99 3.55 ms, 0 frames over 33 ms, 0 streaming hitches. Run 2 had one second of stalls of up to 90 ms with no streaming work in it, which was not reproducible; it is reported as measured. Report: `qa/performance-logs/2026-10-20_M1-19_b3_stream_run_dev-pc.md`.
5. **Client frame:** 60 creatures animated in view at 60 FPS on the recommended spec.
6. **Dev server in Docker (§3.1a):** `docker compose up` starts the headless server, a Windows client connects on localhost, the world survives a container restart, and CI builds the same image.

## 9. Open technical questions
1. Steam Deck support level after EA.
2. Should the dedicated server also ship as a pure .NET process (no Godot) later? Only if physics/navigation can be replaced; not before 1.0.
3. Mod support (Steam Workshop after 1.0, DEC-002): data-only mods first; scripting would need a sandbox ADR.

## Changelog
- **0.1 (2026-10-19):** First version. Hardware targets and budgets, 30 Hz sim, ECS layout, two-level determinism, network model, persistence and migrations, chunking and streaming, M1 spikes.
- **0.1 approved (2026-10-19):** Stakeholder approved via PR #3, with one addition: the dev server runs in local Docker (§3.1a, spike 6). Server-bound characters, headless-Godot server and the revised load targets confirmed.
