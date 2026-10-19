# M1-02b production ECS: raw BenchmarkDotNet results

Date: 2026-10-19 · Machine: dev-pc (`machines/dev-pc.json`) · Build: Release · Job: 3 warm-up + 10 iterations · Analysis: ADR-002 "Implementation results"

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.9711)
.NET SDK 9.0.313
  [Host]     : .NET 8.0.26 (8.0.2626.16921), X64 RyuJIT AVX2
```

| Method             | Count | Mean        | Error       | StdDev      | Allocated |
|---|---|---|---|---|---|
| Core_Movement      | 200   |  1,555.9 ns |   153.03 ns |   101.22 ns |         - |
| Core_MovementDense | 200   |    656.6 ns |    33.75 ns |    20.09 ns |         - |
| Core_TierQuery     | 200   |  1,170.7 ns |   123.65 ns |    81.79 ns |         - |
| Core_TierChurn     | 200   |    301.6 ns |    17.63 ns |    10.49 ns |         - |
| Core_Spawn         | 200   |    775.4 ns |    57.11 ns |    33.98 ns |         - |
| Core_Movement      | 10000 | 59,668.5 ns | 5,287.58 ns | 3,497.40 ns |         - |
| Core_MovementDense | 10000 | 35,343.5 ns | 4,449.78 ns | 2,943.25 ns |         - |
| Core_TierQuery     | 10000 | 60,501.6 ns | 7,920.82 ns | 5,239.13 ns |         - |
| Core_TierChurn     | 10000 | 16,258.4 ns | 1,484.03 ns |   981.60 ns |         - |
| Core_Spawn         | 10000 | 55,274.2 ns | 2,999.10 ns | 1,784.72 ns |         - |
