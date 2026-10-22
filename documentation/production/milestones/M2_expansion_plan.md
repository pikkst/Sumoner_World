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
| **E4a** A bigger world: ground and life | 512 m → 2 km region; biomes in data shape the ground (highlands, marsh, ash hills, ruins) and mountains wall it; 14 new places with spawners for every class's creatures and new gathering (ore, glowcaps); navigation baked in tiles (near the spawn first); entity capacity 1024 | 3d |
| **E4b** A bigger world: looks | Biome ground colours, plant cover and colours, the marsh's water, place looks (town, outposts, ruins, camp) | 2d |
| **E5a** People of the Vale | People from data (name, role, robe, greeting, lines, a walked round); Thornwick's innkeeper, merchant, smith, herbalist, scout, villagers and watchman; a mentor in each class home; talking to anyone (rounds follow the world tick, so server and clients agree) | 2d |
| **E5b** Trade and rest | **Vendors** sell from their stock and buy what players gather and loot (server transactions with gold); a shop window; rest at the inn | 3d |
| **E6** Story chapter 1 | The main quest line across the regions, class prologues, story dialogue, a chapter boss | 5d |
| **E7** World map and travel | World map (M) with places, quest markers and the player; a compass; waystones to travel between visited towns | 3d |
| **E8** Day and night | The 48-minute day of the GDD: sun, sky and light; creatures and NPCs follow the time of day | 2d |
| **E9** Sound v1 | Sound effects (UI, hits, spells, steps, creature calls by mood) and ambient sound per biome, made in-house; the volume setting already drives them | 4d |

Then **M2-18** (slice test and polish) runs on the expanded game: a bot run of the main path in CI, test sessions, bug triage
to zero critical, the test package.

## Dependencies
E1 → E2 → E3a, E3b (in parallel) → E3c → E4a → E4b → E5a → E5b → E6 → E7, E8, E9 → M2-18.

## Status
| Task | Status |
|---|---|
| E1 | **Done** 2026-10-22: test package (`package-game.sh`, `packaging/game/`, new README with F1 and Esc), DEC-003 0.2 (testers 7–37, normal game package), this plan |
| E2 | **Done** 2026-10-22: story and world bible v0.1 (`design/content/story.md`): Aerwyn and the Vale of Oaths, the Breaking, the four traditions and their homes, the Binders and Warden Morvane, the cast, chapter 1 "Shards of the Oath" and four class prologues; GDD 0.5 |
| E3a | **Done** 2026-10-22: Storm (spark, thunderclap, tailwind), Umbral (soulbolt, shade grip, veil) and Ember (firebolt, cinder burst, ember shield) in data; effects burning, quickened, weakened, warded, shielded; spell poise, drain and shields; a class casts only its school (server); the bar and ability book follow the class; school colours (combat spec 0.5 §8a) |
| E3b | **Done** 2026-10-22: six class creatures in data (storm hawk, plains hound; wisp hound, bone sprite; ember salamander, cinder mole) with 18 moves, skill trees, loot and items; data-driven looks (quadruped, avian, biped; colours, features, glow) for every species; `--showcase=species` (companions spec §12) |
| E3c | **Done** 2026-10-22: all four classes open at the Academy; Keepers Tomas, Nyx and Borra beside Lisel; only one's own Keeper brings the first creature (quest target `keeper`); the vote becomes the class's creature (`firstCreatures`); ceremony and Keepers name the student's Keeper (Academy spec §10) |
| E4a | **Done** 2026-10-22: the Vale of Oaths is 2 km (32 × 32 chunks); six biomes shape the ground and mountains wall it, the Academy's vale unchanged (old saves stand where they stood); 14 new places, 18 new spawners (every class's creatures, levels up to 9), ore and glowcap gathering; navigation in 64 tiles of 256 m, the 4 near the spawn before start (4.7 s), the rest in the background; entity capacity 1024 |
| E4b | **Done** 2026-10-22: biome ground colours (Terrain3D colour map, fallback vertex colours), plants thinned, grown and tinted by biome, the Mournfen's water, place looks (Thornwick's houses, outposts with class banners, ruins, the Binder camp); fix: navigation tiles now join (the bake area includes the border; the server checks a path across a tile edge at start); `--showcase=vale` |
| E5a | **Done** 2026-10-22: 12 people of the Vale in the region data (Thornwick: Marta, Pell, Hadda, Juniper, Scout Rook, Old Tam and Fenna on their rounds, Watchman Orrin; mentors Sael, Ilsa Varn, Wren and Dunrik); `IDataRegistry.People` (the Academy's first); talking to anyone, in reach of where they are now; greeting then lines; `PeopleView` |
