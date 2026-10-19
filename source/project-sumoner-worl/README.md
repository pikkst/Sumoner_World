# Sumoner World - Source Code

## Engine: Godot 4.7.2-stable (.NET) · Language: C# (net8.0)
See `documentation/technical/architecture/ADR-001-engine-language-architecture.md`.

## Solution layout (`source/`)
- `SumonerWorld.sln`: the whole solution
- `src/SumonerWorld.Core`: pure C# simulation, ECS, creature AI, contracts. **No Godot reference** (enforced by tests).
- `tests/SumonerWorld.Tests`: unit, integration and architecture tests
- `project-sumoner-worl/`: the Godot project (this folder): scenes, assets, presentation-only C# scripts

## Setup
1. Install **Godot 4.7.2-stable .NET** and .NET SDK 8+ (SDK 9.0.313 is pinned in `global.json`)
2. Open `project-sumoner-worl/project.godot` in Godot
3. From `source/`, run `sh scripts/verify.sh` (format, analyzers, tests)

## Development rules
Every change must pass the Definition of Done in `documentation/production/development_rules_enforcement.md`.
Rules R01–R57 are in `project-config.json`.

## Current Phase
Pre-Production (M0)
