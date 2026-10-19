# Sumoner World - AI Bible (Creature Cognition Contract)
Version: **0.1** (M0-04) · Date: 2026-10-19 · Owners: Sr Game Designer (behaviour) + Mechanics Developer (implementation) · Status: **Delivered, review pending (stakeholder)**
Rules: R08, R11, R13–R19, R21, R28, R29, R30, R50 · Related: GDD v0.1 (Pillar 1, Systems §1–2, §7–9), ADR-001, TDD (M0-05, pending)

> This document is the single source of truth for how creatures perceive, remember, feel, decide and bond. Code in `SumonerWorld.Core` implements it; the GDD describes the player-facing intent. If the three disagree, this document wins for creature behaviour and must be updated in the same PR as the code (R50).
> All numbers are **starting values**. Live values come from versioned data files (R08), not from code or from this document.

---

## 1. Principles (non-negotiable)
1. **Memory is the product.** Every cognitive system exists to create, use or show a memory or relationship (GDD Pillar 1).
2. **No omniscience (R15).** A creature only knows what it perceived, remembers or was told by its group. It cannot "see" a player behind a wall or know who killed its packmate unless it witnessed or heard of it.
3. **Deterministic (R11).** Same seed + same inputs = same decisions. Randomness only through the creature's own `IRandom` stream (`world.Fork(creatureId)`). Ties are broken by stable ids, not by chance.
4. **Server-authoritative (R30).** Cognition runs only on the server (or the host in co-op). Clients receive presentation state (emote, animation intent, Bond Lens summary), never the memory store.
5. **Data-driven (R08).** Species, traits, event types, goals, considerations and behaviour trees are data. Adding a species or goal must not need a change to core logic.
6. **Bounded and allocation-free (R21).** Every per-creature structure has a fixed capacity. No allocations in the per-tick path; pools and preallocated buffers only.
7. **Readable.** Every important internal change has an outward sign (animation, posture, sound) before it has a UI element (DEC-002).
8. **No generative AI at runtime (R16).** See §11.

---

## 2. Architecture overview
One cognition cycle per creature, run at the rate of its AI LOD tier (§9):

```
 Perception ──► Memory & Knowledge ──► Appraisal (emotion, needs) ──► Utility goal selection ──► Behaviour tree ──► Action intents
     ▲                 │  ▲                                                     ▲                                      │
     │                 │  └─────────────── outcomes / events ──────────────────┼──────────────────────────────────────┘
     │                 └──► Opinions (trust, fear, affection, respect) ─────────┘
 Spatial grid (R19)                                         Player command (validated intent)
```

| Module (Core namespace) | Responsibility | Writes |
|---|---|---|
| `Cognition.Perception` | Sight, hearing, scent queries against the spatial grid | Percepts (per-tick buffer) |
| `Cognition.Memory` | Episodic memories, decay, consolidation, eviction | Memory store |
| `Cognition.Opinions` | Per-subject attitudes derived from memory | Opinion table |
| `Cognition.Appraisal` | Short-term emotions and needs | Emotion + needs state |
| `Cognition.Decision` | Utility scoring, goal selection, command evaluation | Current goal |
| `Cognition.Behaviour` | Data-defined behaviour trees that execute a goal | Action intents |
| `Cognition.Learning` | Tactic and habit tables | Learning tables |
| `Bonding` | The Bonding Contract (§10): ownership, pacts, bond level, commands | Bond state |

All modules are pure C# in `SumonerWorld.Core` (no Godot, ADR-001). They are registered in the system registry (M0-06) so no second implementation is created (R47).

---

## 3. Personality
Four trait axes, each an integer in **[-100, +100]**:

| Axis | −100 | +100 | Main effects |
|---|---|---|---|
| `Boldness` | timid | bold | flee threshold, engage distance, Pact approach |
| `Loyalty` | independent | loyal | obedience, re-pact consent, grief strength |
| `Curiosity` | wary | curious | exploration, reaction to novel players/objects, learning rate |
| `Temper` | gentle | aggressive | attack thresholds, grudge strength, territorial response |

- **Birth values:** species mean ± a spread from species data, rolled from the creature's seeded stream. Bred creatures inherit the parent mean ± spread (traits are inherited, memories are not, GDD open question 3).
- **Drift (proposal for GDD open question 2): slow, bounded drift.** Core memories (§4.4) push a trait by 1–5 points; total drift is capped at **±25** from the birth value. A timid creature you protect for weeks becomes braver, but never becomes a different animal.
- Personality never changes from UI choices or purchases.

---

## 4. Memory model

### 4.1 Memory record (fixed size, 32 bytes target)
| Field | Type | Meaning |
|---|---|---|
| `EventType` | `ushort` | id from the event-type table (§4.2) |
| `Subject` | `EntityRef` (kind + 64-bit id) | who/what it is about: player account, creature, species, place (region cell), item type |
| `Valence` | `sbyte` [-100, +100] | how good or bad it was for this creature |
| `Salience` | `ushort` [0, 1000] | how strongly it is remembered at `LastReinforcedTick` |
| `CreatedTick` | `uint` | sim tick when first formed |
| `LastReinforcedTick` | `uint` | last time it was reinforced |
| `Count` | `ushort` | how many episodes were merged into it |
| `Flags` | `byte` | `Core`, `Rumour`, `Witnessed`, `Pinned` |

Values are **integers** so that decay, saves and replays are bit-exact (R11). Floats are allowed only in presentation.

### 4.2 Event types (v1 starting set, data file `data/ai/memory_event_types.json`)
| Id | Event | Subject | Valence | Base salience | Half-life | Core? |
|---|---|---|---|---|---|---|
| 1 | `FedBy` | player | +20 | 150 | 2 days | no |
| 2 | `HealedBy` | player | +40 | 300 | 7 days | no |
| 3 | `SavedLifeOf` (me) | player/creature | +90 | 900 | never | **yes** |
| 4 | `ProtectedYoung` | player | +70 | 700 | 30 days | **yes** |
| 5 | `AttackedBy` | player/creature | −40 | 400 | 3 days | no |
| 6 | `DefeatedButSpared` | player | +30 / respect | 500 | 14 days | no |
| 7 | `KilledKin` (witnessed or rumoured) | player/creature | −90 | 900 | 60 days | **yes** |
| 8 | `HuntedMyKind` | player | −50 | 400 | 10 days | no |
| 9 | `PactOffered` | player | +5 | 100 | 2 days | no |
| 10 | `PactTermKept` | player | +15 | 150 | 7 days | no |
| 11 | `PactBroken` | player | −80 | 800 | 90 days | **yes** |
| 12 | `CommandedIntoDanger` | player | −30 | 350 | 5 days | no |
| 13 | `FoughtBeside` | player/creature | +25 | 250 | 7 days | no |
| 14 | `BondedDied` | creature | −70 | 900 | never | **yes** |
| 15 | `OwnerDied` | player | −40 | 500 | 3 days | no |
| 16 | `PlaceDanger` / `PlaceSafe` | place | ±40 | 300 | 14 days | no |
| 17 | `Mistreated` (hit, starved, ignored needs) | player | −50 | 450 | 14 days | no |

"2 days" means in-game days; the tick conversion lives in the TDD (M0-05). New event types are added in data, with a new id. Ids are never reused (save compatibility, R26).

### 4.3 Decay, reinforcement and consolidation
- **Lazy decay:** salience is stored at `LastReinforcedTick` and evaluated on read:
  `effective = Salience × 2^(−(now − LastReinforcedTick) / HalfLife)`
  computed with integers: whole half-lives by bit shift, the remainder from a 16-entry fixed-point lookup table. No per-tick work for memories that are not read.
- **Reinforcement:** the same `(EventType, Subject)` within its merge window updates the existing record instead of creating a new one: `Count++`, `Salience = min(1000, effective + Base × 0.6)`, `LastReinforcedTick = now`. Repetition makes memories stronger, so a creature fed every day really trusts you.
- **Forgetting:** a memory below salience 20 is free for eviction. Core memories never decay below 300 while the creature lives.

### 4.4 Capacity and eviction
| Store | Capacity per creature | Eviction |
|---|---|---|
| Episodic memories | **48** | lowest effective salience; never `Pinned` |
| Core memories (`Pinned`) | **8** | a new core memory replaces the weakest core memory only if it is stronger |
| Opinion subjects (§5) | **24** | lowest `|trust| + |fear| + |affection|`, never the owner |

When a core memory forms, the trait drift in §3 is applied once and the creature plays a visible reaction (it stops, looks at the subject, emotes).

### 4.5 Group memory and rumours (Pillar 2)
- Creatures of a social species belong to a **group** (pack, herd, flock). When group members are near each other, they share their **top 3 memories about players**, copied with the `Rumour` flag at **50%** salience. A rumour is never passed on again (no infinite chains).
- **Server reputation:** per `(species, region, player)` the server keeps an aggregate score in [-1000, +1000], updated from `KilledKin`, `HuntedMyKind`, `ProtectedYoung`, `SavedLifeOf` and `PactBroken` events. Newly spawned wild creatures start with an opinion of each known player seeded from this score. This is how herds "remember" an over-hunter.
- Reputation aggregation runs asynchronously at low frequency (R17, §9.3).

### 4.6 Persistence format (R14, R26)
- Saved per creature: traits (birth + drift), memory store, opinions, bond state, learning tables, needs. Short-term emotions are **not** saved (they rebuild in seconds).
- Logical schema `CreatureMind v1`, with a `SchemaVersion` field per creature record. The byte encoding (binary for release, JSON for debug dumps) is chosen in the TDD (M0-05); this document fixes the **logical fields and their ranges**.
- Every schema change bumps the version and ships a migration `vN → vN+1` with a test that loads a saved fixture from the old version (R26).
- Memories reference players by **account id**, never by display name, so renames do not erase history.

---

## 5. Opinions
For each remembered subject the creature holds four attitudes, each in [-100, +100]:

| Attitude | Raised by | Lowered by | Drives |
|---|---|---|---|
| `Trust` | kept terms, feeding, healing, fair commands | broken pacts, commands into danger, mistreatment | obedience, Pact acceptance, re-pact consent |
| `Fear` | being attacked or defeated, kin killed | time, gentle contact | fleeing, submission, hesitation |
| `Affection` | time together, play, protection | neglect, mistreatment | following distance, autonomous protection, grief |
| `Respect` | being defeated but spared, strong combined fights | fleeing together, losing | accepting hard commands, apex Pacts |

- Opinions are **recomputed from memories** when the creature reads them (sum of effective salience × valence weights per event type), plus a slow `Baseline` that keeps long-term history after individual memories fade. The weights per event type are data.
- **Bond level** with the owner (§10.3) is derived from `Trust` and `Affection`.

---

## 6. Appraisal: emotions and needs
### 6.1 Short-term emotions (not saved)
`Fear`, `Anger`, `Joy`, `Curiosity`, `Stress`, each 0–100, decaying back to a personality baseline over seconds to minutes. They are raised by percepts and memory recall (seeing the player who killed its kin raises `Anger` or `Fear` depending on `Boldness`).

### 6.2 Emotes (readability contract with Art/Animation)
The dominant emotion and the opinion of the nearest notable subject map to **one of 6 emotes**, shared by every body type (DEC-002):

| Emote | Trigger | Body-language intent |
|---|---|---|
| `Trust` | Trust > 50 toward the viewer | relaxed posture, approaches, turns its back safely |
| `Fear` | Fear dominant | low posture, keeps distance, watches |
| `Grudge` | negative core memory of the viewer | stares, blocks the path, growls, never turns its back |
| `Curiosity` | Curiosity dominant, novel subject | head tilt, circling, sniffing |
| `Affection` | Affection > 60 | nuzzles, stays close, mirrors movement |
| `Grief` | `BondedDied`/`OwnerDied` recent | slow movement, returns to the place, calls |

The emote id is replicated to clients; the client chooses the animation. Animation authors must make each emote readable at 30 m.

### 6.3 Needs (saved)
`Hunger`, `Rest`, `Safety`, `Social`, `Territory`, each 0–100, rising over time at species rates. Needs feed utility considerations and Pact Terms (§10.2).

---

## 7. Decision architecture (R13)
**Utility AI chooses *what* to do. Behaviour trees decide *how*.**

### 7.1 Goals
v1 goal set (data file `data/ai/goals.json`; species enable a subset):
`Flee`, `Fight`, `Hunt`, `Eat`, `Rest`, `Socialize`, `Explore`, `DefendTerritory`, `ProtectYoung`, `Follow` (bonded), `ExecuteCommand` (bonded), `Guard`, `ProtectOwner`, `ProtectGrave`, `Grieve`, `WorkAtSanctum`, `Approach` (curious about a player), `Avoid` (a feared/grudge subject).

### 7.2 Scoring
```
score(goal) = goal.Weight × personalityModifier(goal) × Π consideration_i(curve_i(input_i)) + momentum
```
- **Considerations** read a normalised input (need, emotion, opinion, distance, health, time of day) through a **response curve** (linear, quadratic, logistic, step) defined in data. Inputs and curve outputs are fixed-point [0, 1000].
- **Personality modifier:** per-goal trait weights (for example `Flee` is scaled by −`Boldness`).
- **Momentum:** the current goal gets +10% until it completes or is interrupted, to prevent flip-flopping.
- **Selection:** highest score wins. If the top two are within 5%, the creature picks between them with its own seeded stream (personality-like variety, still deterministic). Exact ties go to the lower goal id.
- **Interrupts:** `Flee`, `ProtectYoung` and `ProtectOwner` can pre-empt any goal immediately on a high-priority percept; all others wait for the next decision tick.

### 7.3 Behaviour trees
- Trees are **data-defined** (`data/ai/bt/*.json`), built once per species at load, and shared between creatures (flyweight). Per-creature state is a small fixed blackboard.
- Node set v1: `Sequence`, `Selector`, `Parallel`, `Condition`, `Action`, `Wait`, `Cooldown`, `Repeat`, `Abort-on-goal-change`.
- Actions emit **intents** (`MoveTo`, `Attack`, `Cast`, `Emote`, `Interact`), which the simulation validates exactly like player intents (R30). AI cannot do anything a player-controlled body could not.
- Pathfinding requests are asynchronous (§9.3). The tree waits on the result and never blocks the tick.

### 7.4 Commands from the owner
A command is a server-validated intent (owner, range, cooldown, creature alive and summoned). The creature then chooses one of four responses:

| Response | When | What the player sees |
|---|---|---|
| **Obey** | obedience ≥ 600 | immediate, clean execution |
| **Improvise** | 400–599, or it knows a better tactic (§8) | does it its own way (flanks instead of charging) |
| **Hesitate** | 200–399 | short pause + `Fear`/`Grudge` emote, then obeys |
| **Refuse** | < 200, or the command is suicidal and `Loyalty` < 50 | refusal emote, does its own goal |

`obedience = f(bond level, Trust, Respect, Loyalty, −Fear, −danger of the command, −conflict with own memories)`; the curve is data. A command into obvious danger records `CommandedIntoDanger` if the creature is hurt. The response is always shown (Pillar 1: the player learns *why*).

---

## 8. Learning and adaptation (R14)
All learning tables are bounded, integer and saved.
- **Tactic learning:** per creature, a table `(enemy species → tactic weight)` for up to **8** enemy species and the species' tactic list (`Charge`, `Flank`, `Kite`, `Ambush`, `Guard`). After each fight the weight of the used tactic moves ±5 by outcome (damage dealt vs. taken). The `Fight` tree picks tactics by weight.
- **Habit learning:** for the owner only, the creature records the owner's last 3 combat openers (n-gram counts, 16 entries). When a pattern reaches a threshold (e.g. "the owner always opens with an Ember spell") the creature gets an anticipatory action (move to flank before the spell lands). This creates the GDD moment "it learned my style".
- **Combo learning:** a player-spell + creature-ability combo becomes reliable after it succeeds **5 times** (data value), then the creature starts it on its own (GDD Combat §3).
- Learning rate is scaled by `Curiosity`.

---

## 9. Performance: AI LOD, spatial queries, async work (R17–R19, R21, R22)

### 9.1 AI LOD tiers
Tier is chosen by distance to the nearest player, combat state and whether the creature is bonded to an online player. Server sim tick rate is set in the TDD (working value **30 Hz**).

| Tier | Who | Perception | Decision (utility) | Behaviour tree | Budget per creature |
|---|---|---|---|---|---|
| **T0 Active** | in combat, or ≤ 40 m of a player, or summoned | 10 Hz | 5 Hz + interrupts | every tick | ≤ 20 µs/tick avg |
| **T1 Near** | ≤ 120 m | 4 Hz | 2 Hz | 10 Hz | ≤ 8 µs/tick avg |
| **T2 Far** | loaded chunks beyond 120 m | 1 Hz (coarse) | 0.5 Hz, reduced goal set | 2 Hz, simplified trees | ≤ 2 µs/tick avg |
| **T3 Abstract** | unloaded chunks | none | none | none | population model only (§9.4) |

**Total AI budget: ≤ 4 ms per server tick for 200 creatures in T0–T2 on the recommended spec** (M1 gate, benchmark in M0-07). Typical M1 mix: 30 T0 + 70 T1 + 100 T2 ≈ 0.6 + 0.56 + 0.2 = 1.4 ms, which leaves headroom for spikes.

### 9.2 Time slicing
Creatures in a tier are split into buckets by `creatureId mod N`, and one bucket is updated per tick, so the load is spread evenly and the order is deterministic.

### 9.3 Spatial partitioning and async jobs
- **Spatial hash grid** (working cell size 16 m) for perception, proximity, targeting and rumour sharing (R19). Queries write into preallocated buffers.
- **Async (R17):** pathfinding, reputation aggregation and T3 population steps run as jobs. Results are applied at the start of a later tick in a fixed order (by job id), so the outcome does not depend on thread timing (R11).

### 9.4 Abstract simulation (T3, Pillar 2)
Unloaded regions keep populations, territories and migrations as per-region numbers stepped at about 0.1 Hz. When a chunk loads, creatures are instantiated from the population model with seeded minds; bonded or remembered individuals are **always** persisted as full minds, never abstracted away.

---

## 10. Bonding Contract v1.0.0 (R28, R29)
Every creature in the game follows this contract. It is versioned with semantic versioning; a change to it requires an ADR, a save migration and an update of this section (R50).

### 10.1 Ownership states
```
Wild ──Pact accepted──► Pacted ──assigned──► SanctumResident
  ▲                      │  ▲                    │
  │                      │  └──recalled──────────┘
  └──Released / Left─────┤
                         ├──Re-pacted (consent)──► Pacted (new owner)
                         └──Died──► Deceased (Hardcore) / Fallen (Normal: revivable)
```
| Transition | Validated by the server | Memory written |
|---|---|---|
| Wild → Pacted | player in range, Pact accepted (§10.2), Bond Capacity not exceeded | `PactOffered`, then a Pact core memory |
| Pacted → Released | owner action | `Released` (valence by Affection) |
| Pacted → Left | Trust < −50 for 3 in-game days, or 3 broken terms | `PactBroken`; may turn hostile if `Temper` > 40 |
| Pacted → Pacted (re-pact) | both players agree **and** the creature accepts the new player (same rule as a Pact) | memories travel with it |
| → Deceased / Fallen | death; server preset decides (GDD §9) | `BondedDied` for its group and bonded companions |

A creature has **exactly one owner** at a time. Ownership changes are atomic, server-side and idempotent (R27, R34): they share the trading system's transaction path.

### 10.2 Pacts and Pact Terms
- A Pact offer contains **1–3 terms** from the term catalogue (data): `FeedDaily`, `TerritoryNearSanctum`, `ShareOfLoot`, `FreedomAtNight`, `NoFightingKin`, `ProtectYoung`.
- **Acceptance utility:** `accept = Σ termValue(term, personality, needs) + Trust + Respect + reputation − Fear penalty − Grudge`. Accepted when ≥ the species threshold. **No random roll decides it** (GDD §1, R15). Refusal is shown with an emote and is remembered (a pushy player loses trust).
- Terms are checked by the server at fixed times (for example once per in-game day). Kept terms write `PactTermKept`; broken terms write `PactBroken`.

### 10.3 Bond levels
| Level | Name | Requirement (Trust & Affection) | Unlocks (data) |
|---|---|---|---|
| 0 | Wary | Pact just accepted | follow, basic commands |
| 1 | Accepting | ≥ 20 | guard, work at Sanctum |
| 2 | Companion | ≥ 40 | combo learning, second ability |
| 3 | Trusted | ≥ 60 | autonomous protection, Resonance bonus (GDD §4) |
| 4 | Devoted | ≥ 80 + one core memory with the owner | signature ability, protects your grave |
| 5 | Soulbound | ≥ 95 + 2 core memories + 30 in-game days | bond story moment, unique ability |

Bond level can **go down**. It is recalculated once per in-game day and immediately after a core memory.

### 10.4 Bond Capacity and summoning
- Bond Capacity (from the player's `Will` attribute and Summoner talents) limits **active summons**, not the number of Pacts. Starting value 2, cap 5 at EA.
- Summoning costs mana and uses the sigil cast; it is a validated intent. A summoned creature is always AI LOD T0.

### 10.5 Creature progression
- Creatures gain **experience per role** (combat, work, travel) and level up to a species cap. Level gives stats; **bond level gives abilities**. You cannot buy either (DEC-002).
- Stat formulas are part of the shared combat contract (R28) and live in data; they will be specified with the combat system.

### 10.6 Contract interfaces (Core)
`IBondingRules` (state transitions and validation), `IPactEvaluator` (acceptance), `IObedienceModel` (command responses), `ICreatureMindStore` (memories and opinions). Names are reserved in the system registry (M0-06).

---

## 11. Generative AI policy (R16)
- **No generative AI model (LLM, image, voice) runs in any runtime gameplay loop**: not on the server, not on the client, not asynchronously. All creature behaviour comes from the deterministic systems in this document.
- Reasons: determinism and replay (R11), server cost (DEC-002 has no paid servers), latency, moderation risk, and the market finding that about 31% of engaged Steam players react negatively to generative AI.
- Allowed **offline** only: internal brainstorming and test-data generation, disclosed in the PR. Nothing generated ships as an asset (DEC-002).
- Any future exception needs an ADR and stakeholder approval.

---

## 12. Data files and validation
| File | Contents |
|---|---|
| `data/ai/species/*.json` | trait means/spreads, needs rates, enabled goals, tree ids, group type, Pact threshold |
| `data/ai/memory_event_types.json` | §4.2 table |
| `data/ai/opinion_weights.json` | event type → attitude weights |
| `data/ai/goals.json` / `considerations.json` | §7 goals, inputs, curves |
| `data/ai/bt/*.json` | behaviour trees |
| `data/ai/pact_terms.json` | §10.2 term catalogue |
| `data/ai/bond_levels.json` | §10.3 table |

Every file has a `schemaVersion` and is validated at load (unknown ids, ranges, cycles in trees). A validation failure stops the server with a clear error; it never runs with half-loaded AI data.

---

## 13. Testing & debugging
- **Determinism test:** run a seeded scenario twice (and with 1 vs. N worker threads) and compare a hash of all creature minds per tick (R11).
- **Golden behaviour tests:** scripted scenarios, for example "feed 5 days → bond level 1", "break a term 3 times → creature leaves", "kill a herd member in view → herd avoids the player", "a rumour is never passed on twice".
- **Property tests:** capacities are never exceeded; salience stays in range; decay is monotonic.
- **Save tests:** every `CreatureMind` schema version has a fixture that must load and migrate (R26).
- **Perf:** the M0-07 benchmark (200 creatures) measures AI ms/tick per tier and allocations/tick (target: 0 in steady state).
- **AI Inspector (dev build only):** select a creature to see traits, top memories, opinions, goal scores and the active tree node. Without this tool the USP cannot be tuned.

---

## 14. Open questions (resolve by the M1 gate)
1. Server tick rate (30 Hz working value) and in-game day length → TDD M0-05.
2. Should memories of *other players* be visible in the owner's Bond Lens (privacy/griefing)?
3. Rumour sharing between groups of different species (predator learns from prey's fear)? Proposal: no in EA.
4. Can a creature that left (Pacted → Left) be re-pacted by the same player? Proposal: yes, but only after a `SavedLifeOf` or `ProtectedYoung` memory.
5. Breeding (GDD open question 3) depends on this contract; it would be Bonding Contract v1.1.

## Changelog
- **0.1 (2026-10-19):** First version. Memory model, opinions, emotions/emotes, utility + BT decision architecture, AI LOD budgets, Bonding Contract v1.0.0, generative-AI policy.
