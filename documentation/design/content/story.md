# Sumoner World - Story and World Bible v0.1 (E2)

Version: **0.1** · Date: 2026-10-22 · Owner: Sr Game Designer + Narrative · Status: **Adopted under the stakeholder's
expansion mandate (2026-10-22)**: "write the game's story; the idea stays the same". Related: GDD 0.5 (§11, Content), Academy
spec (D4), AI Bible (memories, rumours), `production/milestones/M2_expansion_plan.md`.

> The GDD's framing was one line: *the broken Pacts, a world where summoners once betrayed creatures.* This bible turns it into
> a history, a place, a cast and a main story that the quests (E6) are built from. Everything here serves the pillar
> **"Every creature remembers"**: the story is about what happens to a world when promises to creatures are broken, and what
> it takes to earn their trust again.

---

## 1. Tone
- **Mythic, warm and a little sad.** Bright forests and friendly towns over an old wound. The danger is real, the villains have
  reasons, and kindness is the hero's strongest tool, not a weakness.
- **For players aged 7 and up and for adults alike** (testers are 7–37, DEC-003 0.2): plain sentences, no gore, no cruelty
  shown on screen. Loss is shown through creatures' grief and memories, never through injury.
- Creatures are never props: every story beat that involves a creature lets the player choose how to treat it, and the
  creature remembers the choice.

## 2. The world in one page
- The world is **Aerwyn**. The game's first region is **the Vale of Oaths**, a wide valley ringed by highlands, a marsh and
  ash hills, with the **Summoner Academy** and the town of **Thornwick** at its heart.
- Long ago, people and creatures lived by **Pacts**: freely offered promises. A summoner promised (to feed, to protect, to
  never leave a companion behind) and a creature chose to accept. Every Pact ever made was echoed in the **Oathstone**, a
  great white stone in the middle of the Vale, and creatures could feel through it that the promises were kept.
- About three hundred years ago, the **Iron Covenant** learned to bind creatures **without their consent**, with iron
  collars and a forged "Chain Pact". For a few years the Covenant's armies of bound beasts ruled the Vale. When the creatures
  finally broke free, the backlash **shattered the Oathstone**. That night is called **the Breaking**.
- Since the Breaking, **creatures remember**. Not only what one summoner did to them, but, faintly, what summoners did to
  their kind: wild creatures are wary of anyone who carries a sigil (this is why the game starts every creature cautious,
  and why rumours between creatures matter, AI Bible §9).
- The **Summoner Academy** was founded after the Breaking by the four traditions together, to teach the old way: a Pact is a
  promise, and a promise is kept.

## 3. The four traditions (the player classes)
Every tradition summons in its own way, with its own magic school and its own creatures. A creature only ever answers a
summoner of its own tradition (GDD §1, §11). The class is for life.

| Tradition (class) | Home in the Vale | Magic school | Belief | Creatures (E3b) |
|---|---|---|---|---|
| **Elves** | **Silverleaf Glade** in the Elderwood (north of the Academy) | **Primal**: growth, roots, healing | "Every creature is kin." The oldest summoners; they lost the most in the Breaking | mossback, bogfox, hollow owl, (later) pixie |
| **Humans** | **Fort Calder** on the Stormreach highlands (north-west) | **Storm**: lightning, wind, speed | "A bond is a duty." Disciplined, organised, proud of keeping their word; their ancestors fought the Covenant | storm hawk, plains hound |
| **Necromancers** | **Lantern Hollow** in the Mournfen marsh (south-west) | **Umbral**: spirits, shadow, binding of the self | "Nothing is truly gone while it is remembered." They care for creatures' spirits, who remember a past life | wisp hound, bone sprite |
| **Dwarves** | **Emberdeep Gate** in the Ashen Reach (east) | **Ember**: fire, forge, wards | "What is made well lasts." Smiths and builders; they forged the Covenant's collars once, unknowingly, and have made amends ever since | ember salamander, cinder mole |

- **Necromancers** are not villains: they are the Vale's keepers of memory and of the dead. Their creatures are friendly
  spirits and bone sprites, a little spooky, never gory.
- **The Dwarves' guilt** is a quiet thread: the collars were made in their forges under a lie. Forgemaster Dunrik's prologue is
  about it.

## 4. The antagonists
- **The Binders.** Masked collectors in grey cloaks who catch creatures with iron collars and take them away. Creatures that
  come back are **Hollowed**: calm, obedient and empty. The Binders tear memories out to make a creature obey. A Hollowed
  creature has no memories of anyone, so it trusts no one and fears no one (a strong, readable case for the AI Bible's
  memory model).
- **Warden Morvane.** The Binders' leader, once the Academy's most gifted teacher of Pact lore, expelled for experiments on
  creatures' memories. She believes memory is what makes creatures suffer: "A creature that forgets its pain is a creature at
  peace." She wants to forge a **new Chain Stone** from the **shards of the Oathstone**, so that every creature in the Vale
  obeys and forgets. She is calm, polite and certain she is right.
- **Iron Warden Kask.** Morvane's lieutenant in the field and the first chapter's boss: a former Fort Calder soldier who rides
  a collared **Thornback**, an old apex creature.

## 5. The Vale of Oaths (the 2 km region, E4)
The Academy guards the Vale's south-western gate, where the old roads meet; the Vale opens north and east from it, and
mountains ring it all round. (Coordinates in metres, x east, z north; the data is `data/world/slice_region.json`.)

| Area | Where | Biome | What is there |
|---|---|---|---|
| **Summoner Academy** | south-west (256, 234) | the Academy's vale, meadow | campus, Grand Arena, the four Keepers; safe zone |
| **Thornwick** | beside the Academy (190, 300) | meadow, farms | the hub town: inn **The Kept Promise**, general goods, smith, herbalist, the town waystone, the notice board |
| **Elderwood** | north of the Academy (520, 680) | forest | the **Memory Grove**, the **Gathering Meadow**, the **Bogfox Den** at its edge; **Silverleaf Glade** (Elves); the Deep Elderwood |
| **Stormreach** | north (420, 1480) | highlands | **Fort Calder** (Humans), the Windy Ridges, the **Hawk Cliffs**, a **Binder camp** |
| **Mournfen** | east (1480, 520) | marsh | **Lantern Hollow** (Necromancers), the Heart of the Fen, the **Old Barrows**, will-o'-wisps at night |
| **Ashen Reach** | north-east (1560, 1520) | ash hills | **Emberdeep Gate** (Dwarves), the Ash Fields, the **Cold Forge** |
| **The Shattered Ring** | the middle of the Vale (960, 920) | ruins | where the Oathstone stood: a ring of broken white stone, the chapter's end |

## 6. Main cast
| Name | Who | Where | Role |
|---|---|---|---|
| **Headmaster Oriel** | the Academy's head, an old Elf | Academy gate | ranks the students; the voice of the old way |
| **Arena Master Brann** | a Dwarf veteran | Academy arena | teaches fighting; knows Kask from the old wars |
| **Keeper Lisel** | Elf keeper | Academy, east | the Elves' first creatures |
| **Keeper Tomas** | Human keeper | Academy, west | the Humans' first creatures |
| **Keeper Nyx** | Necromancer keeper | Academy, south-west | the Necromancers' first creatures |
| **Keeper Borra** | Dwarf keeper | Academy, south-east | the Dwarves' first creatures |
| **Warden Sael** | Elf ranger | Silverleaf Glade | Elf prologue; the missing mossbacks |
| **Captain Ilsa Varn** | Human officer | Fort Calder | Human prologue; Kask was once her captain |
| **Mother Wren** | Necromancer lantern-keeper | Lantern Hollow | Necromancer prologue; a spirit that cannot rest |
| **Forgemaster Dunrik** | Dwarf smith | Emberdeep Gate | Dwarf prologue; the collars' secret |
| **Marta** | innkeeper | Thornwick, The Kept Promise | rumours, rest, the notice board |
| **Pell** | merchant | Thornwick | general goods (food, ink, bags) |
| **Hadda** | smith | Thornwick | weapons and armour |
| **Juniper** | herbalist | Thornwick | herbs and food, buys what players gather |
| **Scout Rook** | Academy scout | Thornwick, then everywhere | the main story's guide |
| **Warden Morvane** | the Binders' leader | (unseen until the end of chapter 1) | antagonist |
| **Iron Warden Kask** | Binder lieutenant | Binder camps, the Shattered Ring | chapter 1 boss |

## 7. The main story: Chapter 1, "Shards of the Oath" (E6)
About 3–4 hours. Each step is a quest; the class prologue (§8) sits between steps 2 and 3.
1. **A Student No Longer** (after the Academy's ranking and first creature): Headmaster Oriel sends the new summoner to
   Thornwick: creatures have gone missing, and Scout Rook needs help.
2. **Empty Eyes**: Rook shows the player a **Hollowed** mossback near the Memory Grove. It does not react to food or kindness.
   The player learns about the Binders.
3. **Home Ground**: the class prologue (§8).
4. **The First Shard**: a Binder camp in the Stormreach is digging at the Hawk Cliffs. The player finds a **shard of the
   Oathstone** and learns that the Binders are collecting them. Collared creatures guard the camp: the player can **free**
   them (break the collar by defeating them without killing, or by freeing them while snared) instead of killing them, and
   freed creatures remember it.
5. **What the Fen Remembers**: in the Old Barrows of the Mournfen, the spirits show the player the night of the Breaking
   (a short vision told through creatures).
6. **The Cold Forge**: in the Ashen Reach, the Dwarves' old forge where the collars were made. The player finds Morvane's
   orders and the second shard.
7. **The Shattered Ring** (chapter boss): Iron Warden Kask waits at the ruins of the Oathstone with his collared Thornback.
   The player and their companions defeat Kask; the Thornback can be **freed** and spared. Its collar breaks, and it
   remembers who freed it. Kask escapes, dropping a letter signed **M.**
8. **The Name in the Letter**: back at the Academy, Oriel recognises the hand: Warden Morvane. Chapter 2 is set up.

**Rewards** across the chapter: gold, gear from the class home, a rank trial at the end (E → D or D → C, later), the first
Oathstone shard as a keepsake.

## 8. Class prologues (one per class, about 30 minutes each)
| Class | Quest | Story |
|---|---|---|
| Elf | **The Silent Herd** | Warden Sael's mossback herd lost two calves to Binders. Track them through the Elderwood, find the collar marks, and bring back the one that escaped. It is frightened of everyone, and the player wins its trust with food and patience |
| Human | **Old Colours** | Captain Ilsa Varn asks the player to carry orders to the Hawk Cliffs and learns that her old captain, Kask, leads the Binders there. Train with a plains hound, protect a storm hawk nest |
| Necromancer | **The Lantern That Would Not Go Out** | Mother Wren's oldest lantern holds a wisp that cannot rest because it forgot its name. Find three memories (objects) in the Barrows and give the wisp its name back |
| Dwarf | **Iron and Shame** | Forgemaster Dunrik admits the Dwarves forged the collars. Gather ore and fire-salts, make a **collar-breaker** at the forge with an ember salamander's help, and test it on a broken collar |

## 9. Story rules for quests and dialogue
- Every story NPC has **one short line** they say when the player passes, and a **dialogue** when spoken to (E5).
- Dialogue lines are short (at most two sentences). The player answers with short choices where it matters.
- **Choices that matter to creatures are remembered by the creatures**, not by a hidden morality meter.
- Names and places in quest text use the names in this bible. New names are added here first.

## Changelog
- **0.1 (2026-10-22):** first version under the stakeholder's expansion mandate (E2).
