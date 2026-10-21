# Sumoner World - Combat & Magic v1 (D1)
Version: **0.1** (M2 D1) · Date: 2026-10-21 · Owners: Sr Game Designer (feel, values) + Mechanics Developer (contract, server) ·
Status: **Approved by stakeholder 2026-10-21** (§13 answers recorded)
Rules: R08, R11, R15, R21, R27, R28, R30–R33, R50 · Related: GDD v0.2 §3 Combat, §4 Magic, §9 Death, §10 Companions;
AI Bible v0.1 §4 (memory events), §7–8 (decisions, learning), §10 (Bonding); TDD v0.1 §1 (budgets), §2 (tick stages), §3 (net)

> This spec covers what the M2 slice needs: melee, defence, the first school of magic, creature abilities, defeat and death.
> All numbers are **starting values** and live in data (R08). The M1 placeholder "the third hit kills" (M1-23a) is replaced.

---

## 1. Goals
1. **One fair rulebook for everyone.** Players, wild creatures and companions use the same stats, moves, hit rules and damage
   formula: the **Combat contract v1.0.0** (R28). A creature's attack and a player's attack are the same kind of data.
2. **Readable before fast.** Every attack is telegraphed (wind-up), and a hit shows clearly who hit whom (GDD Pillar 3).
   Children in the family can follow it.
3. **No dice in v1.** No critical hits and no random damage. Outcomes come from timing, positioning, stats and the creature's
   mind (R15). This keeps fights readable and the simulation deterministic (R11).
4. **Fights create memories.** Every fight feeds the creature mind (AI Bible §4): who attacked, who spared, who fought beside
   whom, who healed. Combat is how most memories are made.
5. **One continuous combat mode** (M0-08, binding): no separate battle screen or state starts when a fight or a summon begins.

## 2. Stats (data)
Every combatant (player or creature) has a `Combatant` component on the server:

| Stat | Meaning | Player source | Creature source |
|---|---|---|---|
| `Health` | 0 = defeated (§6) | Vitality | species base × level |
| `Stamina` | spent by attacks, blocks, dodges; regenerates when not spending | Stamina attribute | species |
| `Mana` | spent by spells | Focus | species (magic species only) |
| `Power` | multiplies outgoing damage, %: `100` = ×1.0 | gear + attributes | species × level, growth stage |
| `Guard` | flat damage reduction per hit | armour | species (hide, shell) |
| `Resist[type]` | per damage type, per mille (0–750) | gear, charms | species |

- **Damage types v1:** `Physical`, `Primal`. (`Storm` and `Ember` come with those schools.)
- All values are **integers** (fixed-point where needed), so the server's combat stage is deterministic and allocation-free
  like the rest of Core (R11, R21).
- Species stats live in `data/ai/species/*.json` (a new `combat` block); player base values in `data/combat/player.json`; the
  level and growth-stage scaling belongs to D2 (companion progression).

**Starting values (slice):**
| | Health | Stamina | Mana | Power | Guard | Notes |
|---|---|---|---|---|---|---|
| Player (start) | 100 | 100 | 60 | 100 | 0 | |
| Mossback (grazer) | 160 | 80 | 0 | 90 | 4 | slow, heavy charge |
| Bogfox (small predator) | 60 | 120 | 20 | 110 | 0 | fast bites, one Primal trick |
| Hollow owl | 40 | 100 | 40 | 80 | 0 | placeholder until the avian model |

## 3. Moves (data)
A **move** is anything that deals or blocks damage: a sword swing, a bite, a charge, a spell. Moves are data
(`data/combat/moves.json`) shared by players and creatures.

| Field | Meaning |
|---|---|
| `windupTicks` | telegraph before the hit can land (readable; 30 ticks = 1 s) |
| `activeTicks` | when the hit can land |
| `recoveryTicks` | after the hit, the attacker cannot act |
| `shape` | `arc` (reach, angle), `line` (reach, width), `circle` (radius, around the attacker), `projectile` (speed, radius, max range) |
| `damage`, `type` | base damage and damage type |
| `stamina`, `mana` | cost when started |
| `poise` | how hard the hit is to shrug off (§5 stagger) |
| `effects` | status effects applied on hit (§7) |
| `tags` | for combos and AI (`heavy`, `charge`, `bite`, `spell`, `primal`) |

**Slice moves:** player `light` (fast arc, 8 dmg) and `heavy` (slow arc, 20 dmg, poise 40); mossback `charge` (line 6 m,
22 dmg, heavy); bogfox `bite` (short arc, 7 dmg) and `pounce` (line 4 m, 12 dmg); spells in §8.

## 4. Hits on the server
- **Clients send intents, never results** (R31). New intents: `Attack(move, aim)`, `Block(on/off)`, `Dodge(direction)`,
  `Cast(spell, aim or target)`. The server validates: the move is known to the actor, enough stamina or mana, not staggered or in
  recovery, cooldown done (R30).
- **Stage 7 "Combat & magic"** (TDD §2.2) advances every active move by one tick. During `activeTicks` it queries the spatial
  index with the move's shape (R19) and tests each target's body circle (`bodyRadiusCm`, from M1's personal-space work). A
  target is hit **at most once per move**.
- **Latency:** the player's own animation starts at once on the client (prediction). The server checks hits against target
  positions **rewound by the attacker's measured delay, at most 150 ms**, so a hit that looked right on screen counts. On
  the family's local server the delay is a few milliseconds.
- **Friendly fire:** off between a player and their own companions and between players on PvE servers (GDD §8). Area spells skip
  them too.

## 5. Damage, defence and stagger
**Damage per hit:**
`damage = max(1, (move.damage × Power / 100) × (1000 − Resist[type]) / 1000 − Guard)`

- **Block:** while blocking and facing the attacker (front 120°), damage × 0.3 and the blocker pays stamina = damage. With 0
  stamina the guard breaks (stagger).
- **Parry:** a block started **within 6 ticks (0.2 s) before the hit lands** takes no damage and staggers the attacker.
- **Dodge:** 9 ticks (0.3 s) of no hits, 25 stamina, a short dash. It cannot be repeated for 15 ticks.
- **Stagger (poise):** every combatant has `poise` (data). A hit whose move poise is above the target's current poise breaks
  its action and staggers it for 12 ticks. Heavy creatures have high poise and shrug off light hits. Readable, with no randomness.
- **Stamina regeneration:** 20 per second, starting 0.5 s after the last spend.

## 6. Defeat, yield and death
GDD §1 says a player can earn trust by "defeating it without killing it". So creatures do not simply die at 0:

- **Yield.** When a creature's health drops **below 20%**, its mind decides whether to fight on or yield (utility AI; timid and
  low-Loyalty creatures yield sooner, bold ones fight to the end). A yielding creature stops attacking, lowers its body and
  backs off. Hitting a yielding creature is possible, but it is remembered harshly (§9).
- **Spared.** If the player stops attacking a yielded creature for 5 s, it records `DefeatedButSpared` (Respect +,
  AI Bible §4.2) and goes free. This is the "show strength" path to a Pact.
- **Defeated at 0 health:**
  - **Wild creature:** dies. Kin nearby remember `KilledKin`, as in M1.
  - **Companion:** falls (`Fallen`, AI Bible §10). On **Normal** servers it can be revived at a cost; on **Hardcore** it dies
    permanently and the others grieve (GDD §9).
  - **Player:** dies and respawns at the Sanctum or a shrine, with a grave holding the carried items (GDD §9).
- **Health regeneration** out of combat (10 s without damage): 2% of max health per second for creatures and players.

## 7. Status effects v1 (stage 8)
| Effect | What it does | Source in the slice |
|---|---|---|
| `Stagger` | interrupts the action, 12 ticks | poise break, parry |
| `Rooted` | cannot move (can still attack and cast), 2 s | Primal *Entangle* |
| `Slowed` | 50% move speed, 3 s | bogfox *pounce*, thorn hits |
| `Mending` | heals over time, 4 s | Primal *Mend* |

Effects are data (`data/combat/effects.json`): duration in ticks, stack rule (refresh, never stack in v1), and tags for combos.

## 8. Magic v1: Primal
GDD §4: spells are built from **runes**. v1 has two rune kinds:
- **Form:** `Bolt` (projectile), `Burst` (circle around a point within 15 m), `Ward` (on self or an ally).
- **Essence (Primal):** `Thorns` (Primal damage + Slowed), `Roots` (Rooted), `Growth` (Mending).

A spell is a form plus an essence. The player starts with three, and more combinations open with progression (D2):

| Spell | Runes | Cost | Cast | Effect |
|---|---|---|---|---|
| **Thornbolt** | Bolt + Thorns | 10 mana | 0.4 s | 12 Primal damage, Slowed 3 s |
| **Entangle** | Burst + Roots | 20 mana | 0.8 s | radius 3 m, Rooted 2 s, no damage |
| **Mend** | Ward + Growth | 15 mana | 1.0 s | +30 health over 4 s on self or a companion |

- **Mana regeneration:** 3 per second. Casting can be interrupted by stagger (the mana is spent).
- **Resonance** (GDD §4): a bonded Primal creature nearby lowers Primal spell costs by 10%. It is the first small payoff for
  your party shaping your build; more comes with D2.
- Creatures cast with the same system: the bogfox's `foxfire` is `Bolt + Thorns` with its own stats.

## 9. Fights and the creature mind
| What happens | Memory event (existing, AI Bible §4.2) | For whom |
|---|---|---|
| Any damage from a player | `AttackedBy` | the target |
| Spared after yielding | `DefeatedButSpared` | the target |
| Hit while yielding | `Mistreated` (core) | the target, and kin who see it |
| Kill | `KilledKin` (+ rumours) | kin within 30 m |
| Fighting on the same side in a fight | `FoughtBeside` | companions, and wild creatures the player helped |
| Healed by a player | `HealedBy` | the target |
| Commanded into a fight it was losing | `CommandedIntoDanger` | the companion (existing obedience rule) |

- **Health becomes real input** for the utility AI (it is already an input, AI Bible §7). Flee, Yield and Fight now react to it.
- **Target choice:** creatures keep a small **threat list** (up to 4 entries: damage taken from each attacker plus their
  opinion of it). The Fight tree attacks the top entry. Companions add "who is attacking my owner".
- **Tactic learning** (AI Bible §8) uses the fight's damage dealt vs. taken, as already specified.

## 10. Combos (GDD §3)
A combo is a **creature move hitting a target that carries a status effect from the player** (or the reverse), within a short
window. Combos are data (`data/combat/combos.json`).

- **Slice combo: "Uproot".** A target that is `Rooted` (Entangle) and hit by a `charge` move takes ×2 damage and is knocked
  down (stagger 30 ticks). Mossbacks are the natural partner.
- A companion learns a combo after it succeeds **5 times** (AI Bible §8, data value). Then it starts it on its own when the
  player casts Entangle. The first time it does so is a "it learned my style" moment and is shown with an emote.

## 11. What the client shows
- **Snapshots** carry, per entity: health as a fraction (one byte), the current move and its phase (for animation), and active
  status effects (bit flags). Exact numbers stay on the server.
- **Feedback:** a hit flash and a short sound on hit; a clear "block" and "parry" sound; the telegraph is part of each move's
  animation. No floating damage numbers by default (readability through bodies first, DEC-002); an option can show them.
- **Health bars:** over creatures only while in a fight with them, and always for your companions in the HUD (M2-17).
- **Children:** no blood or gore anywhere. A defeated creature lies down and fades; the death is shown through the others'
  grief, not through injury.

## 12. Budgets and tests
- **Budget:** the combat & magic stage and status effects together ≤ **2.0 ms per tick** on the server (TDD §1). The B5 gate
  gets a combat scenario (two herds and a pack fighting) with 0 B allocated per tick.
- **Contract tests:** the damage formula, block/parry windows, stagger rule and yield thresholds as table-driven unit tests;
  the same move used by a player and a creature gives the same result (R28).
- **Server-authority tests:** forged `Attack` intents (unknown move, no stamina, in recovery, out of range) are rejected;
  duplicate intents deal damage once (R27, R30, R34).
- **Determinism:** two seeded runs of the combat scenario give identical state hashes (R11).

## 13. Stakeholder answers (2026-10-21)
1. **Yield and spare (§6): yes**, this is how defeating without killing works.
2. **Server preset: Normal is the default** (fallen companions can be revived). Hardcore stays an option a server can choose.
   GDD §9 updated to match.
3. **No randomness in v1:** not answered directly; the stakeholder asked the team to fill open gaps, so v1 keeps **no random
   damage or critical hits** (§1). It can be revisited after the family sessions.

## Changelog
- **0.1 (2026-10-21):** First draft (M2 D1), for stakeholder approval.
- **0.1 approved (2026-10-21):** yield and spare confirmed; Normal is the default server preset; no randomness in v1 kept.
