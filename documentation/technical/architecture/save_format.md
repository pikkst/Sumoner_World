# Save format and versioning (M2-15, R26)

Status: **in force** from M2-15 (2026-10-22). Owner: Lead Programmer.

The family's worlds must survive every update: a save written by any shipped build loads in every later build, and an old
server never damages a newer save.

## 1. Three layers
| Layer | Where | Versioned by | Rule |
|---|---|---|---|
| **Schema** | SQLite tables (`meta`, `items`, `players`, `creatures`, `reputation`, journal) | `meta.schema_version` and the ordered list `Migrations.All` | Never edit a shipped migration; add the next one. Before migrating an existing world the store makes a backup (`pre-migration-vN.db`). A database newer than the server is refused (`StoreVersionException`). |
| **Records** | MessagePack payloads in the rows (`PlayerRecord`, `CreatureRecord` and the records inside them), each prefixed with a payload version byte | integer `[Key]`s | Keys are only ever **added at the end**: never renamed, reused or removed. A new key has a default that means "not saved yet" (null, empty, 0). `SaveFormatTests.FrozenKeys` lists every shipped key and fails if one changes. |
| **Ids** | Items, species, quests, recipes in saves | stable string ids from the data | Saves name things by id, never by index (indices change when data is added). Unknown ids are dropped on load (an item, a quest) or left empty (a worn slot). |

## 2. Loading older saves
- Every field added since M1 loads with a default: growth is migrated from the species (M2-09), skills and combos start empty,
  nothing is worn, creatures have no bag, players have no quests and no Academy standing (so the ranking quest starts).
- The fixtures in `tests/fixtures/saves/` are loaded by every CI run:
  - `world-v1.db`: an M1 world with schema v1: migrated and backed up.
  - `world-v2.db`: an early M2 world (one creature, one player with a position only): restored into today's world.
  - `world-m2.db`: a world with every kind of M2 state (progress, inventory with gaps, worn gear, quests, Academy standing,
    a pacted creature with growth, skills, combos, gear and its own bag). Written once by
    `SW_WRITE_SAVE_FIXTURES=1 dotnet test --filter SaveFormat` and committed; never rewritten, a later milestone adds its own.

## 3. Game data on client and server
The client and server load the same data files, and saves and messages refer to items, quests and species by index on the
wire. Since protocol 24 the client sends a **data fingerprint** (FNV-1a over the files' relative paths and contents, line
endings ignored) in `Hello`; a server with other data refuses it with `DataMismatch`, and the client says "get the new family
package". So a package from before a data change can no longer join and show the wrong quests.

## 4. Checklist for a change that touches saved state
1. Add the field to the Core save type (`PlayerSave`, `CreatureSave`, …) with a default for older saves.
2. Add a new `[Key(n)]` at the end of the record, map it in `SaveMapping` / `WorldTables`, and add its name to
   `FrozenKeys`.
3. Add it to the round trip tests; if it is a new kind of state, extend a new milestone fixture.
4. A new table or index needs a new migration in `Migrations.All` and a migration test.
