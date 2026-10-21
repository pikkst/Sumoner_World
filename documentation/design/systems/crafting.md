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

## Changelog
- **0.2 (2026-10-21):** approved with the stakeholder's answers: a first loot system with **gold, item drops and inventory**
  (§3, §4, personal loot bags), so killing now drops loot (its memory consequences stay); gold vendors, respecs and repairs;
  boss eggs in loot tables; never pay-to-win.
- **0.1 (2026-10-21):** First draft (M2 D3).
