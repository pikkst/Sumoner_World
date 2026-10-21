# Sumoner World - Items, Loot, Gold & Crafting v1 (D3)
Version: **0.2** (M2 D3) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Approved by stakeholder
2026-10-21** (with the changes in the changelog)
Rules: R08, R26, R27, R30, R31, R34 · Related: GDD v0.4 §1 (Pact items), §5, §6 Crafting & Sanctum, §8 Trading, §10 Equipment and
boss eggs; combat spec v0.4; companions spec v0.2 (D2); DEC-002

> What the M2 slice needs (stakeholder: "a basic loot system: gold, item drops, inventory" in the first version): items,
> inventory, gold, loot drops, gathering, recipes, equipment, and the safety rules every item change follows. Trading between
> players is M3, but the item model is built for it now. Numbers are starting values in data (R08). Items marked *(filled)* were
> filled in by the design team.

---

## 1. Principles
1. **Items and gold are server state.** The client asks; the server decides (R30, R31). Nothing appears, disappears or doubles
   outside a server transaction (R27).
2. **Every change is atomic and idempotent** (R34). The transaction journal from M1-17 covers crash safety.
3. **Gear has a ceiling** (Pillar 3). Skill and companions matter more than item numbers.
4. **Never pay-to-win** (stakeholder, DEC-002): no gold, item, creature or boss egg is ever sold for real money.

## 2. Item model
- **Item definition** (data, `data/items/*.json`): id, name key, category (`material`, `food`, `offering`, `weapon`, `armour`,
  `charm`, `tool`, `quest`, `egg`), stack size, weight, tags, **gold value** (what a shop pays), and for gear: slot, body types
  allowed, stats (power, guard, resist, stamina), a minimum level, liked/disliked material tag (D2 §6).
- **Item instance:** stackable items are a count in a slot. Gear is a **unique instance** with a server-issued id, its
  definition and its condition (durability 0–100).
- **Inventory:** the player carries 24 slots and a weight limit; the Sanctum chest has 60 slots (M2: one chest).
- **Soulbound items:** quest items, the Academy starter gear and unhatched boss eggs cannot be traded or dropped.

## 3. Gold (first version)
- **Gold** is the one currency, a whole number per player (server state, saved). It never goes below 0.
- **Earned** from loot drops (§4), quest rewards, and selling items to Academy and Sanctum vendors (their gold value).
- **Spent** at vendors (basic tools, food, recipes), on skill respecs (D2 §5) and repairs. Player-to-player gold trades come with
  trading in M3.
- No way to buy gold with real money, ever.

## 4. Loot drops (first version)
- A defeated **creature, construct or boss** drops loot from its **loot table** (data, `data/items/loot_tables.json`): gold
  (a range) and entries of `(item, chance, min–max count)`. Rolls use the world's seeded random stream (R11): deterministic for
  replays, random for the player.
- Loot appears as a **loot bag** at the place of defeat, visible to everyone who took part; each player picks up **their own
  share** (personal loot *(filled)*: no stealing, no fighting over drops between family members).
- Loot bags last 5 real minutes, then vanish.
- **Killing still has consequences** (GDD §6): the herd remembers. A creature that is **spared** (combat spec §6) drops nothing
  but may later leave a gift (a shed feather, a berry) where the player rests *(filled)*.
- **Boss eggs** (D2 §7) are an entry in a world boss's loot table, with its conditions checked for each player.
- **Slice loot tables** *(filled)*: mossback: 2–6 gold, shed moss (60%), horn chip (15%); bogfox: 3–8 gold, fox fur (50%),
  bright tooth (10%); training constructs: 1–3 gold, scrap (40%).

## 5. Gathering
| Source | What | Rule |
|---|---|---|
| **Plants** (herbs, berries, roots) | food, offering and potion materials | regrow after an in-game day |
| **Ore and stone** | metal and stone | need a tool; come back after 3 days |
| **Wood** | wood, resin | fallen branches by hand, trees with an axe |
| **Creature materials** | shed fur, moulted scales | found where creatures rest, or given by companions |

Gathering amounts are deterministic from the node and the tool. Companions with the *Scout* or work skills can gather when asked
and if they want to.

## 6. Crafting
- **Recipes** (data): inputs, output, station (`hand`, `campfire`, `workbench`, `forge`), time, and an unlock rule (known at
  start, bought from the Academy, or found).
- Crafting takes time and can be interrupted by combat (inputs are only taken when it completes).
- **Slice recipes:** meal, Pact offering *Sweetroot bundle*, sigil ink, wooden staff, leather vest, mossback charm.

## 7. Pact items (GDD §1, M0-08)
- **Offerings** meet a species' needs or a Pact term and raise the acceptance score through the Pact term values. They are
  **given**, not thrown.
- **Sigil inks** let the player offer one extra term (up to 3). **Scents** draw curious creatures closer.
- Pact items never show a chance, never capture, are never thrown at a creature (M0-08, binding), and **never work across
  classes** (D2 §2).

## 8. Equipment
- Player slots: weapon, off-hand, head, body, charm ×2. Companion slots by body type (D2 §6). Gear has a minimum level.
- Equipping is a server transaction. Gear stats add to the combat stats (combat spec §2); the weapon chooses the player's basic
  move set.
- **Durability:** gear loses condition with use; repaired at a workbench with materials and a little gold.

## 9. Death and items (GDD §9)
- On defeat the player's **carried** materials, food and **a share of carried gold (20% *(filled)*)** stay in a **grave** at the
  place of defeat; equipped gear stays on the player. The grave lasts 30 real minutes.
- On **Normal** servers equipped gear loses 10% condition; on **Hardcore**, equipped gear also goes into the grave.

## 10. Safety rules (tests)
- Duplicate intent ids never give two items or twice the gold (R34). Clients only send intents (R31).
- Crash during crafting, a pick-up or a move between inventory and chest: after restart the item or gold is in exactly one place.
- Save migration: inventory, gold and gear are part of the versioned save (R26).

## 11. Implementation notes (M2-13)
- **Data:** `data/items/items.json` (8 slice items), `loot_tables.json` (§4's slice tables, plus the hollow owl's feathers) and
  `gather_nodes.json` (berry bush, sweetroot patch, fallen branches; plants regrow after a day, branches after three).
- **Inventory:** 24 slots, 30 kg, gold; server state per account, part of the state hash; saved with the player by item id
  (unknown ids are dropped on load). The client gets it when it changes (`InventoryList`, protocol 16) and shows it with I; the
  HUD shows the gold.
- **Loot:** every player who hit a creature is one of its fighters (up to four); when it is defeated, each gets a personal bag
  rolled from the species' table with the world's seeded random stream, visible only to them (snapshot kind `Loot`), lying
  where it fell for 5 real minutes. Walking onto it sends `PickUp`. Bags are not saved (they last minutes).
- **Gathering:** nodes are placed from a fixed seed around the spawn point at every start (4 of each kind; not saved); H sends
  `Gather` for the nearest one that has grown back (snapshot kind `Node`, with or without its fruit).
- **Safety:** pick-up and gathering are atomic transactions: everything fits or nothing changes; a repeated intent gets the
  first answer (tests). Graves on death (§9), vendors (§3) and trading come later.

## 12. Implementation notes (M2-14)
- **Data:** `data/items/recipes.json` (six hand recipes: berry meal, sweetroot bundle, sigil ink, wooden staff, leather vest,
  mossback charm); gear items name their `slot`, `wearers` (`player` and body types), `minLevel` and `stats` (power %, guard,
  resists, stamina). The loader checks every recipe's items, counts and time, and that gear names known wearers.
- **Crafting:** B opens the panel, a number starts a recipe (`Craft`, protocol 17). It takes the recipe's time; a hit on the
  crafter or a move it starts stops it and nothing is used up; when the time is up, inputs and output change in one step if
  they still fit. Only hand recipes for now: campfire, workbench and forge recipes are refused until the Sanctum has them.
- **Equipment:** six positions (weapon, off hand, head, body, two charms); creatures use only those their body has
  (quadrupeds head, body, a charm; birds head, a charm). Put on and taken off by dragging in the character window (§13,
  M2-14b). Gear is refused below its level or on the
  wrong body. Stats are recomputed from scratch (base, level, skills, gear), so nothing adds up twice; worn gear is saved by
  item id with the player and with each creature.
- **Safety:** craft, equip and unequip are atomic, idempotent transactions (tests); equipping on someone else's creature is
  refused. Durability, quality, stations and recipe discovery come later.

## 13. Bags, the character window and the action bar (M2-14b)
Stakeholder decision 2026-10-22: skills and inventory follow the usual MMO/RPG layout.
- **Action bar:** ten buttons at the bottom on the keys 1-9 and 0. A button holds a spell, a companion command, an item
  (food is eaten, gear put on) or, from M2-14c, a companion's skill. The player drags actions onto it from the ability book
  (L) and items from the bag, drags a button onto another to swap them, and off the bar (or right-click) to clear it. The
  first layout: spells on 1-3, berry meal on 5, commands on 7-0. The layout is kept per character on the player's computer.
  C/V/G and the command keys 1-4 are gone.
- **Character window (I):** the chosen wearer on the left (the player, or a companion at their side, by tabs) drawn with
  the equipment slots where they are worn (head above, body in the middle, weapon and off hand at the sides, charms below; a
  creature shows only the slots of its body), the player's bag on the right (24 slots, gold, weight). Items are dragged
  between the bag, the slots and the companion's bag; right-click uses food, puts gear on, or takes it off.
- **Creature bags:** every pacted creature carries its own bag, sized by its body: birds 4 slots and 3 kg, four-legged
  creatures 8 slots and 15 kg, two-legged 6 and 10 kg, others 6 and 8 kg. Items move only while it is at its owner's side;
  a whole stack moves at once, if it all fits. The bag is saved with the creature.
- **Bag order:** items stay in the slot they were put in (also through a save); dropping on the same item tops it up,
  dropping on another item swaps them.
- **Food:** berries give back 5 health; a berry meal 40 health and 30 stamina, for the player or a companion at their side.
- **Protocol 18:** MoveItem, TransferItem and UseItem intents; inventory slots carry their index; companions carry their bag.
- **Companion skills (M2-14c):** a learned move is dragged from the companion panel (K) onto the bar. Pressing it commands
  the companions at one's side to attack the target (lock-on, else the creature in reach), and those that know the move and
  obey try it first, waiting up to 5 seconds for it to reach. Obedience works as for any command: a companion may refuse.
  Protocol 19 (`CompanionSkill`).

## Changelog
- **0.2 (2026-10-21):** approved with the stakeholder's answers: a first loot system with **gold, item drops and inventory**
  (§3, §4, personal loot bags), so killing now drops loot (its memory consequences stay); gold vendors, respecs and repairs;
  boss eggs in loot tables; never pay-to-win.
- **0.1 (2026-10-21):** First draft (M2 D3).
