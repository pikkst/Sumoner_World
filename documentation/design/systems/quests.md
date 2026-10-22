# Quests: System Spec v0.1 (M2-19)

Status: **draft 0.1 (2026-10-22)**. The GDD (§10 open question 6) left quests without a design; this spec fills the v1
engine the M2 plan asks for (M2-19: definitions in data, server-side state per player, saved, objective events from combat,
Pacts and places, a quest log). Everything marked *(filled)* was chosen by the design team and can be changed by the
stakeholder. Dungeons, quest-given creatures (GDD §1) and the Academy's ranking quest (M2-21) build on this engine later.

## 1. What a quest is
- A quest has an **id**, a **title**, a short **summary**, the **level** it needs, the quests that must be **done before it**,
  one or more **steps** in order, and **rewards**.
- A step has a line of text ("Pick sweet berries by the path") and **1–4 objectives**; the step is done when all are done,
  then the next step starts.
- When the last step is done the quest is **done** and the rewards are given at once *(filled: no turn-in NPC until the
  Academy brings NPCs in M2-21)*. If the reward items do not fit in the bag, the quest waits, "ready", until they do.

## 2. Objectives (v1)
| Type | Counts | Target |
|---|---|---|
| `defeat` | creatures defeated that the player fought (everyone who hit it counts, as for loot) | a species, or any |
| `gather` | items gathered from nodes | an item, or any |
| `craft` | items made | an item, or any |
| `pact` | Pacts a creature accepted | a species, or any |
| `feed` | creatures fed | a species, or any |
| `help` | creatures freed from snares | a species, or any |
| `reach` | standing within a radius of a place (x, z, metres) | — |

Only progress made **while the quest is active** counts *(filled: items already in the bag do not count; standard MMO kill and
gather counting)*.

## 3. Taking and leaving quests
- *(filled)* Quests without a giver are **offered in the quest log** (J) as soon as the player meets their level and has done
  the quests before them; the player accepts them there. Quests with a giver (Academy NPCs, M2-21) are offered by talking to it.
- At most **10 active quests** *(filled)*. A quest can be **abandoned** (its progress is lost and it is offered again).
- v1 quests are done once per character *(filled: repeatable and daily quests later)*.

## 4. Rewards
Experience (through the normal level curve), gold and items. *(filled: no creature rewards in v1. Quests that lead to a
creature end at the creature, which still accepts or refuses a Pact itself, GDD §1.)*

## 5. The quest log and tracker
- **J** opens the quest log: available quests with **Accept**, active quests with their step, objectives and progress and
  **Abandon**, and done quests.
- The **tracker** at the top right shows the active quests' current objectives (e.g. "Bogfox 1/2").
- Lines tell the player when a quest is accepted, when an objective or step is done and when a quest is done, with its rewards.

## 6. Server rules
- Quest state is server state per account, part of the state hash, saved with the player by quest id (unknown ids are
  dropped on load). Accept and abandon are atomic, idempotent transactions.
- Progress comes from the simulation: the quest system reads defeats, gathers, crafts, Pacts, feeds and helps of the tick,
  and checks places twice a second.

## 7. Slice content (M2-19, path M2-16)
The slice is one path (`data/world/slice_region.json`), about 30 minutes, each quest opening the next:
1. **The Ranking** and **A First Friend** at the **Summoner Academy** (just south of the spawn).
2. **The Memory Grove** (40 m north): go there, feed two creatures; a mossback herd and an owl live there.
3. **Berries for the Road** and **A Staff of Your Own** in the **Gathering Meadow** (east of the grove): berries, roots and
   branches, two owls.
4. **The Bogfox Den** (north-east of the meadow, from level 2): a ring of rocks, a pack of four bogfoxes (levels 2-4) and their
   leader (level 6); defeat three with your companion.
Spawners keep each place lived in, also in worlds saved before the places existed; entering a place shows its name; the quest
tracker shows how far and which way the next place is.

## 8. Academy additions (M2-21a)
The Academy spec §2 (approved) lists more objective kinds; `talk` (to one of the Academy's people) and `answer` (the ranking
questions) now exist, and a quest can be `autoStart` (taken by every player as soon as it is offered: the ranking quest).
`spare` and `read` come with the arena moments (M2-21b); a creature reward with the first creature (M2-22). Quest progress is
counted after the players' intents of each tick.

## Changelog
- **0.2 (2026-10-22):** talk and answer objectives, autoStart, counting after intents (M2-21a).
- **0.1 (2026-10-22):** first draft with M2-19, filled by the design team.
