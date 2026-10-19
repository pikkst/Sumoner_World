# Development Rules Enforcement
Owner: Producer Agent · Effective: 2026-10-19 · Source of truth: `project-config.json` → `development_rules` (R01–R57, numbered in order)

Every task assignment cites the rule IDs it touches. Any deliverable that violates a rule is **rejected** until fixed or until a waiver is recorded in the Waiver Log at the bottom of this file (see `DEVELOPMENT_RULES.md`).

## 1. Enforcement layers
| Layer | Mechanism | Blocks merge? |
|---|---|---|
| **A: Build** | `Directory.Build.props`: `Nullable=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-recommended`, `EnforceCodeStyleInBuild=true` | Yes |
| **B: Format/lint** | `.editorconfig` + `dotnet format --verify-no-changes`. GDScript: `gdlint` + `untyped_declaration=error` | Yes |
| **C: Tests** | `dotnet test` (unit + integration + architecture tests) in CI | Yes |
| **D: Architecture tests** | Automated: Core has no Godot reference; layer direction is respected; no `System.Random`/`DateTime.Now` in Core | Yes |
| **E: Review** | PR template checklist + Producer/QA review for gameplay, AI, net, persistence and security code | Yes |
| **F: Milestone gate** | Producer milestone checklist, perf captures and playtest evidence | Blocks phase release |

## 2. Rule → enforcement matrix
| Rule | Summary | How it is enforced | Layer |
|---|---|---|---|
| R01, R06 | SOLID/DRY/KISS, small focused units | CA1502/CA1506 complexity and coupling analyzers. Review: methods ≤ 40 lines, classes ≤ 300 lines (guideline). | A, E |
| R02 | Modular, component-based | Project and layer structure (ADR-001) | D |
| R03 | ECS for hot paths | Creatures, projectiles and perception live in the Core ECS. Review rejects per-entity `_Process` for these. | E |
| R04 | Established engine/language/naming | ADR-001. `.editorconfig` naming rules. | A, B |
| R05 | Strong typing | `Nullable` + warnings-as-errors. `dynamic` is banned (analyzer). GDScript typed-only. | A, B |
| R07 | DI + interfaces | Constructor injection via a composition root. Review rejects `new` of services inside systems and static service locators. | E |
| R08, R48 | Data-driven, no magic numbers | Gameplay values come from versioned data files (`data/*.json` or `.tres`). Review plus a test that loads every data file against its schema. | C, E |
| R09 | Extensible/testable | Each system ships with tests (R42). Public seams are interfaces. | C, E |
| R10 | Event-driven decoupling | `IEventBus` in Core. No direct cross-system references (architecture test on namespaces). | D |
| R11, R25 | Determinism, reproducible seeds | Architecture test bans non-seeded RNG and wall-clock in Core. Same-seed replay test (two runs → identical state hash). | C, D |
| R12, R38 | Separation of sim/AI/render/net/persistence; UI separate from state | Layered projects. Core cannot reference Godot (architecture test). | D |
| R13–R15 | Modular AI, memory/learning, rule-respecting decisions | AI Bible contract. Unit tests per utility consideration and BT node. Memory persists through save round-trip tests. | C, E |
| R16 | No unrestricted generative AI in hot loops | Banned in Core/Net. Any GenAI dependency needs an ADR plus Steam disclosure review. | E |
| R17, R18 | Async/configurable AI frequency, AI LOD | AI tick rates are set in data. LOD tiers are tested (far entities tick at a reduced rate). | C |
| R19 | Spatial partitioning | Perception and targeting go through `ISpatialIndex`. Review rejects O(n²) scans. | E |
| R20, R21 | Pooling, no allocations in hot loops | Benchmark tests (allocations per tick = 0 for the sim loop) and profiler captures | C, F |
| R22, R23 | 60 FPS on defined hardware, profile first | Target hardware defined in TDD. Perf captures are attached to milestone gates. Optimization PRs need a before/after capture. | F, E |
| R24 | Streaming/chunking/LOD | World chunk system spec in TDD. Validated in the M1 streaming test. | F |
| R26, R52 | Versioned saves, backward compatibility | Every save format has a version. A migration test loads fixtures of every previous version. Breaking changes need an ADR. | C |
| R27, R29, R34 | Data integrity, consistent creature rules, idempotency | Transaction/command IDs. Tests replay duplicate commands → no double effects. | C |
| R28 | Shared versioned gameplay contracts | Contracts live in `Core/Contracts` with a version. Changes need a changelog entry. | E |
| R30–R33 | Server authority, never trust the client, auth/validation, latency and reconnect | Clients send *intents* only. The server validates every intent. Integration tests cover rejected forged intents, packet loss, reconnect and duplicate messages. | C, E |
| R35, R36 | Explicit errors, structured logging, no swallowed exceptions | Analyzer bans empty `catch` (CA1031 + custom rule). `ILogger` with structured fields. | A, E |
| R37, R39 | Data-driven accessible UI, localization | All strings go through translation keys (CSV/PO). Test flags hardcoded UI strings. | C, E |
| R40 | Small focused commits, English messages | Commit message convention. Review rejects mixed-purpose PRs. | E |
| R41 | Reviews for critical systems | `CODEOWNERS`/PR template requires a review for Core/AI/Net/Persistence/security paths. **Solo team: the Producer and QA agents review every PR before merge.** | E |
| R42–R44 | Unit, integration and regression tests | CI test stage. Every bug fix PR links a failing-then-passing regression test. | C, E |
| R45 | CI with format, lint, analysis, tests | `.github/workflows/ci.yml` | A–D |
| R46 | Don't edit tests to hide bugs | Review: any test-assertion change must explain why the *spec* changed. QA sign-off required. | E |
| R47 | No duplicate systems | Before a new manager/service, check `documentation/technical/architecture/system_registry.md`. `ArchitectureRegistryTests` fails if a public Core interface is not registered. | D, E |
| R49, R50 | Document APIs, keep GDD/TDD/AI Bible in sync | XML doc comments required on public APIs (CS1591 as error in Core/Net). PR checklist: "docs updated". | A, E |
| R51 | Check docs before implementing | Task template requires a "docs/dependencies reviewed" line | E |
| R53–R55 | Incremental work, acceptance criteria, Definition of Done | Every task has acceptance criteria. A task is not "Done" until the DoD below passes. | F, E |
| R56 | No merge/deploy/irreversible ops without explicit approval | Agents **never** merge, push, deploy or delete data without the stakeholder's explicit go-ahead in that session | E |
| R57 | Stable core before features | Milestone order: M1 proves the core before content features start | F |

## 3. Definition of Done (applies to every task, per R53–R55)
- [ ] Acceptance criteria in the task are met and demonstrated (evidence: test output, capture, video or screenshot)
- [ ] CI is green: build, format, analyzers, unit, integration and architecture tests
- [ ] New or changed behaviour has tests. Bug fixes include a regression test.
- [ ] No new magic numbers. Values are in data files.
- [ ] Public APIs documented. GDD/TDD/AI Bible updated if behaviour changed.
- [ ] Perf-sensitive change: before/after profiler capture attached
- [ ] Save format change: version bumped plus migration and fixture test
- [ ] Reviewed (Producer + QA agent; plus the stakeholder for Net/Persistence/security)
- [ ] Nothing merged, pushed or deployed without explicit stakeholder approval

## 4. Task assignment template
```
TASK: [ID] [Title]
Agent: [owner]          Milestone: [M#]       Est: [days]
Rules: [R##, R##, ...]
Docs reviewed (R51): [GDD §, TDD §, AI Bible §]
Acceptance criteria:
  1. [measurable]
  2. [measurable]
Evidence required: [tests / capture / playtest note]
```

## 5. Waiver Log
| Date | Rule | Scope | Reason | Approved by | Expires |
|---|---|---|---|---|---|
| — | — | — | — | — | — |
