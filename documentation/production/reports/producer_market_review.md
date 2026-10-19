# PRODUCER: MARKET ANALYSIS REVIEW
Date: 2026-10-19 · Project: Sumoner World · Phase: Market Analysis → **Pre-Production**

## Reports read
- [x] Market Overview: `resources/market-research/market_overview.md`
- [x] Competitor Analyses: `competitor_palworld.md`, `competitor_ark.md`, `competitor_black_desert_online.md`
- [x] Executive Summary: `documentation/production/reports/market_analysis_summary.md`

**Market Verdict**: PIVOT · **Confidence**: Medium

### Key market insights
1. Proven demand, dominated by Palworld (about 30.5M units). Feature parity is not a winning strategy.
2. Clear opportunity: creatures with memory and personality (our USP), which none of the competitors has.
3. Main risk: scope vs. resources.

## Stakeholder decisions (2026-10-19)
| Topic | Market recommendation | Stakeholder decision | Status |
|---|---|---|---|
| Scope | Solo + 1–4 co-op, no server economy | **Keep original scope** (full multiplayer, server-authoritative economy and trading) | Risk accepted |
| Audience | Accessible mid-core | **Core / hardcore** | Risk accepted |
| Engine | Unreal Engine 5 | **Godot 4** (pinned to 4.7.2 .NET, see ADR-001) | Risk accepted |
| Team | ~8–12 people | **Solo + agents** | Risk accepted |

**PROCEEDING WITH: Original plan, with risk sequencing.**

## Producer escalation (recorded once, per risk matrix)
The combination of original multiplayer scope, a hardcore audience, a solo developer and Godot is rated **Critical (High probability / High impact)**. The protocol response would be "stop work". Because the stakeholder has explicitly accepted the risk, the Producer is proceeding under these controls:
1. **M1 is a risk burn-down milestone, not a content milestone.** It proves the four hardest unknowns: creature AI with memory at scale, Godot world streaming, a deterministic headless simulation, and a server-authoritative loop.
2. **There is a formal GO / RE-SCOPE gate at the end of M1 (2027-03-31)** with measurable criteria (see `project-config.json` → milestones). If the criteria fail, the Producer will bring the scope options back to the stakeholder.
3. **The architecture is server-authoritative from day one** (ADR-001), so keeping full multiplayer later costs no rewrite.
4. **Timeline honesty:** the milestone dates assume full-time solo work with heavy agent assistance. The EA date (Aug–Sep 2028) is **at risk** and will be re-forecast at each gate.

## Market-based adjustments applied
- **Features:** creature memory/bonding is Core Feature #1 and is protected from cuts (Best Practice #7).
- **Mechanics:** no aim-and-throw capture. Summoning contracts/rituals instead (patent risk).
- **Messaging:** "living ecosystem", not "AI-powered".
- **Performance:** a 60 FPS mid-range target is a differentiator against ARK.

## Still-open stakeholder decisions
1. Monetization model (GDD says "Not Applicable"; the market recommends premium EA)
2. Art direction / visual style
3. ~~Title spelling~~ Decided 2026-10-19: **Sumoner World**. Trademark search still pending.
4. Multiplayer topology (player-hosted dedicated vs official servers). Decide at the M1 gate.
