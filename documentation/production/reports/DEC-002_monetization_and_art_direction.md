# DEC-002: Monetization Model & Art Direction
Date: 2026-10-19 · Author: Producer Agent (with Market Analyst, Sr Game Artist, Technical Artist)
Status: **Adopted.** The stakeholder delegated the choice ("find the best options"). The stakeholder can override at any time.
Constraints applied: full multiplayer with trading/economy, hardcore audience, solo developer, Godot 4.7.2, PC, 60 FPS target (R22), data integrity (R27), USP = creatures that remember.

---

## Part A: Monetization

### Options evaluated
| Option | Fit | Why |
|---|---|---|
| **A1. Premium early access → 1.0 + paid expansions + cosmetic supporter packs** | ✅ **Best** | Standard for the genre (Palworld, ARK, Valheim, Enshrouded). No live-ops treadmill for a solo dev. Hardcore players accept it. Revenue arrives up front, when the money is needed. |
| A2. Free-to-play + cosmetic shop / battle pass (Once Human model) | ❌ | Needs constant content, an anti-fraud team and paid servers from day one. F2P in a game with player trading attracts bots and RMT (real-money trading), which puts R27/R31 at risk. Not feasible solo. |
| A3. Buy-to-play + cash shop (Black Desert model) | ❌ | Hardcore PC players review-bomb paid convenience items. Any purchasable power or convenience item breaks the player economy. |
| A4. Subscription / paid official servers | ❌ | Recurring infrastructure cost and liability. Players won't pay a subscription for an unknown IP. |

### Decision: A1, with these rules
1. **Price:** $24.99 in early access → $29.99 at 1.0. Use Steam regional pricing. The first discount is ≤ 20% and not before 3 months after launch. (ARK's discount data shows a strong sale response, so discounts are a lever to use later.)
2. **Servers:** **player-hosted dedicated servers + host-your-own co-op** (free server binary, as with ARK/Palworld/Valheim). There are no official servers at launch, so the stakeholder carries no server cost. This answers the open "multiplayer topology" question provisionally. Confirm at the M1 gate.
3. **Post-launch revenue:** paid **region expansions** ($9.99–14.99 each: new biome, creatures, story arc) after 1.0. Optional **cosmetic supporter packs** (outfits, sanctum decor, sigil effects).
4. **Hard bans (economy integrity, R27, R31):**
   - No item for sale that affects stats, progression speed, creature acquisition or bonding
   - **Cosmetics are account-bound and can never be traded** in the player economy. This prevents an RMT bridge.
   - No loot boxes or randomised paid items (EU/UK regulation risk and review-bomb risk)
   - No paid creatures. Creatures are always earned in the game. (This protects the USP: a bought creature has no shared history with you.)
5. **Steam Workshop / mods:** free, after 1.0 (long-tail retention, as with ARK).

### Revenue impact
The market projections stay as they are (P50: about 100K units / $2.0M gross in year one). Expansions add about 15–25% of base revenue in years two and three *(est.)*.

---

## Part B: Art Direction

### Options evaluated (solo developer, Godot, open world, many creatures, 60 FPS)
| Option | Asset cost (solo) | Godot fit | Market fit (hardcore) | Verdict |
|---|---|---|---|---|
| Realistic / photoreal PBR | ❌ Very high | ⚠️ Weak at open-world scale | ✅ | ❌ Not feasible solo |
| Cute / anime creature style | ⚠️ Medium | ✅ | ❌ Reads as a "Palworld/Pokémon clone" and invites IP comparison | ❌ |
| Stylized painterly 3D (Enshrouded / Genshin-lite) | ⚠️ Medium–high | ✅ | ✅ | Second choice |
| **Atmospheric low-poly + modern lighting (Valheim lineage)** | ✅ **Low** | ✅ **Strong** (Forward+, volumetric fog, SDFGI/VoxelGI, glow) | ✅ Proven with hardcore survival players (Valheim: a 5-person team, a hit with hardcore survival players) | ✅ **Best** |

### Decision: "Mythic Low-Poly": atmospheric low-poly dark fantasy
**Look:**
- **Geometry:** low-poly models with clear silhouettes. Flat-to-light texturing (small palette textures, little or no photographic detail). This is cheap for one person to make, easy to LOD and fast to render.
- **Lighting does the beauty work:** volumetric fog, god rays, day/night and weather, glow on magic. Light gives the world its emotion (Valheim's lesson).
- **Tone:** mythic, mysterious, a little dark and wonder-filled. **Not cute.** Fits the hardcore audience and separates us from Palworld and Pokémon.
- **Creatures:** inspired by folklore and myth (not Pokémon-style mascots). **Each has a readable silhouette at 50 m** and a distinct colour language per ecological role (predator / grazer / spirit / scavenger).
- **Magic:** the brightest, most saturated elements in any scene. Each school has a colour signature; summoning sigils glow.

**USP-driven rule (most important):** creature memory and emotions must be **readable from body language**, not UI. That means expressive animation (ears, posture, tail, distance kept, gaze), a small set of readable "emotes" (trust, fear, grudge, curiosity, affection), and optional subtle UI. Animation budget beats texture budget.

### Production rules (Technical Artist)
- **Budgets** (to be confirmed in TDD v0.1, M0-05):
  - creatures 1.5–4K tris for LOD0, at least 3 LODs
  - environment kits modular and snapped to a grid
  - textures 256–512 px palette atlases
- **Shaders:** a shared, small shader set (stylized terrain, foliage wind, creature rim light, magic emissive). No per-asset custom shaders (R47).
- **No generative-AI art in shipped assets** (market finding: about 31% of engaged Steam players react negatively). Concept exploration is allowed but must be disclosed internally.
- **Tools:** Blender → glTF 2.0 → Godot import presets (versioned). Rigs and animation clips are shared per creature body type (quadruped, avian, serpentine, biped) to cut animation cost.

### Next step
M0-09 (Sr Game Artist + Technical Artist): style guide v0.1 with mood boards, a palette, a creature silhouette sheet, the LOD/poly budget table and a lighting test scene in Godot.
Delivered 2026-10-19: `documentation/art/style-guides/style_guide.md` v0.1 (mood boards still to be collected by hand).
