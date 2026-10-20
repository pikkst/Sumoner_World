# M1-19 scenario B3 "Stream-Run" and spike 4: dev PC

Date: 2026-10-20 · Machine: dev-pc (`machines/dev-pc.json`: i7-8700K, RTX 2060 SUPER, 32 GB; about 5 GB free during the runs, other Docker stacks running) · Build: `cd8835d` + the M1-19b benchmark, Debug C# in the Godot 4.7.2 editor binary · Terrain3D 1.0.2-stable
Command: `Godot --path project-sumoner-worl --resolution 1920x1080 -- --benchmark=B3 --region-chunks=64 --benchmark-out=<dir>` (from `source/`). Raw data: `2026-10-20_B3_dev-pc/` (`summary.json` per run plus every frame over 16.7 ms).

## Setup
- **Region:** the seeded test region. Spike 4 uses the 4 km region (64 × 64 chunks); one more run uses the 512 m region (8 × 8 chunks).
- **Route:** a 19.3 km loop through the corners and across the middle, flown at eye height (2 m above the ground) and looking ahead.
- **Timing:** a 5 s settle while already flying is not measured. Then 150 s at sprint speed (9 m/s, an assumption: 1.5 × the 6 m/s walk, since no sprint speed is designed yet) and 150 s at 3× sprint (27 m/s). 5.4 km are flown in total.
- **Streaming:** 1,820 chunk uploads per 4 km run.
- **Display:** windowed 1920 × 1080 with V-Sync off. The run was windowed, not fullscreen, to avoid taking over the desktop (A4 asks for fullscreen).
- **Streaming hitch:** a frame over 33.3 ms in which, or right after which, a chunk was uploaded.

## Results

| Run | Frames | p50 | p95 | p99 | Max | Frames > 16.7 ms | Frames > 33.3 ms | Streaming hitches | Longest upload | Most chunks waiting |
|---|---|---|---|---|---|---|---|---|---|---|
| 4 km #1 | 112,737 | 2.63 ms | 3.28 ms | 3.56 ms | 9.7 ms | 0 | 0 | 0 | 3.24 ms | 12 |
| 4 km #2 | 113,875 | 2.61 ms | 3.00 ms | 3.55 ms | **90.7 ms** | 53 | **19** | **4** | 0.26 ms | 12 |
| 4 km #3 | 115,463 | 2.60 ms | 3.00 ms | 3.36 ms | 31.2 ms | 8 | 0 | 0 | 2.11 ms | 12 |
| **4 km median** | | **2.61 ms** | **3.00 ms** | **3.55 ms** | 31.2 ms | 8 | **0** | **0** | | |
| 512 m #1 | 111,444 | 2.65 ms | 3.37 ms | 3.73 ms | 15.1 ms | 0 | 0 | 0 | 0.21 ms | 4 |

Godot static memory peaked at 353 MiB (4 km) and 67 MiB (512 m). Run-to-run p99 varies by 6%, so the set is valid by the A4 rule (≤ 10%).

**B3 (0 streaming hitches > 33.3 ms): passed** with a median of 0. **Spike 4 (no frame above 33 ms flying across the 4 km region): passed on the dev PC** with a median of 0. Both results are indicative only, because the dev PC is a minimum-class machine (A5) and the build was not a release export. **Decision: Terrain3D is adopted** (TDD §5.1, §7); the in-house fallback stays only for machines without the extension.

## Run 2: what happened
- **The burst:** from 250.5 s to 251.5 s, frames were erratic, between 10 ms and 90 ms, and 19 of them were over 33 ms. Outside that second, run 2 looks like runs 1 and 3.
- **Why the rule still counted 4 hitches:** during the burst, nothing was waiting to stream and the few chunk uploads took 0.07–0.10 ms each. They happened to sit next to slow frames, and the rule counts any slow frame next to an upload.
- **Earlier test runs:** before the three counted runs, one run had similar clusters of about 30 ms frames. Those disappeared in the next runs with the same settings, both with Terrain3D's dynamic collision on and with it off.
- **Reading:** a stall that cannot be reproduced and has no streaming work in it most likely comes from outside the game. The PC had little free memory and other Docker stacks running.
- **Not changed after the fact:** the hitch rule is unchanged, and run 2 counts as measured.
- **Follow-up for the gate runs (M1-24):** run on a quiet machine; use a release export and fullscreen, as A4 requires; record OS-level activity during the run, so that stalls like this one can be attributed.
