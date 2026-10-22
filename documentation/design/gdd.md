# Sumoner World - Game Design Document
Version: **0.4** · Date: 2026-10-21 · Owner: Sr Game Designer · Status: **Approved by stakeholder** (0.1 on 2026-10-19: PvE default, permanent creature death on Hardcore, 16-player target; 0.2 on 2026-10-21: summoned companions, §10; 0.3 on 2026-10-21: Summoner Academy, rank and faction, §11; Normal preset default; 0.4 on 2026-10-21: levels and rank caps, class-bound creatures, loot and gold, boss eggs)
Rules: R28, R29, R50, R53, R57 · Related: `DEC-002` (monetization/art), `ADR-001` (tech), AI Bible v0.1 (`systems/ai_bible.md`), TDD v0.1 (`../technical/architecture/tdd.md`)

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

### First hour: the Summoner Academy (§11)
A new player arrives as a student at the Summoner Academy → the **ranking quest** asks them questions and sets their **class** (for life) and **Summoner Rank** → they receive a **guaranteed young creature of their class** → they leave for the wild with their first companion.

### Long-term loop: Bonds & World
Bond arcs with individual creatures (trust milestones unlock abilities and story moments) → **rank trials** at the Academy (§11) → mastery of magic schools → the world state changes because of server-wide player actions → new regions (EA updates / expansions).

---

## Game Systems (v0.1 design intent; detailed specs to follow in `systems/`)

### 1. Pacts (creature acquisition, the "Summoner" fantasy)
Creatures are **never captured**. The player proposes a **Pact**, a magical contract the creature accepts or refuses.
- **How to approach:** earn trust (feed, help, protect its young, show strength by defeating it without killing it) or meet its needs.
- **Pact Terms:** the player offers terms (food, a protected territory near the Sanctum, a share of loot, freedom on certain nights). The creature's personality decides which terms it values.
- **Acceptance** is a utility decision based on the creature's memory of *this* player (and rumours from its group), its personality, its current needs and the player's reputation. It is never pure RNG (R15).
- **Breaking terms** damages trust. Repeated breaking can make a bonded creature leave, or turn against the player.
- **Creatures belong to a class** (stakeholder 2026-10-21): every species belongs to one summoner class (§11). **A Pact with a
  creature of another class always fails** (for example a Necromancer can never bond the Elves' light creatures). Within the
  own class, Pacts follow the normal rules.
- **Level difference matters** (stakeholder 2026-10-21): creatures well below the player's level accept a Pact much more
  easily, creatures above it much less. *(filled numbers)* A creature at or below 90% of the player's level gets a bonus to its
  acceptance score that grows as the gap grows; at or below 50% it accepts almost at once (if it does not hate the player); above
  the player's level it gets a penalty.
- **Where new companions come from (stakeholder 2026-10-21):** the first one from the Academy's Bonding Ceremony (§11); after that
  **in the wild** (Pacts), **through quests** (a quest leads to a creature, for example one to rescue or a guardian to prove
  yourself to), **through world events** (migrations, awakenings), **by trading** with other players (only with the creature's
  consent; its memories go with it, §8) and with the help of **special Pact items** (offerings, sigil inks, scents that meet a
  species' needs or terms). In every case the creature itself accepts or refuses. Items help you meet its terms; they never capture
  it, are never thrown at it, and never show a chance (M0-08).
- **Summoning:** bonded creatures live in the world or at the Sanctum. The player **calls** them through a sigil (it costs mana; there is a limit on active summons = **Bond Capacity**, which grows with mastery).
- Ownership, bonding, progression and command rules are a single **versioned contract** shared by every creature (R29). It is defined in the AI Bible.
- **IP guardrails (M0-08, binding):** no thrown or launched object to acquire or summon a creature; no acceptance or capture probability shown while targeting; **one continuous combat mode** (no separate battle state started by a summon); mounts need a patent review before they are designed. See `documentation/production/reports/M0-08_legal_naming_check.md`.

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
- **Lock-on** (stakeholder 2026-10-21): the camera can lock on to an enemy so attacks and combos aim at it; **not on Hardcore
  servers**. See the combat spec §11a.
- **Summoner tactics:** quick commands (attack target, guard me, hold, retreat, combo with my spell). How well a command is carried out depends on bond, trust and the creature's personality.
- **Combos with creatures:** player spell + creature ability combos (e.g. a frost spell freezes a target and a heavy creature shatters it). Combos are learned by the creature through repetition (Pillar 1 + 3).
- All combat values are data-driven and validated on the server (R08, R30).

### 4. Magic
- **4 schools at EA** (working names): **Primal** (nature, growth, beasts), **Storm** (lightning, wind, mobility), **Ember** (fire, ruin, wards) and **Umbral** (spirits, shadow, binding; the Necromancers' school, added in v0.3, §11). More schools come with expansions.
- **Resonance:** the creatures you bond with strengthen related schools and unlock spell variants. Your party composition shapes your build.
- Spells are crafted from **runes** (data-driven components), so the system can grow without core logic changes (R08, R09).

### 5. Character Progression
- **Levels** (stakeholder 2026-10-21): players gain experience and levels. The **highest level a player can reach is set by
  their Summoner Rank** (§11): the higher the rank, the higher the cap. Ranking up raises the cap.

  | Rank | F | E | D | C | B | A | S | SS | SSS | God |
  |---|---|---|---|---|---|---|---|---|---|---|
  | Level cap *(filled)* | 10 | 20 | 30 | 40 | 50 | 60 | 70 | 80 | 90 | 100 |
- Attributes (Vitality, Stamina, Focus, Will). **Will** = Bond Capacity and Pact persuasion.
- Talent trees per magic school + a Summoner tree (commands, Pact options, Bond Capacity). A skill tree is part of the first
  version (M2).
- Gear power has a ceiling, so skill still matters more than numbers (Pillar 3).
- **Never pay-to-win** (stakeholder 2026-10-21, DEC-002): levels, ranks, creatures, items and gold can never be bought.

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
- **Bonded creatures can be *re-pacted* to another player only with the creature's consent.** Its memories go with it. Only to a player of the creature's class (§1, §11).
- **PvP stance:** **PvE by default.** PvP is a server setting (off / zones only / full). **No offline raiding:** bases are invulnerable while their owners are offline. This removes ARK's worst pain point.

### 9. Death & Difficulty (hardcore)
- **On death:** you respawn at the Sanctum or a shrine. Your carried inventory stays at a **grave** you must go back for (Valheim-style). Your bonded creatures react to your death based on their bond: they defend your grave, flee or go wild.
- **Creature death:** on **Normal** servers (the default) a fallen companion can be revived at a cost. On **Hardcore** servers creature death is permanent. Either way, other creatures remember the loss and grieve.
- **Server presets:** *Normal (default, stakeholder 2026-10-21) / Hardcore / Custom.* All values are data-driven (R08). Hardcore
  also turns off lock-on (§3).

### 10. Summoned Companions (stakeholder direction, v0.2)
The end goal: **a Summoner's creatures are intelligent companions that can do everything the player can**: fight, explore, take
part in quests and dungeons, gather and craft. They are upgradable, have tiers, can fuse, and each grows its own skill tree and
its own evolving personality. Creatures are partners, not tools. They help because of the bond, and what they do still depends on
their personality and memory (§2), never only on stats.

- **What companions can do (all four, stakeholder 2026-10-21):**
  1. **Combat and magic:** attack, defend, cast their species' spells and combo with the player (§3, §4).
  2. **Exploration and quests:** scout ahead on their own, find things, carry out parts of quests (fetch, guard, escort) and go
     into dungeons with the player.
  3. **Gathering and crafting:** collect resources and work in the Sanctum, *if they want to* (§6).
  4. **Equipment:** wear gear suited to their body (weapons, armour, charms), like the player. Gear power keeps its ceiling
     (Pillar 3), and gear is never sold for money (DEC-002).
- **Levels and life stages** (stakeholder 2026-10-21): companions grow through **levels** and three life stages, **young,
  grown and elder**. Creatures are magical and **never die of old age**.
- **A creature's tier sets its highest level** (as rank does for players, §5). A companion's tier is tied to its summoner's rank:
  it can be raised as the summoner ranks up, never above the summoner's rank. *(filled: the details in the companions spec)*
- **World boss eggs** (stakeholder 2026-10-21): a world boss can **drop an egg** when certain conditions are met. The egg
  hatches into a **young boss creature with lower stats** that grows like any companion. *(filled conditions: the boss is of
  the player's class, the player took a real part in the fight, and the player's rank is high enough; the drop itself is a
  chance, never for sale.)*
- **Tiers, both kinds (stakeholder 2026-10-21):**
  - **Rarity class per species** (some species are rarer and stronger by nature). Rarity comes from the world (where and when a
    species lives), never from paid or random-reward items (DEC-002), and acceptance chances are never shown (M0-08).
  - **Growth stages per creature:** each creature grows stage by stage (working target: 3 stages) through experience and bond
    level, and its look changes with each stage. Stages are earned, never bought.
- **Skill trees per creature:** every species has a tree. Each creature's path through it is shaped by what it has lived:
  personality traits, the roles it has done most (combat, work, travel, AI Bible §10.5), the combos it has learned with the player
  (§3), and bond level (which already gates abilities). Two creatures of the same species should end up different.
- **Fusion (stakeholder 2026-10-21): both creatures must agree, and their memories merge.**
  - Fusion is offered like a Pact (§1): **both creatures must trust the player and accept**. A creature can refuse, and being
    pushed into it is remembered.
  - The fused creature **keeps the most important memories of both** (core memories, and memories about the player and others),
    and its personality is a blend of the two. It remembers being two, and can show it (grief, recognising the other's friends).
  - Fusion is permanent. Its rules are part of the versioned Bonding contract (R29) and validated by the server like any
    transaction (R27).
- **Delivery:** these systems come **after M1** (M1 proves the technology). Their detailed specs go into the AI Bible (§10
  Bonding contract) and new `systems/` documents before they are built.

### Open questions for §10 (decide before building)
1. **Fusion legal check.** Creature fusion exists in other monster games. Like mounts (M0-08), the design needs an IP/patent review
   before it is detailed.
2. **Fusion inputs and result:** same species only, or any two? What decides the resulting species and stage? Can a fused creature
   fuse again?
3. **Memory merge limits:** the mind store has fixed capacity (AI Bible §5). Rule needed for which memories survive when both are
   full (proposal: all core memories, then the most salient episodic ones).
4. **Rarity classes:** how many and their names, and how rarity shows in the world without a "capture chance" display.
5. **Stage count and caps** per species, and how equipment fits each body type.
6. **Dungeons and quests:** quests v1 are in `systems/quests.md` (M2-19, draft 0.1); dungeons and how companions take part in
   quests have no design yet.

### 11. Summoner Academy, Rank and Class (stakeholder direction, v0.3, revised v0.4)
Every new character starts as a **student who enters the Summoner Academy**. The first quest, the **ranking quest**, sets the
student's **class** and **Summoner Rank** and ends with a **guaranteed first creature** of that class that the player can summon.
Items marked *(filled)* were filled in by the design team at the stakeholder's request and can be changed.

**The ranking quest (stakeholder 2026-10-21).** The Academy asks the student a series of questions with answer choices (how they
would act, what they value, what they would do in a fight or for a creature). **The answers, plus some randomness, decide the
class and the rank.** The details are in `systems/academy.md`. The quest still teaches the basics on the way (moving, a first
fight, reading a creature's body language, a Pact) *(filled)*.

**Class: for life.** The class is set once and **can never be changed**: an elf stays an elf. A class is a summoning tradition with
its own people, magic school and creatures. **Creatures are bound to classes** (§1): a player can only make Pacts with creatures of
their own class.

| Class (working names) | Tradition | Magic school | Creatures (examples) |
|---|---|---|---|
| **Elf** | forest, light and beast | Primal | pixie, young mossback, bogfox kit, hollow owl |
| **Human** *(filled)* | discipline and formation | Storm | storm-hawk, plains hound *(new)* |
| **Necromancer** | restless spirits; their creatures remember a past life | **Umbral** (4th school) | undead skeleton, wisp-hound *(new)* |
| **Dwarf** *(proposed, filled)* | forge and fire | Ember | ember-salamander *(new)* |

- Necromancer creatures are friendly-spooky, not gory, so they suit the family's youngest players. *(filled)*

**Summoner Rank.** **F, E, D, C, B, A, S, SS, SSS and God tier.** The ranking quest gives the first rank. Through the game the
player **raises their rank** (rank trials, quests, deeds) but never changes class. Rank sets the player's **level cap** (§5),
**Bond Capacity**, and which quests and regions open. It can never be bought.

**The guaranteed first creature.** At the end of the quest the student receives a young creature of their class, **of a tier that
matches their rank**, chosen by the answers *(filled: the Keeper offers it, and the student offers a starter Pact that it always
accepts, so it still begins as a real bond with a first core memory)*. The student can summon it from then on.

**Delivery.** M2: the Academy, the ranking quest, ranks F–C *(filled: a newcomer starts between F and C)*, and **one playable
class (Elf, Primal)**, with the others shown as coming. More classes, rank trials and the Umbral school come in M3.

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
**Performance targets**: 60 FPS on minimum (Low + FSR) and recommended (High) spec; initial join ≤ 20 s, respawn and in-world transitions ≤ 3 s (TDD §1)
**Simulation**: deterministic 30 Hz fixed tick, server-authoritative, AI LOD; in-game day = 48 real minutes (TDD §2)

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
2. How much can a creature's personality change: slow drift or milestone-based? *Resolved (AI Bible v0.1 §3, approved): slow drift from core memories, capped at ±25.*
3. Can creatures breed? The proposal is yes, with inherited traits (not memories). This needs the Bonding contract first.
4. Expansion story arcs (post-1.0).

## Changelog
- **0.1 (2026-10-19):** First version. Pillars, loops, system intent, content targets, monetization and art references.
- **0.1 approved (2026-10-19):** Stakeholder approved via PR #1 merge, including PvE-by-default, permanent creature death on Hardcore servers, and the 16-player server target (still to be validated at M1).
- **0.4 (2026-10-21):** stakeholder decisions: player levels with rank caps (§5); creatures bound to classes, Pacts with other
  classes always fail, level difference eases or hardens Pacts (§1); companion levels and young/grown/elder stages, no death of
  old age, creature tier tied to the summoner's rank, world boss eggs (§10); the ranking quest decides class and rank from
  answers plus randomness, class is for life, a guaranteed first creature of the class (§11); a first loot system with gold,
  item drops and inventory (crafting spec); never pay-to-win.
- **0.3 (2026-10-21):** §11 Summoner Academy, Arena Trial, ranks F–God tier, factions (Elves, Humans, Necromancers, Dwarves proposed) and the Bonding Ceremony; the ways to get new companions (§1); Normal is the default preset (§9). Gaps filled at the stakeholder's request and marked *(filled)*.
- **0.2 (2026-10-21):** §10 Summoned Companions, from the stakeholder's direction: companions can do everything the player can (combat and magic, exploration, quests and dungeons, gathering and crafting, equipment); tiers as both species rarity and per-creature growth stages; per-creature skill trees; fusion by mutual consent with merged memories. Open questions listed. Hitting creatures stays in the game (stakeholder, 2026-10-21).
