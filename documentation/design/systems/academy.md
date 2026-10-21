# Sumoner World - Summoner Academy & First Quest v1 (D4)
Version: **0.1** (M2 D4) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Draft, waiting for
stakeholder approval**
Rules: R08, R15, R26, R27, R30, R39 · Related: GDD v0.3 §1, §11; combat spec v0.2 (D1); companions spec (D2); AI Bible §6
(emotes), §10 (Pacts)

> The first hour of every character: arriving at the Summoner Academy, the Arena Trial (rank and faction), and the Bonding
> Ceremony. Also the minimal **quest system** the Academy questline runs on, which later quests reuse. Numbers are starting
> values in data (R08).

---

## 1. The Academy
- A walled campus at the edge of the test region: gate, courtyard, the **Grand Arena**, four faction quarters (only the Elves'
  open in M2; the others have closed doors with a "coming" sign), a workbench, a fireplace and the keepers' grove.
- **NPCs** (non-player characters, simple in M2: they stand, talk through a dialogue box and give quests): the **Headmaster**
  (welcomes, gives the rank), the **Arena Master** (runs the trial), and one **Keeper** per faction (the ceremony).
- The Academy is a **safe zone**: no combat outside the arena.

## 2. Quest system v1 (M2-19)
- **Quest definition** (data, `data/quests/*.json`): id, title and text keys (R39), a list of **steps**, each with
  **objectives** and an optional reward; prerequisites; repeatable or not.
- **Objective kinds v1:** `reach` (a place), `talk` (to an NPC), `defeat` (n creatures or constructs of a kind), `spare`
  (a yielded creature), `pact` (seal a Pact), `gather` (n items), `craft` (an item), `read` (identify a creature's emotion,
  §3.2), `choose` (a dialogue or gate choice).
- **Quest state** per player is server state, saved and versioned (R26). Objectives advance from **game events** the server
  already publishes (`CombatHit`, `CombatantDefeated`, `MemoryFormed`, Pact results, item changes), so quests never need their
  own hooks into other systems (R10).
- **Rewards:** items, a Pact opportunity (a creature introduced, never handed over), Academy standing, rank trial access.
- **Quest log** in the HUD: current step, objectives with progress, no markers that solve the puzzle for you (the reading test
  in §3.2 must stay a test).

## 3. The Arena Trial (first quest, about 20 minutes)
The Arena Master explains each test in one sentence. Every test gives **0–100 points**, from skill only, never luck (R15).

### 3.1 Body and blade
- Three training constructs attack one at a time, with clear telegraphs (D1). The student may block, dodge, parry and strike.
- **Score:** damage avoided (blocks, dodges, parries) 50%, constructs defeated 30%, time 20%. Failing (being knocked down)
  is allowed: the construct stops, the student gets up and continues with a lower score. No one fails the Academy.

### 3.2 Reading creatures
- Three Academy creatures in pens show an emotion each through body language only (AI Bible §6: fear, anger, curiosity, joy,
  ...). The student walks to the right sign for each ("Which one is afraid?").
- **Score:** correct answers ×30, +10 for answering all three without guessing twice. This is the game's USP taught in the
  first ten minutes.

### 3.3 Affinity gates (faction choice)
- Four gates, one per faction; each shows its tradition in a short vision (look, magic, creatures). The student walks
  through the one they want. In M2 only the **Elves' gate** is open; the others are visible and closed.
- No score: this is the player's choice (GDD §11).

### 3.4 Pact rehearsal
- A calm Academy creature with clear needs. The student chooses up to three terms and offers them (AI Bible §10.2). The
  creature's answer and reasons are shown.
- **Score:** how well the terms fit its needs and personality (the Pact evaluator's score, normalised), with a bonus for
  noticing the hint in its behaviour.

### 3.5 Rank
- **Total** = body 35% + reading 30% + Pact 35% → rank: **F** < 40, **E** 40–54, **D** 55–69, **C** ≥ 70 (data).
- The Headmaster gives the rank in a short ceremony. Rank is saved on the player profile and shown to other players
  (M2-20). It sets **Bond Capacity**: F 1, E 1, D 2, C 2 (higher ranks later raise it to the cap of 5).
- Rank trials for B and above come in M3.

## 4. The Bonding Ceremony (M2-22)
- At the faction's grove the Keeper brings **three young creatures** of the faction's species (Elves: a young mossback, a
  bogfox kit, and a third species to add in M2-16). Each has a rolled personality from its species (seeded, AI Bible §3).
- The student spends a few minutes with them: feed, play, talk (the normal interactions). Their opinions of the student form
  from that, like with any creature.
- Then the student **offers a Pact** (with the starter terms the Keeper suggests). The creature with the highest acceptance
  score that passes its threshold **chooses the student**; if more than one would accept, the student picks among them; if
  none would, the Keeper lets them spend more time. Nobody leaves without a companion, but it is always a real Pact.
- The ceremony writes a core memory (`FirstBond`, new event type) for the creature about the student.

## 5. Player profile (M2-20)
- Saved per account: display name, rank, faction, Academy standing, starter quest progress. Server state, versioned.

## 6. Children and families
- Texts are short and read aloud by the NPC voice later (M3); in M2 the dialogue box has a "read again" button.
- The reading test uses clear, exaggerated body language (style guide), and wrong answers are met kindly ("Look at its
  tail: is it tucked in?").
- Rank names are shown with a short, encouraging line ("Rank D: a promising student").

## 7. Questions for the stakeholder
1. **Ranks from the first trial: F to C (§3.5).** Is C the right top for a newcomer, or should a very good first trial reach B?
2. **Bonding Ceremony (§4):** the creature chooses the student through a real Pact, but no one leaves without a companion. Good?

## Changelog
- **0.1 (2026-10-21):** First draft (M2 D4).
