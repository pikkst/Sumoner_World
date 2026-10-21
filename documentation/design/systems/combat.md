# Sumoner World - Combat & Magic v1 (D1)
Version: **0.4** (M2 D1, M2-01, M2-02a, M2-02c) · Date: 2026-10-21 · Owners: Sr Game Designer (feel, values) + Mechanics Developer (contract, server) ·
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
| Mossback (grazer) | 160 | 80 | 0 | 90 | 2 | slow, heavy charge (guard 4 → 2 in M2-01: a flat 4 left light hits at 4 damage) |
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

## 11a. Lock-on (stakeholder, 2026-10-21)
- **Middle mouse button or T** locks the camera on the nearest creature in front of it within 25 m; pressing again releases it,
  as do the target leaving view, going farther than 25 m or being defeated. A red marker floats over the target.
- While locked, the camera swings behind the character to keep the target in view (dragging does not turn it), WASD circles
  the target, and **attacks aim at the target**, so players can think about timing and combos instead of aiming. This helps
  the family's younger players most.
- **Hardcore servers have no lock-on:** aiming is part of the challenge. The server tells the client in its welcome
  (`ServerRules.LockOn`, protocol 8); `--hardcore` / `SW_HARDCORE` starts a Hardcore server. Lock-on is aim assistance on the
  client: the server still checks every attack, and an attack aimed by hand works the same on both presets.

## 12. Budgets and tests
- **Budget:** the combat & magic stage and status effects together ≤ **2.0 ms per tick** on the server (TDD §1). The B5 gate
  gets a combat scenario (two herds and a pack fighting) with 0 B allocated per tick.
- **Contract tests:** the damage formula, block/parry windows, stagger rule and yield thresholds as table-driven unit tests;
  the same move used by a player and a creature gives the same result (R28).
- **Server-authority tests:** forged `Attack` intents (unknown move, no stamina, in recovery, out of range) are rejected;
  duplicate intents deal damage once (R27, R30, R34).
- **Determinism:** two seeded runs of the combat scenario give identical state hashes (R11).

## 12a. Implementation (M2-01, Combat contract v1.0.0)
- **Data:** `data/combat/moves.json` (kind `moves`), `data/combat/combat.json` (kind `combat`: rules and the player's stats),
  and a `combat` block in every species file. The loader checks ranges and that every listed move exists.
- **Core:** `SumonerWorld.Core.Combat`: `CombatRules` (the formula, `CanStart`, `Start`), components `Combatant`,
  `ActiveMove`, `Yielding` and `Downed` (all in the state hash), events `CombatHit` and `CombatantDefeated`.
  `CombatSystem` (stage 7) resolves arc, line and circle hits once per move; `VitalsSystem` (stage 8) regenerates without
  rounding drift, settles spares and defeats.
- **Player attacks** use the basic move (the first in the player's list) through the existing `Hit` intent (F key), aimed at
  the target. A hit can miss if the creature moves out of the shape during the wind-up.
- **Placeholders until later tasks:** whether a creature yields is decided by `yieldMaxBoldness` until the utility AI decides it
  (M2-03); creatures do not attack yet (M2-03); a defeated player gets up where they fell after `playerDownTicks` (graves and
  shrines later); health is not saved (restored creatures start at full health, M2-15); block, parry, dodge and stagger come
  with M2-02.
- **Tests:** formula table, shapes, regeneration sums, move timing, yield and spare, mistreatment, herd friendly fire, a downed
  player getting up, and a six-creature fight that is deterministic and allocates 0 bytes per tick.
- **M2-02a (protocol 7):** `Blocking`, `Dodging`, `Staggered` and the rules for them in `combat.json` (block share 300‰ over a
  120° arc, parry window 6 ticks, dodge 9 ticks of evasion / 25 stamina / 15-tick cooldown / 250% speed, stagger 12 ticks).
  Block is a held input button and dodge a pressed one (`PlayerButtons`); the server dashes the avatar during a dodge and keeps
  a downed or staggered player still. The `Attack` intent (slot 0 basic, 1 heavy, aimed by heading) hits whatever is in the
  shape. Snapshots carry each entity's health fraction, `CombatAction` and move, and the receiver's own health, stamina and
  mana. Stamina does not regenerate while blocking.
- **M2-03 (creatures fight back):** `CreatureAttackSystem` carries out the behaviour trees' `attack` intents (the Fight tree): the
  creature turns to its focus and starts the first of its species' moves whose shape reaches it (a bogfox bites up close and
  pounces from 4 m), through `CombatRules` like players; not while snared, and never at a downed player. The utility AI and trees
  now get the creature's real health (0–1000), so Flee rises as a creature gets hurt (the Flee and Hunt curves already used it).
  Still placeholders: yielding by a boldness threshold (not yet a goal), Hunt stays abstract (no creature-on-creature kills yet,
  so the Memory Grove is not emptied by predators), and herds defending their young waits for life stages (M2-09).
- **M2-02b (client):** left click attacks where the camera looks (held ≥ 0.4 s: heavy), Shift held blocks, Space dodges
  (`CombatControls`); the client predicts the dodge dash and holds still while down or staggered (`ClientPrediction`,
  reconciled as usual). `CombatHud`: health, stamina and mana bars and the keys; creatures and players show a health bar while
  hurt, swell briefly when hit and tilt with their action (`CombatPose`, placeholder until animations). The dev tools pick a
  creature with the left button only while the overlay or inspector is open.
- **M2-04 (player character):** players are shown as a placeholder summoner with a staff (style guide §1–2) animated from ground speed and the snapshot's combat action: the wind-up raises the staff behind the head (the telegraph of §11), the strike swings it forward and down, block raises both forearms, dodge is a low dive, stagger throws the arms out, yield kneels, and a hit snaps the head back. `CombatPose`'s whole-body tilts stay on top. Check with `--showcase=player --showcase-distance=9`.

## 13. Stakeholder answers (2026-10-21)
1. **Yield and spare (§6): yes**, this is how defeating without killing works.
2. **Server preset: Normal is the default** (fallen companions can be revived). Hardcore stays an option a server can choose.
   GDD §9 updated to match.
3. **No randomness in v1:** not answered directly; the stakeholder asked the team to fill open gaps, so v1 keeps **no random
   damage or critical hits** (§1). It can be revisited after the family sessions.

- **M2-11 (runes and Primal v1):** runes (`data/magic/runes.json`: forms bolt, burst, ward; essences thorns, roots, growth),
  spells (`data/magic/spells.json`: thornbolt, entangle, mend with §8's costs and cast times) and effects
  (`data/combat/effects.json`: rooted 2 s, slowed 3 s at 50%, mending 4 s). The loader composes each spell into a move of the
  same id (cast time = wind-up, form = shape: line, burst around a point ahead, ward on self or an own creature; essence =
  damage, effect, healing), so new spells need no code. Players know every spell; `Cast` intent (protocol 14) with aim, burst
  distance and ward target; the server checks mana and timing. A spell's wind-up shows as `Cast`. Resonance: a summoned
  companion of a class sharing the school within 20 m makes it 10% cheaper. A stagger now interrupts any move (§7), so a cast
  is lost with its mana. Players' own companions are never hit by their moves or spells. Rooted creatures stand still, slowed
  ones go at half speed; mending heals over 4 s. Snapshots carry status marks (roots, slowed ring, mending glow on the body);
  keys C / V / G. Creatures' spells (`foxfire`) come later.

- **M2-12 (first combo):** `data/combat/combos.json` holds Uproot (rooted target, `charge` move, 200% damage, 30-tick knock-down,
  learned after 5, trigger spell entangle); a combo's position is its saved bit (at most 4). A hit whose move carries the tag on a
  target with the effect is a combo (`ComboLanded`). A pacted creature that lands it 5 times knows it. When its summoner's
  entangle catches a creature, every summoned companion that knows Uproot goes for it by itself, the combo move first; the
  first time, it shows Affection for 2 s, and the player reads "Your mossback learned your style: Uproot!" when it learns it.
  A companion that does not know it does not react to its summoner's rooting (only to real hits). Companions now attack as soon
  as any move reaches (a charge from 6 m), unless they flank. Combos are saved with the creature; the companion list carries
  them (protocol 15).

## Changelog
- **0.1 (2026-10-21):** First draft (M2 D1), for stakeholder approval.
- **0.4 (2026-10-21, M2-02c):** lock-on (§11a) on Normal servers, none on Hardcore; protocol 8.
- **0.3 (2026-10-21, M2-02a):** defence implemented (§12a); protocol 7.
- **0.2 (2026-10-21, M2-01):** implementation notes (§12a); mossback guard 4 → 2 (a flat 4 left light hits at 4 damage).
- **0.1 approved (2026-10-21):** yield and spare confirmed; Normal is the default server preset; no randomness in v1 kept.
