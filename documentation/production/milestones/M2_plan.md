# M2: Networked Vertical Slice (Plan)
Window: **2026-10-21 → 2027-08-31** (M1 closed early; target date kept as buffer) · Owner: Producer Agent ·
Status: **In progress** (stakeholder 2026-10-21: close M1 now and start M2) · Related: GDD v0.2 (§1 Pacts, §3 Combat, §4 Magic, §6 Crafting,
§10 Summoned Companions), AI Bible v0.1 §10 (Bonding contract), DEC-002, DEC-003, TDD v0.1
Velocity: solo-adjusted, about 1 task per 3–5 working days, as in M1.

All tasks follow the Definition of Done (`development_rules_enforcement.md` §3). One PR per task, CI green, merged under the
stakeholder's standing approval. New or changed design documents (D1–D3) still need the stakeholder's approval (R56).

## M2 goal
**A 30-minute multiplayer slice the family can play together** on the local Docker dev server (DEC-003). In those 30
minutes a player:
1. meets wild creatures that remember them (M1),
2. earns trust and makes a **Pact**,
3. **summons** the creature, and it follows, defends and helps on its own,
4. **fights together** with it: the player with melee and a first school of magic, the creature with its own abilities and
   the player's **commands** (it obeys, improvises, hesitates or refuses, and the player can see why),
5. sees the companion **grow**: experience, level, a first growth stage and first skill-tree choices,
6. **gathers and crafts** food, Pact offerings and a first weapon, and equips the player and the companion,
7. quits, comes back, and everything is still there: bonds, memories, items, progress (versioned saves).

### Success criteria (project-config M2, adapted by DEC-003)
| Criterion | How it is shown |
|---|---|
| 30-minute playable slice in multiplayer | 2–4 family members play the slice together on the dev server; a scripted bot run covers the same path in CI |
| Zero critical bugs | Bug list in `qa/bug-reports/` has no open critical item at the end (crash, lost save, lost item, duplication) |
| Save migration tests pass | Every saved schema has a migration test from each earlier version (R26) |
| ~~Steam page live~~ | **Deferred by DEC-003** (the game is private until the family decides) |

---

## Start: close M1
| Task | Delivers | Est |
|---|---|---|
| **M1-25** M1 gate report | `documentation/production/reports/M1_gate_report.md`: evidence for server authority, reconnect, determinism and the B2/B3/B5 dev-PC results; the USP readability and recommended-spec performance rows read "not measured (DEC-003)"; GO / RE-SCOPE recommendation | 1d |

**Proposal:** M1's planned work is done or deferred five months early. As with M0, the stakeholder can take the gate
decision now and M2 can start straight away. The timeline dates then move forward, and the buffer is kept.

## Phase 0: Design first (specs before code, R50)
| Task | Delivers | Est |
|---|---|---|
| **D1** Combat & Magic v1 spec | `systems/combat.md`: health, stamina, damage formula, hit validation on the server, melee/dodge/block, the Primal school's first spells, creature abilities, the combat contract shared by players and creatures (R28); replaces the M1 placeholder "3 hits kill" | 3d |
| **D2** Companion progression spec | AI Bible §10 → Bonding contract v1.1: role experience and levels, growth stages (working target 3), skill tree data format and how personality and history choose a branch, equipment slots per body type, commands v1 | 3d |
| **D3** Items & crafting spec | `systems/crafting.md`: item model, inventory, gathering, recipes, equipment; transaction rules (atomic, idempotent, R27, R34) | 2d |

## Phase A: Player and combat (after D1)
| Task | Delivers | Est |
|---|---|---|
| **M2-01** Combat core | Health, stamina, damage and death as server systems from data; combat events into creature memory (AttackedBy, KilledKin now from real fights) | 4d |
| **M2-02** Player melee, dodge, block | Server-validated attack and defence intents with client-side animation prediction; readable hit feedback | 4d |
| **M2-03** Creature combat behaviour | Real Attack intents with damage, threat and fleeing through the existing utility AI and behaviour trees; predators hunt, herds defend their young | 4d |
| **M2-04** Player character | A placeholder humanoid with idle/walk/run/attack/dodge/hit animations replaces the capsule (style guide) | 4d |

## Phase B: Summoning and companions (after D2) - GDD §1, §10
| Task | Delivers | Est |
|---|---|---|
| **M2-05** Pact in play | Pact with terms chosen in a simple menu; Pacted state saved; the creature's answer shown by behaviour first, text second | 3d |
| **M2-06** Summoning | Sigil cast, mana cost, Bond Capacity, summon and dismiss, summoned creatures at AI LOD T0 | 3d |
| **M2-07** Companion behaviour | Follow, keep personal space, defend the owner unprompted, help in fights, wander off when bored if independent | 4d |
| **M2-08** Commands v1 | Attack target, guard me, hold, retreat; the `ObedienceModel` answer (obey, improvise, hesitate, refuse) is shown with an emote and a short reason in the AI Inspector | 4d |
| **M2-09** Growth v1 | Role experience (combat, work, travel), levels to a species cap, stats from data; first growth stage with a visible change (size, markings) | 4d |
| **M2-10** Skill tree v1 | One tree per slice species (2–3 abilities each), nodes unlocked by level and bond level; branch weighting from personality and the roles it has done most; shown in a simple companion panel | 5d |

## Phase C: Magic (after D1)
| Task | Delivers | Est |
|---|---|---|
| **M2-11** Runes and Primal v1 | Mana, 3 Primal spells built from runes (data), server-validated casting | 4d |
| **M2-12** First combo | One player-spell + creature-ability combo that the creature learns by repetition (GDD §3) | 3d |

## Phase D: Items and crafting (after D3)
| Task | Delivers | Est |
|---|---|---|
| **M2-13** Inventory and gathering | Item model, inventory, gathering from plants and ore, atomic idempotent transactions with duplicate-intent tests | 4d |
| **M2-14** Crafting and equipment v1 | Recipes from data (food, Pact offering, a weapon, a creature charm); equipment slots for the player and companions (GDD §10) | 4d |

## Phase E: Slice
| Task | Delivers | Est |
|---|---|---|
| **M2-15** Versioned saves | Player, inventory, bonds, progression and skill choices in the world store; schema versions with migration tests (R26) | 4d |
| **M2-16** Slice region and content | A 30-minute path in the test region: Memory Grove, a gathering area, a predator den as the first "dungeon-like" encounter, 3–4 species | 5d |
| **M2-17** HUD and family UX | Health, stamina, mana, companion status, key hints, settings (volume, mouse sensitivity); readable for children | 3d |
| **M2-18** Slice test and polish | Bot run of the slice path in CI; family sessions (consent and questionnaire, DEC-003); bug triage to zero critical; family package | 4d + sessions |

Total about 80 working days.

---

## Not in M2 (and why)
| Item | When | Why |
|---|---|---|
| Fusion | M3, after a legal check | GDD §10 open question 1 (IP/patent review), and the fusion rules and memory merge limits need design |
| Species rarity classes | M3 | GDD §10 open question 4; needs more species than the slice has |
| Quests and full dungeons | M3 (with EA content) | No design yet (GDD §10 open question 6); M2 has one den encounter |
| Trading and economy | M3 | project-config M3 |
| Steam page | when the family decides to open the game | DEC-003 |

## Dependencies
M1-25 → D1, D2, D3 (in parallel) → A (after D1), B (after D2; M2-05 to M2-08 also need M2-01), C (after M2-01), D (after D3) →
M2-15 (after B and D) → M2-16 → M2-17 → M2-18.

## Risks watched in M2
| Risk | Signal | Response |
|---|---|---|
| Scope for one developer | Phase A+B over 1.5× estimate | Cut M2-12 (combo) and M2-10 to one species first; keep the loop whole rather than every feature |
| Combat feel over the network | Testers say hits feel late | Client-side animation prediction (M2-02); local dev server keeps latency low for the family |
| Animations and models | Placeholder art slows readability | Keep the style guide's simple shapes; readability over detail (DEC-002) |
| Content for young children | Parents skip hitting or children are upset by creature death | Parent's choice in the consent form; Normal server preset allows revival (GDD §9) |
| Family feedback ≠ target audience | Decisions tuned only to children | Record it in reports; the formal playtest (QA plan Part B) runs before any public release |

## Status
| Task | Status |
|---|---|
| M2 plan | Adopted 2026-10-21 (M2 started) |
| M1-25 | **Done** 2026-10-21: gate report, GO |

## Changelog
- **0.1 (2026-10-21):** First version, after GDD v0.2 and DEC-003.
