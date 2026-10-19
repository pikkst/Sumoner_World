# M0-08: Legal & Naming Check (preliminary)
Date: 2026-10-19 · Author: Producer Agent (with Market Analyst) · Status: **Preliminary research done; formal clearance is a stakeholder action**
Rules: R56 · Related: GDD v0.1 §1 (Pacts), AI Bible v0.1 §10, market_overview.md (Legal and IP)

> **This is not legal advice.** It is desk research from public sources, done so the stakeholder can decide what to pay a professional for and so the design avoids obvious risks now, while changes are cheap. Two items below need a qualified trademark attorney and a patent attorney before the Steam page goes live (M2).

---

## 1. Title: "Sumoner World"

### Findings (web and Justia US trademark search, 2026-10-19)
| Mark | Owner | Field | Relevance |
|---|---|---|---|
| "Sumoner World" (exact) | — | — | **No game, company or trademark found** with this exact name. |
| "Summoner World" / "Summoners World" | — | — | No trademark filing found. |
| **SUMMONERS WAR** (+ Sky Arena, Chronicles, Lost Centuria, Rush) | Com2uS | computer game software (class 9/41) | **Main conflict risk.** Large active franchise (> $1B revenue), with recent filings (Rush, April 2025). Its esports event is called "Summoners War **World** Arena Championship". |
| SUMMONER | owner not shown | software games | Plain word; the 2000 THQ/Volition game *Summoner* also exists. Weak, descriptive word, but check status. |
| SUMMONER'S LEGION, DRAGON SUMMONER | various | online games, gaming machines | "Summoner" is a crowded term, so it is weak on its own; distinctiveness has to come from the whole name. |

### Assessment
- **Risk: low–medium.** "Sumoner World" and "Summoners War" share the structure *Summoner(s) W…* in the same goods class. The spelling, the different second word and the different genre (premium PC survival RPG vs. free-to-play mobile) help, but a large rights holder could still object at registration or on the Steam store.
- **Practical risk:** "Sumoner" reads as a typo of "Summoner". Search engines and Steam search will autocorrect it, so players looking for the game may land on Summoners War. That is a discoverability cost even without any legal issue.

### Recommended actions
1. **Stakeholder:** pay for a formal **clearance search** (USPTO, EUIPO, WIPO Global Brand Database, plus common-law/Steam/app-store names) by a trademark attorney. Rough cost: a few hundred to about €1,500.
2. If clear: file a trademark in **EU (EUIPO) and US**, classes 9 and 41, **before the Steam page announcement (M2)**. Register the domain and social handles now (cheap, prevents squatting).
3. If the attorney flags Summoners War, the fallback is a distinct subtitle-led name (e.g. *Sumoner World: The Broken Pacts*) or a new title. Decide before M2; renaming after the Steam page is expensive.

---

## 2. Patent exposure: creature capture and summoning

### Findings
| Patent | Covers (summary of claims) | Status (public reports) |
|---|---|---|
| **JP7545191** (Nintendo / The Pokémon Company) | Selecting between *capture items* and *combat characters*, **aiming** with directional input, and **throwing/launching** the selection toward the crosshair; a hit capture item triggers a capture check; a launched combat character starts combat | Asserted against Pocketpair in Tokyo District Court (filed 2024-09-18). Pending; court's preliminary views reportedly due **2026-11-09** |
| **JP7493117** | An indicator of **capture success likelihood** shown while aiming | Same case |
| **JP7528390** / **US 12,409,387** | **Riding** an owned character as a mount, and **switching to an aerial mount while in the air** | Same case (JP). Palworld moved gliding to an item in v0.5.5 to avoid it |
| **US 12,403,397** ("summon sub-character and fight in one of two modes") | Input makes a sub-character **appear on the field**; if an enemy is there, a **player-input-driven battle** runs; otherwise it moves automatically; a second input sends it in a direction, and if an enemy is at that spot an **automatic battle** runs | Granted 2025-09-02. USPTO Director ordered ex parte reexamination 2025-11-03; examiner issued a **non-final rejection of all 26 claims** (~2026-03-25). **Not final**: the patent is still in force until a final decision |

Note: earlier project notes said the USPTO "revoked" these patents in April 2026. That is inaccurate: it was a **non-final** rejection in reexamination. Corrected in `market_overview.md`.

### Our design vs. the claims
| Claim element | Sumoner World (GDD v0.1, AI Bible v0.1) | Risk |
|---|---|---|
| Aim and throw a capture item | **No capture, no thrown item.** A creature is acquired by a Pact it accepts or refuses (§1 Pacts) | Low |
| Capture success check on hit | Pact acceptance is a deterministic utility decision about trust and terms, not a hit check | Low |
| Capture-likelihood indicator while aiming | None planned. **Guardrail below** (no "acceptance chance" meter while targeting) | Low if the guardrail holds |
| Launch a combat character toward the crosshair | Summoning is a **sigil cast; the creature appears beside the player** (GDD §1) | Low |
| Two battle modes (player-input battle vs. automatic battle) depending on where the summon lands | **One continuous real-time combat mode.** Creatures always act from their own AI, shaped by commands and bond; there is no separate battle mode or battle scene | Low–medium: commanding a creature at an enemy resembles the "second input → automatic battle" step, so the single-mode rule must hold |
| Ride an owned creature; switch to an aerial mount mid-air | Mounts are **not in the GDD yet** | Becomes medium if mounts are added without the guardrail |

### Design guardrails (binding for design and code from now on)
1. **No thrown or launched object** is part of acquiring, summoning or releasing a creature. Summons appear at the player's side or at the Sanctum.
2. **No capture or acceptance probability** is shown to the player while targeting a creature. The Bond Lens may show mood and trust, never a success percentage.
3. **One combat mode.** No separate battle state, scene or "battle start" triggered by a summon reaching an enemy. Commands are given to creatures already present.
4. **Mounts** (if added): need a design review against US 12,409,387 / JP7528390 first. Start with ground mounts only; no mid-air mount switching.
5. Any new creature-acquisition, summoning or mount mechanic requires a short note in its PR checking it against this table (Producer review).

### Recommended actions
1. **Producer:** re-check the Tokyo case after **2026-11-09** and the US 12,403,397 reexamination outcome; update this report (next update: 2026-12-01).
2. **Stakeholder:** a one-off **freedom-to-operate review** by a games patent attorney on the GDD summoning/command design **before the vertical slice (M2)**. It is cheaper to do it on the design document than on a shipped game.

---

## 3. Other checks
| Item | Finding | Action |
|---|---|---|
| Creature designs | Folklore/myth-inspired, not cute mascots (DEC-002) | Art review in M0-09 style guide: no Pokémon-like silhouettes or ball-shaped motifs |
| Generative AI disclosure (Steam) | No generative AI in shipped assets (DEC-002) | Steam content survey: answer "no" for shipped content; keep the internal disclosure log |
| Loot boxes (EU/UK) | Banned by DEC-002 | None |
| Player data (GDPR) | Playtest telemetry is anonymous and deleted after 90 days (M0-07) | Privacy notice before public playtests |

## 4. M0-08 status
- [x] Title spelling decided ("Sumoner World", 2026-10-19)
- [x] Preliminary trademark search (this report, §1)
- [x] Capture/summoning design reviewed against the asserted patents; guardrails written into the GDD (§2)
- [ ] **Stakeholder:** formal trademark clearance and filing (before M2)
- [ ] **Stakeholder:** patent freedom-to-operate review (before M2)

## Sources
- [Justia: SUMMONERS WAR trademarks (Com2uS)](https://trademarks.justia.com/owners/com2us-corporation-4158313), [SUMMONERS WAR: RUSH](https://trademarks.justia.com/991/23/summoners-war-99123761.html), [SUMMONER](https://trademarks.justia.com/764/71/summoner-76471893.html), [SUMMONER'S LEGION](https://trademark.justia.com/865/74/summoner-s-86574827.html)
- [Pocket Gamer.biz: Summoners War $1B lifetime revenue](https://www.pocketgamer.biz/summoners-war-1-billion-lifetime-revenues)
- [Evorix (Japanese patent attorney): Nintendo/Pokémon vs. Pocket Pair](https://www.evorix.jp/en/blog/nintendo-palworld-patent-lawsuit-benrishi)
- [PC Gamer: Is Nintendo suing Palworld over throwing Poké Balls?](https://www.pcgamer.com/games/survival-crafting/is-nintendo-suing-the-palworld-devs-over-a-patent-on-throwing-poke-balls/)
- [Lawfold: Nintendo Palworld lawsuit update (July 2026)](https://lawfold.com/nintendo-palworld-lawsuit-update/)
- [Google Patents: US 12,403,397 B2](https://patents.google.com/patent/US12403397B2/en)
- [Games Fray: USPTO examiner rejects US 12,403,397 (non-final)](https://gamesfray.com/u-s-patent-examiner-rejects-nintendos-summon-subcharacter-and-let-it-fight-in-1-of-2-modes-patent-as-obvious-non-final-ruling/), [reexamination order](https://gamesfray.com/huge-blow-for-nintendo-head-of-u-s-patent-office-takes-rare-step-to-order-reexamination-of-summon-subcharacter-and-let-it-fight-in-1-of-2-modes-patent/)
- [Nintendo Everything: summoning patent update](https://nintendoeverything.com/nintendo-summon-subcharacters-battle-patent-update/)
