# ADR-002: ECS Implementation (Arch vs. in-house sparse set)
Status: **Accepted** (stakeholder, 2026-10-19: "build the small in-house version instead of using Arch"). Supersedes the TDD's first choice.
Rules: R03, R11, R21, R23, R47, R48 · Related: TDD v0.1 §2.2 and §7, ADR-001, system registry (`IEcsWorld`, `IStableIdAllocator`)

## Context
TDD v0.1 named **Arch** (C# archetype ECS) as the preferred ECS, behind an `IEcsWorld` facade, pending an M1 spike, with an **in-house sparse-set ECS** as the fallback. Rule R23 requires measuring before choosing for performance.

The spike (`source/tests/SumonerWorld.Benchmarks/EcsSpike/`) ran the four operations a tick does most, on identical data in both ECSs:
- **Movement:** iterate Position + Velocity
- **TierQuery:** iterate three components and filter on the AI tier
- **TierChurn:** 10% of entities add or remove a tag each tick (creatures changing AI LOD tier, a structural change)
- **Spawn:** 5% of entities destroyed and re-created each tick (deaths, spawns, chunk loads)

## Results
BenchmarkDotNet 0.14.0, .NET 8.0.26, Release, dev PC (i7-8700K, `qa/performance-logs/machines/dev-pc.json`), 3 warm-up + 10 measured iterations. Mean time per tick:

| Operation | 200 entities: Arch | 200: in-house | 10,000 entities: Arch | 10,000: in-house | Allocated |
|---|---|---|---|---|---|
| Movement | 313 ns | 455 ns | 14.1 µs | 22.3 µs | 0 B (both) |
| TierQuery | 246 ns | 246 ns | 7.7 µs | 12.6 µs | 0 B (both) |
| TierChurn | 1.69 µs | 66 ns | 89.7 µs | **3.0 µs** | 0 B (both) |
| Spawn | 866 ns | 784 ns | 46.7 µs | 39.4 µs | 0 B (both) |

Raw table: `qa/performance-logs/2026-10-19_M1-02_ecs_spike_dev-pc.md`.

**Reading the numbers:**
- At the **M1 target (200 creatures)** every operation costs **under 2 µs per tick** in both ECSs. The server tick budget is 19 ms (TDD §2.1), so the ECS choice is worth less than 0.01% of it.
- Even at **10,000 entities**, the worst case is 90 µs (Arch tier churn), still below 0.5% of the budget.
- Arch iterates about **1.6× faster** (archetype memory layout). The in-house set changes structure **25–30× faster**, and AI tier changes happen every few ticks for many creatures (AI Bible §9).
- Neither allocates in steady state.

## Other factors
| Factor | Arch 2.1.0 | In-house sparse set |
|---|---|---|
| Licence | Apache-2.0 (TDD said MIT; corrected) | ours |
| Dependencies | 6 packages: Arch.LowLevel, Collections.Pooled **2.0.0-preview.27** (a 2020-era preview), CommunityToolkit.HighPerformance, Microsoft.Extensions.ObjectPool, System.Runtime.CompilerServices.Unsafe, ZeroAllocJobScheduler | none |
| Maintenance | last release 2025-05-27 (17 months ago) | ours to maintain (~300 lines planned) |
| Global state (R48) | worlds are registered in a static world table | plain instance, injected (R07) |
| Core layering test | needs an allowlist for 6 assemblies | unchanged: Core references only the BCL |
| Determinism (R11) | deterministic for the same operation order, but iteration order is internal to the library | fully specified by us: dense order, swap-remove rule, slot reuse rule |
| Saves and `StableId` (TDD §2.2) | needs a mapping layer either way | same mapping layer, designed together |
| Features we would not use | multithreaded queries, source generators, events, relationships | n/a |

## Decision
**Use an in-house sparse-set ECS in `SumonerWorld.Core.Ecs`**, behind `IEcsWorld`, and drop Arch.
- Performance is not a differentiator at our scale, and the in-house set is better at the structural changes our AI LOD causes most often.
- It keeps Core free of third-party dependencies (including a preview package), avoids static global state, and gives us full control over iteration order for determinism and replays.

### Scope limits (to stop the ECS growing into a project of its own)
The in-house ECS supports only:
1. Entities: 32-bit index + 32-bit generation; freed slots reused in LIFO order (deterministic).
2. One pool per component type: dense array + sparse index, swap-remove.
3. Views over 1–4 component types that iterate the **smallest** pool and check the others.
4. Fixed capacity set at world creation (from server config); exceeding it is an error, never a silent resize in the tick.
5. No multithreading inside the ECS, no reflection-based serialization, no events (the `EventBus` already does that).

Anything beyond this list needs a new ADR.

## Consequences
- (+) Zero dependencies in Core; the layering test stays strict.
- (+) Faster structural changes; deterministic order we control.
- (−) We own and test the ECS code (~300 lines plus tests). Mitigation: the scope limits above, and property tests in M1-02b.
- (−) Iteration is ~1.6× slower than Arch. If the M1 benchmark (B1/B5) ever shows the ECS above 0.5 ms/tick, revisit with a profile (R23), not by default.
- The TDD (§2.2, §7) is updated to match this ADR.

## Next step (M1-02b, after approval)
Implement `IEcsWorld`, `IStableIdAllocator` and the views in `SumonerWorld.Core.Ecs` with unit and property tests (no allocations per tick, generation checks, swap-remove correctness, deterministic slot reuse). The spike code stays in the benchmark project for re-measurement; Arch is referenced only there.

## Reproduce
```
cd source
dotnet run -c Release --project tests/SumonerWorld.Benchmarks -- --filter "*EcsSpike*"
```
