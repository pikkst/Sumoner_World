# ADR-001: Engine, Language and Top-Level Architecture
Status: **Accepted** (Producer, 2026-10-19). Engine chosen by the stakeholder; language and layering chosen by the Producer to satisfy the development rules.
Rules addressed: R03, R04, R05, R07, R09, R11, R12, R30, R42, R45

## Context
- The stakeholder selected **Godot 4**. The current stable version is **4.7.2** (.NET build available).
- The development rules require strong typing (R05), DI and interfaces (R07), ECS for hot paths (R03), deterministic simulation (R11), strict separation of simulation, AI, rendering, networking and persistence (R12), server-authoritative multiplayer (R30), and unit and integration tests in CI (R42, R43, R45).
- `engine_configs/godot_config.json` assumes GDScript and snake_case. That file is framework-generic and predates this project.

## Decision
1. **Engine: Godot 4.7.2-stable, .NET edition.** The version is pinned. Upgrades need a new ADR (R52).
2. **Language: C# (target `net8.0`, which the installed SDK 9 can build).** GDScript is allowed only for editor tooling and trivial scene glue, with static typing enforced (`untyped_declaration=error`).
   - Why: GDScript has no interfaces (R07), weaker static analysis (R45), and no mainstream unit-test or DI ecosystem (R42). C# provides all three, plus `Nullable`, Roslyn analyzers and `dotnet format`.
3. **Layered solution.** Dependencies point downward only:
   ```
   SumonerWorld.Game        (Godot project: scenes, rendering, input, UI presentation)  -> depends on Core, Net, Persistence
   SumonerWorld.Net         (transport, replication, server authority; ENet/Godot HL multiplayer adapters)
   SumonerWorld.Persistence (versioned save formats + migrations)
   SumonerWorld.Core        (pure C#: simulation, ECS, creature AI, rules, contracts) - NO Godot reference
   ```
   `SumonerWorld.Core` must not reference `GodotSharp`. An architecture test enforces this. This makes the simulation deterministic, headless-server-ready and unit-testable (R11, R12, R30).
4. **ECS.** A lightweight data-oriented ECS inside `Core` handles creatures, projectiles and perception (R03, R18–R21). Godot nodes are views that read simulation state. They never own gameplay state (R12, R38).
5. **Determinism.** Fixed-tick simulation, seeded `IRandom` (no `System.Random` or `GD.Randf` in Core), and no wall-clock reads in Core. Float use is reviewed in lockstep-sensitive code (R11, R25).
6. **Naming.** These C# conventions replace the GDScript snake_case conventions for code: PascalCase types and members, `I`-prefixed interfaces, `_camelCase` private fields, and file name = type name. Scenes stay PascalCase `.tscn`. Resources stay snake_case `.tres` (R04).

## Consequences
- (+) Every rule has an automated or checkable enforcement path (see `documentation/production/development_rules_enforcement.md`).
- (+) A headless dedicated server can run `Core` + `Net` without rendering.
- (−) C# builds of Godot cannot export to Web. That is acceptable because the target platform is PC.
- (−) Base agent docs mention GDScript and Godot 4.4.1. **This ADR overrides them** for this project.

## Rule conflicts flagged
- **Godot config vs R04/R05:** resolved here in favour of C#.
- **R57 vs stakeholder scope decision:** R57 prioritises a stable core over feature complexity. The milestone plan resolves this by sequencing. Core simulation and the networking foundation are proven at M1 before content features start.
