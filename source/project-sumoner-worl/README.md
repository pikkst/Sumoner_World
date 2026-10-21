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
4. For the terrain: from `source/`, run `bash scripts/fetch-terrain3d.sh` (installs the pinned Terrain3D addon, not committed), then open the project once in the editor. Without it, the test region uses a simple fallback terrain.
5. Test region: `Godot --path project-sumoner-worl -- --region` (right mouse to look, WASD/Q/E to fly, Shift = faster; `--region-chunks=64` for 4 km)
6. Emote showcase: `-- --showcase=grazer` or `--showcase=predator` (add `--showcase-distance=12` for a close look)
7. Play against the dev server: start it (Docker, see `docker-compose.dev.yml`), then `Godot --path project-sumoner-worl -- --connect=127.0.0.1:7777` (WASD moves, right mouse turns the camera)
8. Family test package (DEC-003): `GODOT=<Godot console exe> bash scripts/package-family.sh` → `builds/SumonerWorld-family-<commit>.zip` (needs the 4.7.2 .NET export templates; launchers and README in `packaging/family/`)

## Development rules
Every change must pass the Definition of Done in `documentation/production/development_rules_enforcement.md`.
Rules R01–R57 are in `project-config.json`.

## Current Phase
Pre-Production (M0)
