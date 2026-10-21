# Sumoner World - Summoner Academy & Ranking Quest v1 (D4)
Version: **0.2** (M2 D4) · Date: 2026-10-21 · Owners: Sr Game Designer + Mechanics Developer · Status: **Approved by stakeholder
2026-10-21** (with the changes in the changelog)
Rules: R08, R11, R26, R27, R30, R39 · Related: GDD v0.4 §1, §5, §11; combat spec v0.4 (D1); companions spec v0.2 (D2); crafting
spec v0.2 (D3); AI Bible §6 (emotes), §10 (Pacts)

> The first hour of every character: arriving at the Summoner Academy, the **ranking quest** that sets class and rank from the
> student's answers plus randomness, and the **guaranteed first creature**. Also the minimal **quest system** the questline runs
> on, which later quests reuse. Numbers are starting values in data (R08). Items marked *(filled)* were filled in by the design
> team.

---

## 1. The Academy
- A walled campus at the edge of the test region: gate, courtyard, the **Grand Arena**, four class quarters (only the Elves'
  open in M2), a workbench, a vendor, a fireplace and the keepers' grove.
- **NPCs** (simple in M2: they stand, talk through a dialogue box and give quests): the **Headmaster** (asks the questions,
  gives the rank), the **Arena Master** (the practical parts), one **Keeper** per class (the first creature), and a **vendor**.
- The Academy is a **safe zone**: no combat outside the arena.

## 2. Quest system v1 (M2-19)
- **Quest definition** (data, `data/quests/*.json`): id, title and text keys (R39), **steps** with **objectives** and optional
  rewards (gold, items, a creature), prerequisites; repeatable or not.
- **Objective kinds v1:** `reach`, `talk`, `answer` (pick an answer in a dialogue), `defeat`, `spare`, `pact`, `gather`, `craft`,
  `read` (identify a creature's emotion).
- **Quest state** per player is server state, saved and versioned (R26). Objectives advance from **game events** the server
  already publishes (combat, memories, Pacts, items), so quests need no hooks into other systems (R10).
- **Quest log** in the HUD: current step and objectives.

## 3. The ranking quest (first quest, about 20 minutes)
Stakeholder (2026-10-21): **the class and rank come from the student's choices in answers to questions, plus randomness.**

### 3.1 The questions
- The Headmaster asks **8 questions** (data, `data/quests/ranking_questions.json`), each with **4 answers**. Questions are about
  how the student would act ("A wounded creature blocks your path…"), what they value, and how they fight.
- **Every answer adds points to classes** (e.g. +2 Elf, +1 Dwarf) **and to a rank score** (0–3, for answers that show courage,
  care, wisdom). The text never says which answer gives what. *(filled)*
- Between the questions, the Arena Master runs short **practical moments** that teach the controls: a first fight against a
  training construct, reading a creature's emotion, and a Pact rehearsal (from v0.1). They add a small bonus to the rank score,
  so playing well helps too *(filled)*.

### 3.2 Class
- **Class** = the class with the most points; a tie is broken by the server's seeded random roll (R11).
- In M2 only the **Elf** class is open: the questions still score all classes, and a student whose answers point elsewhere is
  told "the Elves' grove calls to you for now" *(filled)*. The other classes open in M3.
- **The class is for life**: it cannot be changed later (stakeholder).

### 3.3 Rank, with randomness
- **Rank score** = answer points + practical bonus + a **random part** (the seeded roll, −15…+15 of 100) *(filled)*.
- Score → rank: **F** < 30, **E** 30–49, **D** 50–69, **C** ≥ 70. A newcomer starts between **F and C** *(filled)*; higher
  ranks are earned later.
- The Headmaster gives the rank in a short ceremony. Rank is saved on the player profile (M2-20). It sets the player's **level
  cap** (GDD §5) and **Bond Capacity**: F 1, E 1, D 2, C 2 (later ranks up to the cap of 5).
- Rank is raised later through **rank trials** (M3) but never bought.

## 4. The guaranteed first creature (M2-22)
- At the class's grove the Keeper brings **a young creature of the student's class, of a tier that matches the student's
  rank** (stakeholder: guaranteed). Which species: from the answers *(filled: the species whose traits fit the answers best, e.g.
  caring answers → a pixie, bold answers → a bogfox kit)*.
- The student spends a moment with it (feed, play) and offers the starter Pact, which **always succeeds** for this creature.
  It is still a real bond: the ceremony writes its first core memory of the student (`FirstBond`, new event type), and from
  then on it follows all the normal rules (obedience, trust, memories).
- The student can **summon it** from then on (M2-06).

## 5. Player profile (M2-20)
- Saved per account: display name, class (for life), rank, level and XP, gold, Academy standing, quest progress. Server state,
  versioned.

## 6. Children and families
- Questions and answers are short, in plain words, and read aloud by the NPC voice later (M3); in M2 the dialogue box has a
  "read again" button. There are no wrong answers.
- The reading test uses clear, exaggerated body language; wrong answers are met kindly ("Look at its tail: is it tucked in?").
- Rank names are shown with a short, encouraging line ("Rank D: a promising student").

## Changelog
- **0.2 (2026-10-21):** approved with the stakeholder's answers: the ranking quest now decides class and rank from **answers to
  questions plus randomness** (replacing the skill-only Arena Trial and the affinity gates); the class is for life; the first
  creature is **guaranteed**, of the student's class and a tier matching the rank (replacing "one of three chooses you"); the
  profile holds level, XP and gold.
- **0.1 (2026-10-21):** First draft (M2 D4).
