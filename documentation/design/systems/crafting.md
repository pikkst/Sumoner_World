# Sumoner World - Items, Gathering & Crafting v1 (D3)
Version: **0.1** (M2 D3) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Draft, waiting for
stakeholder approval**
Rules: R08, R26, R27, R30, R31, R34 · Related: GDD v0.3 §1 (Pact items), §6 Crafting & Sanctum, §8 Trading, §10 Equipment;
combat spec v0.2; companions spec v0.1 (D2); DEC-002

> What the M2 slice needs: items, inventory, gathering, recipes, equipment, and the safety rules every item change follows.
> Trading between players is M3, but the item model is built for it now. Numbers are starting values in data (R08).

---

## 1. Principles
1. **Items are server state.** The client asks; the server decides (R30, R31). An item never appears, disappears or doubles
   outside a server transaction (R27).
2. **Every change is atomic and idempotent** (R34): an inventory change either happens fully or not at all, and a repeated
   intent id changes nothing twice. The transaction journal from M1-17 covers crash safety.
3. **Gear has a ceiling** (Pillar 3). Skill and companions matter more than item numbers.
4. **Nothing is sold for money** that affects play (DEC-002).

## 2. Item model
- **Item definition** (data, `data/items/*.json`): id, name key, category (`material`, `food`, `offering`, `weapon`, `armour`,
  `charm`, `tool`, `quest`), stack size, weight, tags, and for gear: slot, body types allowed, stats (power, guard, resist,
  stamina), liked/disliked material tag (D2 §5).
- **Item instance:** stackable items are a count in a slot. Gear is a **unique instance** with a server-issued id (for trading
  and equipment), its definition, and its condition (durability 0–100).
- **Inventory:** the player carries 24 slots and a weight limit; the Sanctum chest has 60 slots (M2: one chest).
- **Soulbound items:** quest items and the Academy starter gear cannot be traded or dropped.

## 3. Gathering
| Source | What | Rule |
|---|---|---|
| **Plants** (herbs, berries, roots) | food, offering and potion materials | regrow after an in-game day; a node gives 1–3 by tool and skill |
| **Ore and stone** | metal and stone | need a tool; deplete and come back after 3 days |
| **Wood** | wood, resin | fallen branches by hand, trees with an axe |
| **Creature materials** | shed fur, moulted scales, antlers | found where creatures rest, or given by companions; **never from killing** in v1 |

- **Harvesting creatures has consequences** (GDD §6): v1 avoids kill-loot on purpose. Killing gives nothing but the memories
  others keep of it. (Killing for materials is a design question for M3, with the hunting reputation rules.)
- Gathering amounts are **deterministic** from the node and the player's tool (no random drops in v1, as in D1).
- Companions with the *Scout* or work skills can gather on their own when asked and if they want to (GDD §6).

## 4. Crafting
- **Recipes** (data, `data/items/recipes.json`): inputs, output, station (`hand`, `campfire`, `workbench`, `forge`), time in
  seconds, and an unlock rule (known at start, learned from the Academy, or found).
- **Stations in the slice:** hand and campfire anywhere; workbench at the Sanctum and the Academy.
- Crafting takes time and can be interrupted by combat (the inputs are only taken when it completes, so nothing is lost).
- **Slice recipes:** meal (berries + root, campfire), Pact offering *Sweetroot bundle* (root + herb), sigil ink (resin + ore),
  wooden staff (wood + resin, workbench), leather vest, mossback charm (shed fur + resin).

## 5. Pact items (GDD §1, M0-08)
- **Offerings** meet a species' needs or a Pact term (food a species likes, a token of territory). They raise the acceptance
  score through the existing Pact term values (AI Bible §10.2). They are **given**, not thrown.
- **Sigil inks** let the player offer one extra term in a Pact (up to the contract's limit of 3).
- **Scents** draw curious creatures of a species closer, so they can be met; they do not change the decision.
- Pact items never show a chance, never capture, and are never thrown at a creature (M0-08, binding).

## 6. Equipment
- Player slots: weapon, off-hand, head, body, charm ×2. Companion slots by body type (D2 §5).
- Equipping is a server transaction. Gear stats add to the combat stats (combat spec §2); the weapon chooses the player's basic
  move set (sword: light/heavy; staff: light/heavy with Primal casting bonus).
- **Durability:** gear loses condition with use; at 0 it gives no stats until repaired at a workbench (materials, not money).

## 7. Death and items (GDD §9)
- On defeat the player's **carried** materials and food stay in a **grave** at the place of defeat; equipped gear stays on the
  player. The grave is owned by the player and lasts 30 real minutes (data). Companions may guard it (bond level 4).
- On **Normal** servers equipped gear loses 10% condition; on **Hardcore**, equipped gear also goes into the grave.

## 8. Safety rules (tests)
- Duplicate intent ids never give two items (R34); a forged "add item" from a client is impossible because clients only send
  intents (R31).
- Crash during crafting or a move between inventory and chest: after restart the item is in exactly one place (journal, M1-17
  spike 3 style test).
- Save migration: inventory and gear instances are part of the versioned save (R26).

## 9. Questions for the stakeholder
1. **No loot from killing creatures in v1 (§3):** materials come from plants, ore, and what creatures shed or give. Is that
   right for the game, or do you want killing to give materials (with the reputation cost)?
2. **Money:** v1 has no coins; things are made or found. Should there be a currency (for Academy respec, trading, shops) from
   M2, or wait for trading in M3?

## Changelog
- **0.1 (2026-10-21):** First draft (M2 D3).
