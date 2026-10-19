# M1-03 spatial grid: perception pass benchmark

Date: 2026-10-19 · Machine: dev-pc · Build: Release · Job: 3 warm-up + 10 iterations

Scenario: every creature moves 0.2 m, then runs a 40 m radius query and a 60 m / 60° cone query; creatures spread uniformly over the 512 m M1 test region; 16 m cells.

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
.NET SDK 9.0.313
  [Host]     : .NET 8.0.26 (8.0.2626.16921), X64 RyuJIT AVX2
```

| Method         | Count | Mean       | Error     | StdDev    | Allocated |
|---|---|---|---|---|---|
| PerceptionPass | 200   |   112.2 us |   5.55 us |   3.30 us |         - |
| PerceptionPass | 2000  | 4,704.6 us | 255.56 us | 152.08 us |       3 B |

Reading: 200 creatures = 0.11 ms for a full pass, i.e. 2.8% of the 4 ms AI budget even if every creature perceived every tick (T0 perceives at 10 Hz, every 3rd tick). 2,000 creatures in the same area are 10x denser, so each query returns ~10x more and the pass costs 4.7 ms; AI LOD tiers keep real loads far below this. The 3 B at 2,000 is BenchmarkDotNet amortisation noise (smaller than any object); the 0-allocation unit test covers the steady state.
