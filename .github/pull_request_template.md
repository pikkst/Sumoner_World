## Task
ID: M?-?? · Rules touched: R??, R??

## What changed
<!-- Small, focused change (R40). -->

## Definition of Done (R53-R55)
- [ ] Acceptance criteria met, with evidence attached (test output / capture / playtest note)
- [ ] `source/scripts/verify.sh` passes (format, analyzers, tests)
- [ ] Tests added for new behaviour; regression test for bug fixes (R42-R44)
- [ ] No assertions weakened to hide a bug (R46). If a test changed, explain the spec change:
- [ ] No magic numbers; gameplay values in data files (R08, R48)
- [ ] Public APIs documented; GDD / TDD / AI Bible updated if behaviour changed (R49, R50)
- [ ] Perf-sensitive: before/after profiler capture attached (R22, R23)
- [ ] Save format: version bumped + migration + fixture test (R26, R52)
- [ ] Net/economy: server validates intents, idempotent, duplicate/reconnect tested (R30-R34)
- [ ] Checked system_registry.md; no duplicate system introduced (R47)

## Review (R41)
- [ ] Producer agent  - [ ] QA agent  - [ ] Stakeholder (required for Net / Persistence / security)

Merging requires explicit stakeholder approval (R56).
