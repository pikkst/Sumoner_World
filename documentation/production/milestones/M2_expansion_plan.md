# M2 Expansion Plan (E): all classes, a bigger world, NPCs and a story

Date: 2026-10-22 · Owner: Producer Agent · Status: **Adopted (stakeholder direction, 2026-10-22)**

## Stakeholder direction (summarised)
- The test build is a **normal game package**, not a "family package". The testers are aged **7 to 37**, which is a normal
  range for the game, so the game is not made into a children's version.
- Add **all player classes**, make the **world bigger**, add **NPCs**, write the **game's story**, and fill every gap the team
  finds. **The idea of the game stays the same**; the team has the authority to improve and extend it.

The expansion runs before M2-18 (slice test and polish), so M2-18 tests the bigger game. Each task is one PR with green CI,
merged under the standing approval. The GDD's "M3" items that this pulls forward are marked in GDD 0.5.

## Tasks
| Task | Delivers | Est |
|---|---|---|
| **E1** Test package and audience | The family package becomes the **test package** (`scripts/package-game.sh`, `packaging/game/`, `builds/SumonerWorld-<commit>.zip`); DEC-003 revised (testers 7–37; still private, local Docker); this plan | 0.5d |
| **E2** Story and world bible | `design/content/story.md`: the history ("the Broken Pacts"), the four traditions, the antagonists, the regions, the main cast of NPCs, the main story's first chapter and a prologue per class; GDD 0.5 | 2d |
| **E3a** Magic schools | Storm, Umbral and Ember runes and three spells each, in data; a player casts the spells of their class's school; the action bar and ability book show them | 3d |
| **E3b** Class creatures | Two or more species per new class (Human, Necromancer, Dwarf) with looks, behaviour, skill trees, loot and growth, in data | 5d |
| **E3c** All classes at the Academy | The ranking answers vote for all four classes; a first creature for each class; a mentor NPC per class | 2d |
| **E4** A bigger world | 512 m → 2 km region with biomes (Academy vale and forest, highlands, marsh barrows, ash hills), each with its places, spawners and gathering; streaming and server budgets checked | 5d |
| **E5** NPCs and towns | NPCs from data (name, role, place, lines, daily walk); a hub town and an outpost per class tradition; **vendors** (buy and sell with gold, server transactions); talking reuses the Academy dialogue | 5d |
| **E6** Story chapter 1 | The main quest line across the regions, class prologues, story dialogue, a chapter boss | 5d |
| **E7** World map and travel | World map (M) with places, quest markers and the player; a compass; waystones to travel between visited towns | 3d |
| **E8** Day and night | The 48-minute day of the GDD: sun, sky and light; creatures and NPCs follow the time of day | 2d |
| **E9** Sound v1 | Sound effects (UI, hits, spells, steps, creature calls by mood) and ambient sound per biome, made in-house; the volume setting already drives them | 4d |

Then **M2-18** (slice test and polish) runs on the expanded game: a bot run of the main path in CI, test sessions, bug triage
to zero critical, the test package.

## Dependencies
E1 → E2 → E3a, E3b (in parallel) → E3c → E4 → E5 → E6 → E7, E8, E9 → M2-18.

## Status
| Task | Status |
|---|---|
| E1 | **Done** 2026-10-22: test package (`package-game.sh`, `packaging/game/`, new README with F1 and Esc), DEC-003 0.2 (testers 7–37, normal game package), this plan |
| E2 | **Done** 2026-10-22: story and world bible v0.1 (`design/content/story.md`): Aerwyn and the Vale of Oaths, the Breaking, the four traditions and their homes, the Binders and Warden Morvane, the cast, chapter 1 "Shards of the Oath" and four class prologues; GDD 0.5 |
