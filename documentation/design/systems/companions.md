# Sumoner World - Companion Progression v1 (D2)
Version: **0.2** (M2 D2) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Approved by stakeholder
2026-10-21** (with the changes in the changelog)
Rules: R08, R15, R26, R27, R28, R29, R50 · Related: GDD v0.4 §1, §2, §5, §10, §11; AI Bible v0.1 §8 (learning), §10 (Bonding
contract v1.0.0); combat spec v0.4 (D1)

> Extends the Bonding contract to **v1.1.0** (AI Bible §10): classes, levels and tier caps, life stages, skill trees, equipment,
> boss eggs and commands v1. Fusion and species rarity are **not** in v1.1 (GDD §10 open questions, M3). All numbers are starting
> values in data (R08). Items marked *(filled)* were filled in by the design team at the stakeholder's request.

---

## 1. Principles
1. **Earned, never bought** (DEC-002, Pillar 3; stakeholder: "never pay-to-win"). Levels, stages, tiers, skills and gear come from
   play.
2. **Shaped by the life it lived.** Two creatures of the same species end up different, because what they did, how they were
   treated and their personality steer their growth (GDD §10).
3. **Stats come from level and stage; abilities come from bond and the skill tree** (AI Bible §10.5). A strong creature that does
   not trust you is still hard to command (obedience, AI Bible §10.7).
4. **Bound to a class** (stakeholder): every species belongs to one summoner class (GDD §11); a creature only ever bonds with a
   summoner of that class.

## 2. Classes and Pacts (Bonding contract v1.1.0)
- Every species has a `class` in its data (`elf`, `human`, `necromancer`, `dwarf`; the slice's mossback, bogfox and hollow owl
  are `elf`).
- **A Pact offer to a creature of another class always fails**, before any scoring, and the creature remembers the attempt like a
  refused Pact (`PactRefused`). The same holds for re-pacting a companion to a player of another class.
- **Level difference** adds to the Pact acceptance score (AI Bible §10.7), from the creature's level `c` and the player's level
  `p` *(filled numbers)*:

| Creature level | Score change |
|---|---|
| `c ≤ 0.5 × p` | +600 (almost always accepts, unless it holds a grudge or core memory against the player) |
| `0.5 × p < c ≤ 0.9 × p` | from +100 at 90% up to +400 near 50%, straight line |
| `0.9 × p < c ≤ p` | 0 |
| `c > p` | −30 per level above the player |

  Example from the stakeholder: a level 100 player finds creatures below level 90 easier, below 50 almost immediate, above 100
  hard.

## 3. Experience, levels and tier caps
- A pacted creature earns **experience per role**:

| Role | Earned by (starting values) |
|---|---|
| **Combat** | damage dealt + damage taken in fights at the owner's side (1 XP per 2 points), +20 per defeated wild creature |
| **Work** | Sanctum tasks and gathering: +5 per task finished |
| **Travel** | +1 per 50 m walked while summoned; +10 for each new place found when scouting |

- **Level** = from the **total** experience, on a curve in data (`data/companions/levels.json`): level `n` needs
  `40 × (n − 1)^1.5` total XP (rounded): level 2 at 40 XP, level 5 at 320, level 10 at 1,080.
- **The creature's tier sets its highest level** (stakeholder), with the same scale as the players' rank caps (GDD §5):
  tier F 10, E 20, D 30, C 40, B 50, A 60, S 70, SS 80, SSS 90, God 100.
- **Tier is tied to the summoner's rank** (stakeholder): a companion's tier can never be above its summoner's rank. When the
  summoner ranks up, a companion can be **raised one tier** at the Academy by a tier trial with its summoner *(filled: a quest
  done together, never bought)*. Wild creatures have their species' tier in data.
- **Each level:** +6% health, +4% power over the species' base (data per species). Stamina, guard and resistances come from the
  life stage and gear.

## 4. Life stages (young, grown, elder)
| Stage | Reached at *(filled numbers)* | Changes |
|---|---|---|
| **Young** | Pact, hatching (§7) or the Academy | base look |
| **Grown** | 40% of its tier's level cap **and** bond level ≥ 2 (Companion) | bigger body and markings, +20% health, +1 skill slot |
| **Elder** | 80% of its tier's level cap **and** bond level ≥ 4 (Devoted) | signature look, +20% power, the species' signature skill |

- **Creatures are magical and never die of old age** (stakeholder). Elder is the peak of a creature's life, not its end.
- **Growing up is a moment.** When the conditions are met, the creature grows during the next rest at the Sanctum or by a fire,
  and remembers it as a core memory with the owner (`GrewUp`, new memory event type).
- A creature whose bond falls below the requirement keeps its stage, but cannot reach the next one until the bond recovers.

## 5. Skill trees (in the first version)
- **One tree per species** in data (`data/companions/skills/<species>.json`), about **9 nodes in 3 branches** for slice species.
  Each node has: id, branch, required level, required bond level, required stage, prerequisites, and what it gives (a move from the
  combat data, a passive, or a behaviour).
- **Skill points:** 1 per 2 levels. **Slots** (active skills at once): 2, +1 when grown, +1 when elder.
- **The player chooses; the creature's character makes some choices cheaper or dearer** (approved). Each branch has an affinity
  from the creature's traits, the roles it did most, its learned combos and its memories:
  - a node in a branch with **positive affinity** costs 1 point; neutral 1 point; **negative affinity costs 2 points** and the
    creature shows reluctance (emote);
  - when the player has not spent points for 3 in-game days, the creature **picks for itself** from its highest-affinity branch.
- **Respec:** at the Academy, for gold (crafting spec), never real money.
- **Slice trees (examples):** mossback *Vanguard / Guardian / Earthshaker*; bogfox *Scout / Trickster / Pack*; pixie *Light /
  Mend / Mischief* *(filled)*.

## 6. Equipment
- Slots by body type (data): **quadruped**: head, body, charm; **avian**: head, charm; **biped**: weapon, head, body, charm;
  **serpentine**: body, charm.
- Gear for creatures comes from crafting and drops (crafting spec). It adds stats and sometimes a passive, never an ability
  the skill tree would give.
- **Comfort:** each creature likes or dislikes some materials (species and personality); a disliked item lowers its mood a little
  each day.

## 7. World boss eggs (stakeholder)
- A **world boss** (apex creature, GDD §7) can **drop an egg** when it is defeated. *(filled conditions)*: the boss belongs to the
  player's class; the player dealt at least 10% of the damage or healed/protected those who did; and the player's rank is at least
  the boss's tier minus two steps. The drop is a chance in the loot table (crafting spec), and never for sale.
- The egg **hatches into a young boss creature** after a day of care at the Sanctum, with the species' stats **reduced** (−40% at
  hatching *(filled)*), a tier no higher than the player's rank, and the normal level and life-stage path. The hatchling's first
  core memory is its summoner.
- Boss eggs are soulbound until hatched (they cannot be traded), so they cannot become a market item.

## 8. Commands v1 (M2-08)
| Command | What it does | Danger (for obedience) |
|---|---|---|
| **Attack** (target) | fight that target | from the target's strength vs. its own |
| **Guard me** | stay close, attack whoever attacks the owner | low |
| **Hold** | stay at this place, defend it | low |
| **Retreat** | leave the fight, come back to the owner | none |
| **Use skill** (slot) | use a skill now | from the skill and target |

- The obedience answer (Obey, Improvise, Hesitate, Refuse) is shown with an emote and, in the companion panel, a one-line reason.

## 9. Saved state and contract
- New saved fields per creature (Bonding contract v1.1.0): class (from species), tier, role XP ×3, level, life stage, skill
  points, chosen nodes, active slots, equipment item ids. Migration from v1.0.0: existing pacted creatures start at level 1,
  young, their species' tier, no skills (R26 migration test).
- All changes (level up, stage up, tier raise, skill choice, equip, hatching) are server transactions, idempotent (R27, R34).

## 10. What the player sees
- **Companion panel:** portrait, level and XP bar, tier, life stage, bond level (as a name), skill tree with warm/cold affinity
  colours, equipment slots, and a "story so far" line from its strongest memories.

## 11. Implementation notes (M2-09a)
- **Data:** species carry `class`, `tier` and wild `levels` (min–max); the growth numbers of §3–4 are the `growth` block of
  `data/combat/combat.json`. A wild creature gets a level in its species' range from its stable id (no random draw) and is grown;
  the Memory Grove calf is young.
- **Stats:** species stats are a grown creature's at level 1; each level adds 6% health and 4% power; a young creature has the
  grown bonus (+20% health) taken off, an elder gets +20% power. A change keeps the creature's health share.
- **Experience** (summoned companions only): combat from damage dealt and taken (1 per 2) and +20 when its foe is defeated;
  travel 1 per 50 m walked at the summoner's side (summon arrivals and catch-ups do not count); work waits for Sanctum tasks.
- **Growing up:** when §4's level and bond conditions are met, the companion grows while its summoner rests (stands still 5 s,
  no fight): Affection, a `GrewUp` core memory (event type 22), and a visible change: young creatures are 62% size, elders
  115% with a pale mane (`--showcase=stages`).
- **Shown:** snapshots and the companion list carry level and stage (protocol 11); the companion list reads "hollow owl Lv 3
  (young): with you"; the AI Inspector shows level, cap, tier, stage and experience per role.
- **Saved:** growth is part of the creature record; creatures saved before it existed migrate as §9 says (pacted: level 1, young).
- **M2-09b:** every player has a class, a Summoner Rank, a level and experience (`PlayerProgress`; Elf and rank F until the
  ranking quest, M2-21). Players earn experience for defeating (+20) or sparing (+30) a creature and for a sealed Pact (+50);
  levels follow the same curve, capped by the rank, and raise health and power like a creature's. A Pact offer now applies §2:
  a creature of another class always refuses (`WrongClass`, remembered as `PactRefused`, "does not answer to your kind of
  summoner"), and the level difference table adds to the score. Progress is saved with the player and shown in the HUD
  ("Elf · Rank F · Level 1" and an experience bar; protocol 12).

## Changelog
- **0.2 (2026-10-21):** approved with the stakeholder's answers: three life stages (young, grown, elder) and levels; creatures
  never die of old age; tier caps the level (same scale as player ranks) and is tied to the summoner's rank; creatures bound to
  classes (other-class Pacts always fail); level difference eases or hardens Pacts; skill trees in the first version; world boss
  eggs. Removed the fixed level caps 15/25/35 (replaced by tier caps).
- **0.1 (2026-10-21):** First draft (M2 D2).
