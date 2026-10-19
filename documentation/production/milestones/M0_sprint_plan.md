# M0: Pre-Production & Quality Gates (Sprint Plan)
Window: 2026-10-20 → 2026-11-30 · Owner: Producer Agent
Velocity target: 10 tasks/week (config). Solo-adjusted plan: about 5–6 tasks/week, because one human executes for all agents.

All tasks follow `documentation/production/development_rules_enforcement.md` (Definition of Done, §3).

---
TASK: M0-01 Engine & toolchain setup
Agent: mechanics_developer · Est: 1d · **Status: Done (2026-10-19)**
Rules: R04, R05, R45
Docs reviewed (R51): ADR-001
Acceptance criteria:
  1. Godot 4.7.2-stable .NET opens `source/project-sumoner-worl/project.godot` with no errors
  2. `dotnet build` of the solution passes with warnings-as-errors
Evidence (2026-10-19): `Godot_v4.7.2-stable_mono_win64_console.exe --headless --import` → no errors; `--build-solutions` → SumonerWorld.Game.dll + SumonerWorld.Core.dll built; `dotnet build` 0 warnings / 0 errors.

TASK: M0-02 Quality-gate scaffolding (CI, analyzers, format, architecture tests)
Agent: mechanics_developer + qa_agent · Est: 1d · **Status: Done (Producer, 2026-10-19)**: local build and tests green; CI workflow is written but has not run on GitHub yet (see notes)
Rules: R05, R11, R12, R35, R36, R41, R45, R49
Acceptance criteria:
  1. `dotnet format --verify-no-changes` and `dotnet test` pass locally
  2. An architecture test fails if Core references Godot or uses unseeded RNG / wall-clock
  3. The PR template contains the Definition of Done checklist
Evidence (2026-10-19): `scripts/verify.sh` → format OK, build 0 warnings / 0 errors, 9/9 tests passed.
A deliberate violation file was compiled and **rejected**: RS0030 (banned `System.Random`), CA1031 (swallowed exception) and CS1591 (undocumented API). The file was then removed.
Notes: repository root is `projects/sumoner-worl` → https://github.com/pikkst/Sumoner_World (set up 2026-10-19). CI lives in `.github/workflows/ci.yml`; CODEOWNERS = @pikkst.

TASK: M0-03 Design pillars + GDD v0.1
Agent: sr_game_designer · Est: 3d · **Status: Done (stakeholder approved 2026-10-19, PR #1)**
Rules: R28, R29, R50, R53, R57
Acceptance criteria:
  1. 3 design pillars, with creature memory/bonding as Pillar #1
  2. Core loop (30 s / 5 min / session) written down
  3. Summoning uses contracts/rituals (no aim-and-throw capture)
  4. Hardcore audience: difficulty, death penalty and PvP stance defined
Evidence: `documentation/design/gdd.md` v0.1. Criteria 1–4 covered (Pillars; Core Gameplay Loop; Systems §1 Pacts; Systems §8–9 PvP/Death/Difficulty).

TASK: M0-04 AI Bible v0.1 (creature cognition contract)
Agent: sr_game_designer + mechanics_developer · Est: 4d · **Status: Done (stakeholder approved 2026-10-19, PR #2)**
Rules: R13, R14, R15, R16, R17, R18, R19, R28, R29, R50
Acceptance criteria:
  1. Memory model defined: event types, decay, capacity, persistence format
  2. Decision architecture: utility AI for goal selection + BT for execution, with the tick budget per LOD tier
  3. Bonding/ownership/command rules (R29) as a versioned contract
  4. Explicit statement: no generative AI in runtime loops (R16)
Evidence: `documentation/design/systems/ai_bible.md` v0.1. Criteria 1–4 covered (§4 memory model and persistence; §7 + §9 utility/BT and per-tier tick budgets; §10 Bonding Contract v1.0.0; §11 generative-AI policy).

TASK: M0-05 TDD v0.1 (Technical Design Document)
Agent: mechanics_developer · Est: 4d · **Status: Done (stakeholder approved 2026-10-19, PR #3)**
Rules: R03, R11, R12, R22, R24, R26, R30–R34
Acceptance criteria:
  1. Target hardware defined (min/rec spec) with CPU/GPU/memory budgets per frame (R22)
  2. Sim tick rate, ECS layout and determinism strategy
  3. Network model: intent → server validate → replicate, reconnect, idempotency keys
  4. Save versioning + migration strategy
  5. World chunk size and streaming radius
Evidence: `documentation/technical/architecture/tdd.md` v0.1. Criteria 1–5 covered (§1 hardware + frame/memory budgets; §2 30 Hz tick, ECS layout, determinism; §3 network model, reconnect, idempotency; §4 SQLite saves + migrations; §5 64 m chunks + streaming radii).

TASK: M0-06 System registry (anti-duplication)
Agent: mechanics_developer · Est: 0.5d
Rules: R47, R49
Acceptance criteria: `documentation/technical/architecture/system_registry.md` lists every planned system, its owner layer and its interface name

TASK: M0-07 M1 benchmark & playtest plan
Agent: qa_agent + data_scientist · Est: 2d
Rules: R22, R23, R42, R43
Acceptance criteria:
  1. Repeatable perf scenario: 200 active creatures, metrics captured (frame time p50/p99, sim ms/tick, allocations/tick)
  2. USP playtest script: "did the tester notice creature memory within 10 minutes?" (target ≥ 70%)
Evidence: `qa/test-plans/M1_benchmark_and_playtest.md`

TASK: M0-08 Legal & naming check
Agent: producer_agent (stakeholder action) · Est: 1d · **Status: Preliminary done 2026-10-19; formal clearance is a stakeholder action (before M2)**
Rules: R56
Acceptance criteria: ~~decision on title spelling~~ (done 2026-10-19: "Sumoner World"); trademark search done; capture-mechanic design reviewed against the Nintendo patent claims
Evidence: `documentation/production/reports/M0-08_legal_naming_check.md`. No exact-name conflict found; Summoners War (Com2uS) flagged as the main similarity risk. Design reviewed against JP7545191, JP7493117, JP7528390/US 12,409,387 and US 12,403,397; five guardrails added to the GDD. Open (stakeholder): attorney clearance search + filing, patent freedom-to-operate review, both before M2. Not an M0 exit blocker (exit gate lists M0-01…M0-07).

TASK: M0-09 Art style guide v0.1 (unblocked: style decided in DEC-002)
Agent: sr_game_artist + technical_artist · Est: 3d
Rules: R22, R24
Acceptance criteria: style pillars, creature silhouette rules, LOD/poly budgets aligned with the TDD

---
## M0 exit gate (Producer validation)
- [ ] M0-01…M0-07 Done per DoD
- [x] Open decisions answered: title, monetization, visual style (DEC-002)
- [ ] Stakeholder sign-off to start M1
