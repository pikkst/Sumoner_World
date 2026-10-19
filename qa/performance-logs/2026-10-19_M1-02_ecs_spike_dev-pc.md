# M1-02 ECS spike: raw BenchmarkDotNet results

Date: 2026-10-19 · Machine: dev-pc (`machines/dev-pc.json`) · Build: Release · Job: 3 warm-up + 10 iterations · Analysis: ADR-002

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
.NET SDK 9.0.313
  [Host]     : .NET 8.0.26 (8.0.2626.16921), X64 RyuJIT AVX2
```

| Method           | Count | Mean         | Error         | StdDev       | Allocated |
|---|---|---|---|---|---|
| Arch_Movement    | 200   |    312.85 ns |     46.981 ns |    31.075 ns |         - |
| Sparse_Movement  | 200   |    455.39 ns |     44.203 ns |    29.238 ns |         - |
| Arch_TierQuery   | 200   |    245.64 ns |      7.622 ns |     5.041 ns |         - |
| Sparse_TierQuery | 200   |    246.45 ns |     17.424 ns |     9.113 ns |         - |
| Arch_TierChurn   | 200   |  1,687.77 ns |    191.556 ns |   113.992 ns |         - |
| Sparse_TierChurn | 200   |     66.37 ns |      4.093 ns |     2.436 ns |         - |
| Arch_Spawn       | 200   |    866.31 ns |     46.515 ns |    27.680 ns |         - |
| Sparse_Spawn     | 200   |    784.29 ns |     74.445 ns |    49.241 ns |         - |
| Arch_Movement    | 10000 | 14,054.10 ns |  1,752.723 ns | 1,159.318 ns |         - |
| Sparse_Movement  | 10000 | 22,307.39 ns |  2,241.156 ns | 1,482.386 ns |         - |
| Arch_TierQuery   | 10000 |  7,680.56 ns |    261.900 ns |   155.853 ns |         - |
| Sparse_TierQuery | 10000 | 12,648.37 ns |    691.146 ns |   361.483 ns |         - |
| Arch_TierChurn   | 10000 | 89,684.17 ns | 10,265.129 ns | 6,108.614 ns |         - |
| Sparse_TierChurn | 10000 |  2,956.19 ns |    376.392 ns |   248.960 ns |         - |
| Arch_Spawn       | 10000 | 46,671.28 ns |  6,902.203 ns | 4,565.380 ns |         - |
| Sparse_Spawn     | 10000 | 39,443.70 ns |  2,739.454 ns | 1,630.205 ns |         - |
