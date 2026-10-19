# Sumoner World - Game Design Document
Version: **0.1** (M0-03) · Date: 2026-10-19 · Owner: Sr Game Designer · Status: **Approved by stakeholder 2026-10-19** (PvE default, permanent creature death on Hardcore, 16-player target confirmed)
Rules: R28, R29, R50, R53, R57 · Related: `DEC-002` (monetization/art), `ADR-001` (tech), AI Bible v0.1 (`systems/ai_bible.md`), TDD (M0-05, pending)

> All numbers in this document are **design targets or starting values**. Real tuning values live in versioned data files (R08, R48), not in code or here.

## Overview
**Concept**: Sumoner World is an immersive open-world fantasy RPG. Players explore a living, evolving world, bind and bond with intelligent creatures, master magic, build their characters and shape their own destiny through combat, exploration, crafting and dynamic interaction with a living ecosystem.
**Genre**: Open-world survival-RPG with creature bonding
**Platform**: PC (Steam) · **Engine**: Godot 4.7.2 .NET
**Audience**: Core / hardcore PC players (18–35) who play ARK, Valheim, Palworld or souls-likes and want creatures that matter
**Multiplayer**: Shared persistent world on player-hosted dedicated servers + host-your-own co-op. Server-authoritative (R30).
**One-line pitch**: *"Every creature remembers you."*

---

## Design Pillars
Every feature must serve at least one pillar. Pillar 1 wins every conflict (Producer Best Practice #7: protect differentiation).

### Pillar 1: Every Creature Remembers
Creatures are not items. Each one has a personality, memories of what you and others did, and opinions that change its behaviour. Trust is earned, can be lost, and shows in how the creature moves, fights and obeys. **Test:** does this feature create, use or show a creature memory or relationship?

### Pillar 2: A World That Lives Without You
The ecosystem simulates on its own. Predators hunt, herds migrate, territories shift and creature groups remember players across the server. Actions have persistent consequences (over-hunting, burning a forest, protecting a nest). **Test:** does this feature make the world react, persist or change?

### Pillar 3: Earned Mastery
Hardcore and fair. Combat and magic are skill-based. Death costs something. Knowledge of creature behaviour is the most powerful tool a player has. **Test:** does this feature reward skill, preparation or understanding, never just time or money?

### Anti-pillars (what we are NOT)
- Not a "collect 150 creatures" game. **Deep over broad**: fewer species, each with rich behaviour.
- No guns, no pokéballs, no throw-to-capture (IP/patent risk and tone).
- Not pay-to-win. No paid creatures (DEC-002).
- No offline raiding of bases (see PvP).

---

## Core Gameplay Loop

### 30-second loop: Read → Act → React
Read the situation (creature body language, terrain, threats) → act (strike, dodge, cast, command a bonded creature) → **the creature reacts according to its personality and memory** (obeys, improvises, hesitates, protects you, flees). The player sees the result and adapts.

### 5-minute loop: Encounter
Travel through a region → an ecosystem event happens (hunt, territorial fight, a wounded creature, a migrating herd, a hostile pack) → choose: fight, avoid, help or **offer a Pact** → gather resources or knowledge → keep moving or retreat.

### Session loop (60–120 min): Expedition
Set a goal at the Sanctum (rare species, new biome, boss, materials) → prepare (craft gear, prepare spells, **choose which bonded creatures come along**; they have opinions about each other) → expedition → return, tend bonds (feed, rest, train, resolve conflicts) → craft and upgrade.

### Long-term loop: Bonds & World
Bond arcs with individual creatures (trust milestones unlock abilities and story moments) → mastery of magic schools → the world state changes because of server-wide player actions → new regions (EA updates / expansions).

---

## Game Systems (v0.1 design intent; detailed specs to follow in `systems/`)

### 1. Pacts (creature acquisition, the "Summoner" fantasy)
Creatures are **never captured**. The player proposes a **Pact**, a magical contract the creature accepts or refuses.
- **How to approach:** earn trust (feed, help, protect its young, show strength by defeating it without killing it) or meet its needs.
- **Pact Terms:** the player offers terms (food, a protected territory near the Sanctum, a share of loot, freedom on certain nights). The creature's personality decides which terms it values.
- **Acceptance** is a utility decision based on the creature's memory of *this* player (and rumours from its group), its personality, its current needs and the player's reputation. It is never pure RNG (R15).
- **Breaking terms** damages trust. Repeated breaking can make a bonded creature leave, or turn against the player.
- **Summoning:** bonded creatures live in the world or at the Sanctum. The player **calls** them through a sigil (it costs mana; there is a limit on active summons = **Bond Capacity**, which grows with mastery).
- Ownership, bonding, progression and command rules are a single **versioned contract** shared by every creature (R29). It is defined in the AI Bible.

### 2. Creature Mind (USP core; full spec in `systems/ai_bible.md`)
- **Personality:** a few trait axes (e.g. bold↔timid, loyal↔independent, curious↔wary, aggressive↔gentle). These are inheritable and slowly shaped by experience.
- **Memory:** episodic events with salience and decay (*"you saved me"*, *"you let my packmate die"*, *"you broke our pact"*, *"that player hunted my kind"*). Memories **persist in saves** (R14, R26).
- **Opinions:** of players, other creatures and places. These drive obedience, positioning, initiative and refusal.
- **Learning:** creatures adapt combat tactics to enemies they've fought often and learn the player's habits (e.g. start flanking on their own when you always open with a fire spell).
- **Autonomy:** creatures act on their own when it fits their character: they defend you unprompted, steal food, refuse a suicidal command or protect your gravestone.
- **Readability:** emotion and memory must be shown through animation and behaviour first, UI second (DEC-002).
- **Tech guardrails:** utility AI + behaviour trees, AI LOD, spatial partitioning, configurable tick rates. No generative AI in runtime loops (R13–R19).

### 3. Combat (real-time action)
- Stamina-based melee + spellcasting. Directional dodge and block/parry. Readable enemy telegraphs (souls-lite).
- **Summoner tactics:** quick commands (attack target, guard me, hold, retreat, combo with my spell). How well a command is carried out depends on bond, trust and the creature's personality.
- **Combos with creatures:** player spell + creature ability combos (e.g. a frost spell freezes a target and a heavy creature shatters it). Combos are learned by the creature through repetition (Pillar 1 + 3).
- All combat values are data-driven and validated on the server (R08, R30).

### 4. Magic
- **3 schools at EA** (working names): **Primal** (nature, growth, beasts), **Storm** (lightning, wind, mobility) and **Ember** (fire, ruin, wards). More schools come with expansions.
- **Resonance:** the creatures you bond with strengthen related schools and unlock spell variants. Your party composition shapes your build.
- Spells are crafted from **runes** (data-driven components), so the system can grow without core logic changes (R08, R09).

### 5. Character Progression
- Attributes (Vitality, Stamina, Focus, Will). **Will** = Bond Capacity and Pact persuasion.
- Talent trees per magic school + a Summoner tree (commands, Pact options, Bond Capacity).
- Gear defines the build, not grinding levels. Gear power has a ceiling, so skill matters (Pillar 3).

### 6. Crafting & Sanctum
- Gather from the ecosystem: plants, ore, and creature materials (*harvesting creatures that can remember has consequences*: herds that see a player hunting will avoid that player).
- Craft weapons, armour, runes, food and Pact offerings.
- **Sanctum:** a player-built base where bonded creatures live. Their happiness depends on space, food, compatible roommates and how you treat them. Creatures help with work based on their skills **if they want to** (contrast with Palworld's forced labour).
- Inventory, crafting and economy actions are atomic, server-validated and idempotent (R27, R34).

### 7. Living Ecosystem
- Each species has a role: predator, grazer, scavenger, spirit or apex.
- Population, territory and migration are simulated at low frequency (AI LOD).
- **Server memory:** groups of creatures remember notable players server-wide (reputation per species/region).
- **World events:** droughts, migrations, apex-predator awakenings. Deterministic and seeded (R11, R25).

### 8. Multiplayer, Economy & PvP
- **Server size target:** up to 16 players per server at EA (to be validated at the M1 gate).
- **Co-op:** shared world. Bonded creatures **remember other players** too, so a friend who mistreats your creature will find out.
- **Trading:** player-to-player trades and player-run market stalls in Sanctums. Server-authoritative, atomic, with anti-duplication (idempotency keys) (R30–R34).
- **Bonded creatures can be *re-pacted* to another player only with the creature's consent.** Its memories go with it.
- **PvP stance:** **PvE by default.** PvP is a server setting (off / zones only / full). **No offline raiding:** bases are invulnerable while their owners are offline. This removes ARK's worst pain point.

### 9. Death & Difficulty (hardcore)
- **On death:** you respawn at the Sanctum or a shrine. Your carried inventory stays at a **grave** you must go back for (Valheim-style). Your bonded creatures react to your death based on their bond: they defend your grave, flee or go wild.
- **Creature death:** **permanent by default** on Hardcore servers (other creatures remember the loss and grieve). Normal servers allow revival at a cost.
- **Server presets:** *Normal / Hardcore (default) / Custom.* All values are data-driven (R08).

---

## Content Specifications (EA target, v0.1)
| Content | EA target | Notes |
|---|---|---|
| Regions / biomes | 1 region, 3 biomes (forest, highlands, marsh) | Plus 1 hidden spirit area |
| Creature species | **20–25** | Deep behaviour > count. 4 shared rig types. |
| Bosses / apex creatures | 4 | Each one can be bonded, under very hard Pact conditions |
| Magic schools | 3 | Primal, Storm, Ember |
| Playtime | 20–40 h | Per market report |
| Story | Light framing: "the broken Pacts", a world where summoners once betrayed creatures | Gives Pacts emotional weight |

Detailed content lists: Mid Game Designer, in `documentation/design/content/` (later M0/M1).

---

## Technical Requirements
**Engine**: Godot 4.7.2-stable .NET (C#) · **Architecture**: ADR-001
**Performance targets**: 60 FPS on recommended spec, < 3 s load (target hardware defined in TDD, M0-05)
**Simulation**: deterministic fixed tick, server-authoritative, AI LOD

## Art Direction
"Mythic Low-Poly": atmospheric low-poly dark fantasy, where lighting carries the beauty and creature emotion is shown through animation. Full rules in DEC-002; style guide in M0-09.

## UI/UX Design
Minimal HUD. Creature state is shown through body language first. Optional "Bond Lens" UI (hold a key) shows mood, trust and recent memories. All strings are localized (R39). UI is data-driven and accessible (R37). Spec owner: UI/UX Agent.

## Audio Design
Creatures have vocal "moods" that match their emotes (trust, fear, grudge, curiosity, affection): audio supports readability (Pillar 1). Spec to come.

## Monetization Strategy
Premium early access at **$24.99** → 1.0 at **$29.99** → paid region expansions + account-bound cosmetic supporter packs. Player-hosted servers. **No pay-to-win, no paid creatures, no loot boxes, cosmetics not tradeable.** (DEC-002)

## Success Metrics
1. **USP readability:** ≥ 70% of playtesters notice a creature remembering them within 10 minutes (M1 gate)
2. **Engagement:** median session ≥ 60 min. ≥ 40% of EA buyers play 10+ hours.
3. **Reception & market:** ≥ 80% positive Steam reviews. ≥ 100K wishlists before EA.
4. **Technical:** 60 FPS with 200 active creatures on recommended spec (M1 gate)

---

## Open Design Questions (to be resolved by M1)
1. Server size: 16 players. Validate against the network/sim budget in M1.
2. How much can a creature's personality change: slow drift or milestone-based? *AI Bible v0.1 proposes slow drift from core memories, capped at ±25 (§3).*
3. Can creatures breed? The proposal is yes, with inherited traits (not memories). This needs the Bonding contract first.
4. Expansion story arcs (post-1.0).

## Changelog
- **0.1 (2026-10-19):** First version. Pillars, loops, system intent, content targets, monetization and art references.
- **0.1 approved (2026-10-19):** Stakeholder approved via PR #1 merge, including PvE-by-default, permanent creature death on Hardcore servers, and the 16-player server target (still to be validated at M1).
