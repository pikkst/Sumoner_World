# Sumoner World - Companion Progression v1 (D2)
Version: **0.1** (M2 D2) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Draft, waiting for
stakeholder approval**
Rules: R08, R15, R26, R27, R28, R29, R50 · Related: GDD v0.3 §1, §2, §10, §11; AI Bible v0.1 §8 (learning), §10 (Bonding
contract v1.0.0); combat spec v0.2 (D1)

> Extends the Bonding contract to **v1.1.0** (AI Bible §10): experience and levels, growth stages, skill trees, equipment
> slots and commands v1. Fusion and species rarity are **not** in v1.1 (GDD §10 open questions, M3). All numbers are starting
> values in data (R08).

---

## 1. Principles
1. **Earned, never bought** (DEC-002, Pillar 3). Experience, stages, skills and gear come from play.
2. **Shaped by the life it lived.** Two creatures of the same species end up different, because what they did, how they were
   treated and their personality steer their growth (GDD §10).
3. **Stats come from level and stage; abilities come from bond and the skill tree** (AI Bible §10.5). A strong creature that does
   not trust you is still hard to command (obedience, AI Bible §10.7).
4. **No hidden dice.** Growth is deterministic from what happened (R15).

## 2. Experience and levels
- A pacted creature earns **experience per role**:

| Role | Earned by (starting values) |
|---|---|
| **Combat** | damage dealt + damage taken in fights at the owner's side (1 XP per 2 points), +20 per defeated wild creature |
| **Work** | Sanctum tasks and gathering (M2-13): +5 per task finished |
| **Travel** | +1 per 50 m walked while summoned; +10 for each new place found when scouting |

- **Level** = from the **total** experience, on a curve in data (`data/companions/levels.json`): level `n` needs
  `40 × (n − 1)^1.5` total XP (rounded), so level 2 at 40 XP, level 5 at 320, level 10 at 1,080.
- **Species level cap** (data): slice species have a cap of 15. Higher caps come with growth stages (§3).
- **Each level:** +6% health, +4% power over the species' base (data per species, the "growth curve"). Stamina, guard and
  resistances come from the stage and gear.
- **Wild creatures** keep level 1 in v1 (their strength is in the species data). Region levels are an M3 question.

## 3. Growth stages (tiers inside a creature's life)
| Stage | Reached at | Changes |
|---|---|---|
| **1 Young** | Pact (or birth) | base look, level cap 15 |
| **2 Grown** | level 10 **and** bond level ≥ 2 (Companion) | bigger body and markings (style guide), +20% health, +1 skill slot, level cap 25 |
| **3 Elder** | level 20 **and** bond level ≥ 4 (Devoted) | signature look, +20% power, the species' signature skill, level cap 35 |

- **Growing up is a moment.** When the conditions are met, the creature grows during the next rest at the Sanctum or by a fire,
  and remembers it as a core memory with the owner (`GrewUp`, new memory event type). The owner is shown it happening.
- A creature whose bond falls below the requirement keeps its stage, but cannot reach the next one until the bond recovers.
- Species rarity classes (GDD §10) are a separate axis, decided in M3.

## 4. Skill trees
- **One tree per species** in data (`data/companions/skills/<species>.json`), about **9 nodes in 3 branches** for slice
  species. Each node has: id, branch, required level, required bond level, required stage, prerequisites, and what it gives (a
  move from the combat data, a passive, or a behaviour).
- **Skill points:** 1 per 2 levels. **Slots** (active skills at once): 2, +1 at stage 2, +1 at stage 3.
- **Who chooses?** The player suggests; the creature agrees or not. Each branch has a **personality and history affinity**:

| Affinity source | Example |
|---|---|
| Traits | a bold creature likes the *Vanguard* branch; a curious one *Scout* |
| Roles it did most (§2) | combat-heavy life → *Vanguard*; lots of scouting → *Scout* |
| Learned combos and tactics (AI Bible §8) | a creature that learned "Uproot" leans *Earthshaker* |
| Memories | `SavedLifeOf` the owner → *Guardian* nodes cost less |

  - A node in a branch with **positive affinity** costs 1 point; neutral 1 point; **negative affinity costs 2 points** and the
    creature shows reluctance (emote). The player can still choose it; the creature never refuses outright in v1. This keeps
    the player in charge while the creature's character visibly matters.
  - When the player has not spent points for 3 in-game days, the creature **picks for itself** from its highest-affinity
    branch, and the player is told. Lazy owners get creatures that grow their own way.
- **Respec:** at the Academy, for an in-game cost (gold or a quest token, D3), never for real money. The creature remembers
  being "reshaped" (mildly negative, `Mistreated` is not used).
- **Slice trees (examples):**
  - **Mossback:** *Vanguard* (stronger charge, knockdown), *Guardian* (taunt, shield the owner), *Earthshaker* (Uproot combo
    master, stomp).
  - **Bogfox:** *Scout* (find hidden items, mark enemies), *Trickster* (foxfire, decoy), *Pack* (bite bleeds, flank bonus).

## 5. Equipment
- Slots by body type (data): **quadruped**: head, body, charm; **avian**: head, charm; **biped**: like the player
  (weapon, head, body, charm); **serpentine**: body, charm.
- Gear for creatures is crafted (D3). It adds stats and sometimes a passive; it never adds an ability that the skill tree
  would give.
- **Comfort.** Each creature has a **liked/disliked material** (from its species and personality). Wearing a disliked item
  lowers its mood (small Affection drop per day). Readable: it tugs at it.

## 6. Commands v1 (M2-08)
| Command | What it does | Danger (for obedience) |
|---|---|---|
| **Attack** (target) | fight that target | from the target's strength vs. its own |
| **Guard me** | stay close, attack whoever attacks the owner | low |
| **Hold** | stay at this place, defend it | low |
| **Retreat** | leave the fight, come back to the owner | none |
| **Use skill** (slot) | use a skill now | from the skill and target |

- The obedience answer (AI Bible §10.7: Obey, Improvise, Hesitate, Refuse) is shown with an emote and, in the companion panel,
  a one-line reason ("It is afraid of the bogfox pack").
- Commands use the existing intent path (server validated, R30) and write `CommandedIntoDanger` as the AI Bible says.

## 7. Saved state and contract
- New saved fields per pacted creature (Bonding contract v1.1.0): role XP ×3, level, stage, skill points, chosen nodes
  (bitmask per tree), active slots, equipment item ids. Schema migration from v1.0.0: every existing pacted creature starts
  at level 1, stage 1, no skills (R26 migration test).
- All changes (level up, stage up, skill choice, equip) are server transactions, idempotent (R27, R34).

## 8. What the player sees
- **Companion panel** (M2-10, M2-17): portrait, level and XP bar, stage, bond level (as a name, not a number), skill tree
  with the creature's affinities as warm/cold colours, equipment slots, and a "story so far" line from its strongest memories.
- In the world: level-ups show a small glow; growing up is a short scene.

## 9. Questions for the stakeholder
1. **Who chooses skills (§4):** the player chooses, the creature's personality makes some choices cheaper or dearer and it
   picks by itself if left alone. Is that the balance you want, or should creatures be able to refuse a skill outright?
2. **Three growth stages** (Young, Grown, Elder) for M2. Enough, or do you want more stages?

## Changelog
- **0.1 (2026-10-21):** First draft (M2 D2).
