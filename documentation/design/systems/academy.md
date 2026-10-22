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

## 6. Readable for every player
- Questions and answers are short, in plain words, and read aloud by the NPC voice later (M3); in M2 the dialogue box has a
  "read again" button. There are no wrong answers.
- The reading test uses clear, exaggerated body language; wrong answers are met kindly ("Look at its tail: is it tucked in?").
- Rank names are shown with a short, encouraging line ("Rank D: a promising student").

## 7. Implementation notes (M2-21a)
- **Data:** `data/academy/academy.json` (campus, people, the eight questions, rank thresholds and lines, open classes) and
  `data/academy/ranking_quest.json` (`the_ranking`, taken automatically by every new character). The slice quests need it done.
- **The campus** lies just south of the spawn point; the Headmaster waits at its north gate, 3.5 m behind where players
  arrive. The Arena Master and Keeper Lisel stand inside (the arena and the first creature come in M2-21b and M2-22).
- **Talking:** near a person, "[E] talk to …"; E opens the dialogue box (Read again, 1–4 to answer, Esc). The quest system v1
  gained `talk` and `answer` objectives (quests spec).
- **Ranking:** answers are kept per player on the server (saved); at the ceremony the server adds answer points × 3, the
  practical bonus (0 until M2-21b) and a seeded roll of −15…+15, and sets class (Elf in M2; another leaning is told) and rank
  F–C once. Rank sets the level cap and Bond Capacity (M2-20).
- **Not yet:** the vendor (later).

## 8. Implementation notes (M2-21b)
- **Campus:** radius 14 m around (256, 234), so the spawn point and the first meadow stay outside; the Headmaster and Arena
  Master wait together at the north gate; the **Grand Arena** (radius 5 m, a ring of posts) lies just inside it.
- **Safe zone:** inside the campus but outside the arena, players cannot attack, hit or cast, and creatures do not attack
  players; the screen says so.
- **Ranking quest steps:** 4 questions → the Arena Master → beat the **training construct** and offer it a Pact (a rehearsal:
  it never accepts, it is not alive) → read how a creature feels (lock on with T, pick Calm, Scared, Curious, Angry, Happy or
  Sad; a wrong answer is met kindly and can be tried again) → 4 more questions → ceremony.
- **Practical bonus:** +5 each for the construct, the rehearsal and a reading right the first time (+2 after a miss), at
  most 15, added to the rank score. The arena always keeps one construct standing.
- New quest objective kinds `read` and `offer`; protocol 23 (`Read`).

## 9. Implementation notes (M2-22)
- After the ranking, every student takes **A First Friend** at once: talk to Keeper Lisel (east of the campus) → she brings a
  young creature beside her → feed it → offer it a Pact.
- **Species:** each answer votes for one Elf creature (caring and steady → mossback, bold → bogfox, curious and wise → hollow
  owl; there is no pixie yet); the most votes wins, a tie goes to the seeded roll. **Tier** = the student's rank, level 1, young.
- The creature is **kept for that student**: their starter Pact always succeeds (any terms), anyone else's is refused. It is
  still a real bond: PactOffered, PactSealed and a new core memory **FirstBond** (+80, salience 900, never fades; AI Bible
  §5 table 23) are written, and from then on all the normal rules apply. It can be summoned (Z) at once.
- Saved: the creature with the world, the link to it with the student's standing.

## Changelog
- **0.2 (2026-10-21):** approved with the stakeholder's answers: the ranking quest now decides class and rank from **answers to
  questions plus randomness** (replacing the skill-only Arena Trial and the affinity gates); the class is for life; the first
  creature is **guaranteed**, of the student's class and a tier matching the rank (replacing "one of three chooses you"); the
  profile holds level, XP and gold.
- **0.1 (2026-10-21):** First draft (M2 D4).
