# M1-05 scenario B5 (sim-only): dev PC timings

Date: 2026-10-19 · Machine: dev-pc (`machines/dev-pc.json`) · Build: Release · Seed 0x53570001
Command: `dotnet run -c Release --project tests/SumonerWorld.Benchmarks -- b5 out.csv` (from `source/`)
Scenario: 200 creatures in the 512 m test region; wander (AiDecision), 40 m perception on 1/3 of creatures per tick (Perception), crowd reaction (Behaviour), movement + spatial index update (Movement), one death + spawn every 30 ticks (Ecosystem). 900 warm-up + 9,000 measured ticks.

| Run | Tick p50 | Tick p99 | Tick max | Perception p99 | Movement p99 | Allocated |
|---|---|---|---|---|---|---|
| 1 | 22.3 µs | 97.7 µs | 409 µs | 65.0 µs | 31.5 µs | 0 B |
| 2 | 18.0 µs | 139.2 µs | 1,439 µs | 89.6 µs | 46.3 µs | 0 B |
| 3 | 23.8 µs | 145.8 µs | 2,893 µs | 92.5 µs | 48.9 µs | 0 B |
| **Median** | **22.3 µs** | **139.2 µs** | 1,439 µs | 89.6 µs | 46.3 µs | **0 B** |

Reading:
- Against the 19 ms server tick budget (TDD §2.1) the stand-in simulation uses about 0.1% at p50 and 0.7% at p99.
- **Run-to-run p99 differs by more than 10%**, so by the M0-07 A4 rule this set is not valid gate evidence. At microsecond scale, Windows scheduling and timer jitter dominate (the max values are single OS preemptions; nothing allocates, so they are not GC). Gate runs (M1-24) use the real AI and will be in the millisecond range, where this noise is small.
- These numbers cover only the stand-in systems. Real creature cognition (Phase B) is added to B5 as it is built, so the gates and timings keep covering it.
