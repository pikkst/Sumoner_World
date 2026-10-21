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
0. **starts as a student at the Summoner Academy**: the ranking quest sets their class (for life) and rank (F–C) from their
   answers plus randomness, and they receive a guaranteed young creature of their class (GDD v0.4 §11),
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
| **D4** Academy & first quest spec | `systems/academy.md`: the Arena Trial's four tests and how they are scored into a rank (F–C), affinity gates and faction, the Bonding Ceremony, a minimal quest model (steps, objectives, rewards, saved on the server), player profile (rank, faction) | 3d |
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
| **M2-09** Growth v1 | Player and creature levels with rank/tier caps (GDD §5); creature role experience, young/grown/elder stages with a visible change; level difference in Pact acceptance and the class rule (other-class Pacts fail) | 5d |
| **M2-10** Skill tree v1 | One tree per slice species (2–3 abilities each), nodes unlocked by level and bond level; branch weighting from personality and the roles it has done most; shown in a simple companion panel | 5d |

## Phase C: Magic (after D1)
| Task | Delivers | Est |
|---|---|---|
| **M2-11** Runes and Primal v1 | Mana, 3 Primal spells built from runes (data), server-validated casting | 4d |
| **M2-12** First combo | One player-spell + creature-ability combo that the creature learns by repetition (GDD §3) | 3d |

## Phase D: Items and crafting (after D3)
| Task | Delivers | Est |
|---|---|---|
| **M2-13** Inventory, gold and loot | Item model, inventory, **gold**, **loot tables and personal loot bags** from defeated creatures, gathering; atomic idempotent transactions with duplicate-intent tests | 5d |
| **M2-14** Crafting and equipment v1 | Recipes from data (food, Pact offering, a weapon, a creature charm); equipment slots for the player and companions (GDD §10) | 4d |

## Phase A2: Academy (after D4) - GDD §11
| Task | Delivers | Est |
|---|---|---|
| **M2-19** Quest system v1 | Quest definitions in data (steps, objectives, rewards), server-side quest state per player, saved; objective events from combat, Pacts and places; quest log in the HUD | 4d |
| **M2-20** Player profile | Class (for life), rank, level, XP and gold saved per account, shown to other players; rank sets level cap and Bond Capacity | 2d |
| **M2-21** Academy and ranking quest | The Academy hub and arena; 8 questions with answer choices scoring class and rank, practical moments (construct fight, reading a creature, Pact rehearsal), seeded randomness, rank F–C | 5d |
| **M2-22** First creature | The guaranteed young creature of the player's class and rank tier (Elf in M2, e.g. a pixie); starter Pact always succeeds; first core memory; summonable | 3d |

## Phase E: Slice
| Task | Delivers | Est |
|---|---|---|
| **M2-15** Versioned saves | Player, inventory, bonds, progression and skill choices in the world store; schema versions with migration tests (R26) | 4d |
| **M2-16** Slice region and content | A 30-minute path in the test region: Academy → Memory Grove → a gathering area → a predator den as the first "dungeon-like" encounter, 3–4 species | 5d |
| **M2-17** HUD and family UX | Health, stamina, mana, companion status, key hints, settings (volume, mouse sensitivity); readable for children | 3d |
| **M2-18** Slice test and polish | Bot run of the slice path in CI; family sessions (consent and questionnaire, DEC-003); bug triage to zero critical; family package | 4d + sessions |

Total about 97 working days (with the Academy, D4 and M2-19 to M2-22).

---

## Not in M2 (and why)
| Item | When | Why |
|---|---|---|
| World boss eggs | M3 (with world bosses) | Designed (companions spec §7); needs world bosses first |
| Fusion | M3, after a legal check | GDD §10 open question 1 (IP/patent review), and the fusion rules and memory merge limits need design |
| Species rarity classes | M3 | GDD §10 open question 4; needs more species than the slice has |
| More quests, rank trials above C, full dungeons | M3 (with EA content) | M2 has the quest system and the Academy questline, plus one den encounter |
| Factions other than the Elves, the Umbral school | M3 | One faction keeps the slice small; the others are shown as coming (GDD §11) |
| Trading and economy | M3 | project-config M3 |
| Steam page | when the family decides to open the game | DEC-003 |

## Dependencies
M1-25 → D1, D2, D3, D4 (in parallel) → A2 (after D4 and M2-01) → A (after D1), B (after D2; M2-05 to M2-08 also need M2-01), C (after M2-01), D (after D3) →
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
| GDD 0.3 | 2026-10-21: Academy, rank and faction added to M2 (D4, M2-19 to M2-22) |
| D1 | **Done** 2026-10-21: `documentation/design/systems/combat.md` approved (yield and spare; Normal preset default; no randomness in v1) |
| D2, D3, D4 | **Done** 2026-10-21: approved with the stakeholder's answers (levels and stages, rank caps, class-bound creatures, loot and gold, rank quest from answers + randomness); `systems/companions.md`, `systems/crafting.md`, `systems/academy.md` v0.2 |
| M2-09b | **Done** 2026-10-21 (merged under standing approval): player class, rank, level and experience (defeat, spare, Pact), capped by rank, raising health and power; the class rule (other-class Pacts always fail) and the level-difference table in Pact acceptance; saved with the player; HUD line and experience bar (protocol 12). M2-09 complete |
| M2-09a | **Done** 2026-10-21 (merged under standing approval): creature growth: species class, tier and wild level range; level curve and tier caps; stats per level and stage; combat and travel experience for summoned companions; young/grown/elder with a visible change (size, elder mane), growing up while the summoner rests (`GrewUp` core memory); level and stage in snapshots, the companion list and the AI Inspector (protocol 11); saved with migration. M2-09b (class rule, level difference in Pacts, player level and rank cap) is next |
| M2-08 | **Done** 2026-10-21 (merged under standing approval): commands v1 for every companion with the player (`Command` intent, protocol 10): 1 attack (lock-on target or the creature in reach), 2 guard me, 3 hold here, 4 come back; each companion answers through the `ObedienceModel` (danger from the foe's strength, fighting its own species goes against its memories) and shows it: Obey Trust, Improvise Curiosity and flanks, Hesitate Fear/Grudge and a 1 s pause, Refuse Grudge; the AI Inspector shows the last command, the answer, the score and the main reason (`ObedienceReason`). Use skill (slot) waits for skills (M2-10) |
| M2-07 | **Done** 2026-10-21 (merged under standing approval): `CompanionSystem` runs summoned companions in place of their trees: follows within 4 m (stops 1.5 m off: personal space), runs to catch up, is drawn back to its summoner beyond 60 m; defends unprompted (whoever hits the summoner or it becomes its foe) and helps (whoever the summoner hits), never its own kind of companion; lets go of a foe that yields or is down, after 10 s, or beyond a 20 m leash; an independent one (Loyalty < 0) roams on its own mind when the summoner stands still 10 s; never attacks its own summoner |
| M2-06 | **Done** 2026-10-21 (merged under standing approval): summoning: `Summon` / `Dismiss` intents (protocol 9); a 1 s sigil cast (the player stands still in a glowing Primal sigil, arms raised) costs 20 mana; Bond Capacity 2 from data until ranks set it (M2-20); the creature arrives beside the player from wherever it is, with a sigil flash; summoned creatures are always AI LOD T0; the server sends each player their companion list, shown at the top left (Z call, X send back); refusals say why (mana, capacity, busy). A dismissed creature goes back to its own life where it stands (the Sanctum comes later) |
| M2-05 | **Done** 2026-10-21 (merged under standing approval): Pact menu (P: the six promises from `pact_terms.json`, up to 3, no chances shown); the creature answers with its body first (`PactAnswerSystem`: stops and faces the player with Curiosity for 0.5 s, then walks up showing Affection or turns its back and walks 6 m off; a snared one answers on the spot), the answer line follows 1.5 s later; the Pacted state was already saved with the creature through the intent journal (M1-17) |
| M2-04 | **Done** 2026-10-21 (merged under standing approval): placeholder summoner (`HumanoidRig`: hips, torso, head, two-part arms and legs, staff) replaces the capsule for the own and other players; procedural idle, walk, run and one pose per combat action plus a hit flinch (`HumanoidAnimator`); the own character faces where it runs and the camera's way while fighting; `--showcase=player` shows every animation in a row |
| M2-03 | **Done** 2026-10-21 (merged under standing approval): creatures fight back: the Fight tree's attack intent starts the species' move that reaches the focus (bite / pounce / charge); the AI gets real health, so hurt creatures flee more. Real hunts and herds defending their young follow with M2-09 (life stages) |
| M2-02c | **Done** 2026-10-21 (merged under standing approval): lock-on (stakeholder request): middle click / T, camera keeps the target in view, attacks aim at it; off on Hardcore servers (`--hardcore`, protocol 8 welcome rules) |
| M2-02b | **Done** 2026-10-21 (merged under standing approval): combat controls (left click / hold for heavy, Shift block, Space dodge), dash prediction, HUD bars, health bars and hit feedback; M2-02 complete |
| M2-02a | **Done** 2026-10-21 (merged under standing approval): block, parry, dodge, poise stagger, heavy attack and aimed `Attack` intent on the server; protocol 7 snapshots with health, combat action and move, and own vitals |
| M2-01 | **Done** 2026-10-21 (merged under standing approval): Combat contract v1.0.0 in Core: combat data (moves, rules, species stats), `Combatant`, moves with wind-up/active/recovery and arc/line/circle hits, damage formula, regeneration, yield and spare, defeat and kin memories; the F key attacks with the player's basic move |

## Changelog
- **0.1 (2026-10-21):** First version, after GDD v0.2 and DEC-003.
