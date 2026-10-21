# DEC-003: Private family testing, no public playtest or benchmarks for now
Date: 2026-10-21 · Author: Producer Agent · Status: **Adopted (stakeholder decision, 2026-10-21)**

## Decision (stakeholder's words, summarised)
1. **The game stays private.** It is opened to anyone else only if the creator and the creator's family like it.
2. **Organiser and owner:** **Eventnexus OÜ** owns the game and organises all test sessions.
3. **Testers:** the creator (admin user) and the creator's family: children aged 4–14 (boys and girls) and their parents,
   aged 36–37. There is no outside recruiting, and testers are not paid.
4. **Environment:** testing happens only through the local Docker dev environment on the family's own PC
   (`source/docker-compose.dev.yml`, `documentation/technical/dev_tools/README.md`). A packaged client build may be made for the
   testers, so they need no Godot editor.
5. **No demo test yet.** The build is too early (one simple creature model, a basic stand-in character, a few interactions), so
   no structured test session is run now.
6. **Benchmarks are not needed now.** M1-24 (gate benchmarks on the recommended-spec PC) is skipped.
7. Consent and questionnaire materials are written **in English**, following good practice.

## What changes
| Before | Now |
|---|---|
| USP playtest with ≥ 20 core-audience adults (QA plan Part B), pilot Feb 2027, gate run Mar 2027 | **Deferred.** Informal family sessions when the creator decides the build is ready. The pilot kit stays in `qa/playtesting/pilot_kit/` for a later public test |
| M1 gate measures 60 FPS on the recommended spec (QA plan A3) | **Skipped for now.** The dev PC benchmark logs and the CI B2/B5 checks continue |
| Recruitment, payment, tester hardware open questions | **Closed** (family only, unpaid, own PC) |
| Consent form for adults | **Family consent:** parent/guardian consent for each child, a spoken assent script for the child, and adult consent (`qa/playtesting/pilot_kit/consent_form.md`) |

The M1 gate (2027-03-31) can still take place, but its USP readability and performance rows will read "not measured
(DEC-003)". The GO / RE-SCOPE call then rests on the server-authority and determinism evidence and the family's impressions.

## Producer notes (risks, recorded once)
- **Audience mismatch.** The approved audience is core/hardcore adults 18–35 (market review, 2026-10-19). Children aged 4–14 are
  a different audience, so their reactions do not predict the target market. This is fine for a private game. If the game is
  later opened, the formal USP playtest (QA plan Part B) should run with the target audience first.
- **Content for young children.** The Memory Grove build lets the player hit and kill creatures, and other creatures then grieve
  or flee. For the youngest testers, the parent decides whether to explain or skip that key. A "no harm" option that hides hitting
  can be added if the stakeholder wants it.
- **Children's data.** Sessions are recorded only if the parent wants to. The game's telemetry is anonymous and stays on the
  family's PC.

## Changelog
- **0.1 (2026-10-21):** Recorded the stakeholder's decision.
