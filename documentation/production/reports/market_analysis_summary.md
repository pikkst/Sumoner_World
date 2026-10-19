# Market Analysis Summary: Sumoner World
Generated: 2026-10-19
From: Market Analyst Agent → Producer Agent

## Verdict: PIVOT (narrow scope), then GO
**Confidence**: Medium · **Opportunity score**: 6/10

Full reports: `resources/market-research/market_overview.md`, `competitor_palworld.md`, `competitor_ark.md`, `competitor_black_desert_online.md`

## Five things the Producer needs to know
1. **The market is real but dominated.** Palworld has sold about 30.5M copies and made about $700M gross. Its 1.0 shipped in July 2026. Over 500 creature-collector games come out on Steam every year, and only about 6% reach 1,000 reviews (about 27% at $20+). A thin or derivative game will sink.
2. **The USP is the right bet.** None of the three competitors has creatures with memory or personality. "Every creature remembers you" is a gap we can win. It must be **visible within the first 10 minutes** of play.
3. **The brief contradicts itself.** "Casual audience + Medium timeline" doesn't fit "immersive open world + magic + crafting + combat + AI ecosystem + server-authoritative multiplayer economy". The competitor set (two survival games and one MMO) is mid-core and hardcore.
4. **Don't call it "AI".** About 31% of engaged Steam players react badly to AI disclosures. Market it as "living ecosystem / creatures that remember". The project rules already ban unrestricted GenAI in gameplay loops, so this wording is accurate.
5. **Legal: avoid throw-to-capture.** Nintendo v. Pocketpair (Tokyo, ruling expected November 2026) targets aim-and-throw capture mechanics. Use summoning contracts or rituals instead, which also fits the "Summoner" fantasy better.

## Decisions requested from the Producer (blocking design)
| # | Decision | Recommendation |
|---|----------|----------------|
| 1 | Target audience | "Accessible mid-core" (optional survival, forgiving death), or commit fully to cosy-casual |
| 2 | Multiplayer scope for v1 | Single-player + 1–4 player host co-op. **No MMO, trading or server economy.** Update the development rules to match. |
| 3 | World scope | One dense region (about 4–8 km²), expanded per early access update |
| 4 | Business model | Premium early access at $24.99 → 1.0 at $29.99 → paid expansions. Update the GDD (currently "Not Applicable"). |
| 5 | Engine | Choose in the next phase. Unreal or Unity are lower-risk for open-world streaming. |
| 6 | Title | ~~Fix the spelling~~ **Decided 2026-10-19: "Sumoner World"**. Trademark search still pending. |
| 7 | Competitor set | Replace Black Desert Online with Monster Hunter Stories / Enshrouded as direct comparisons |

## Immediate action items
1. **Sr Game Designer**: Design pillars doc. Pillar #1 = creature memory and bonding. Postpone everything that doesn't support it.
2. **Mechanics Developer**: 3-month creature behaviour prototype (behaviour tree / utility AI + persistent memory) to show the USP reads clearly to players.
3. **Producer**: Legal review of summoning/capture mechanics. Steam page live about 12 months before early access. Plan a Steam Next Fest demo.
4. **Data Scientist**: Set wishlist targets. Benchmark: 100K+ before EA launch.

## Projections (first year, gross, est.)
| Scenario | Units | Revenue |
|----------|-------|---------|
| Conservative (P90) | 40K | $0.8M |
| Realistic (P50) | 100K | $2.0M |
| Optimistic (P10) | 400K | $8.0M |

## Launch window
Early access **late April–May 2028** or **late August–September 2028** (assuming about 18–24 months to EA). Avoid Q4 and weeks with major Palworld or ARK updates. Re-check about 6 months before.

## Key risks
| Risk | P | I | Owner |
|------|---|---|-------|
| Scope vs. timeline | H | H | Producer |
| Seen as a "Palworld clone" | H | H | Sr Game Designer / Market Analyst |
| USP not readable in demo | M | H | Mechanics Developer / Game Feel Developer |
| AI backlash | M | M | Market Analyst |
| Patent / IP | L–M | M | Producer |

**Next market update**: 2026-12-01 (after the Nintendo v. Pocketpair ruling)
