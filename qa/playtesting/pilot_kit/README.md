# Memory Grove pilot kit (M1-23d)

> **DEC-003 (2026-10-21):** the game is private. For now it is tested only by the creator and family (children 4–14 and their
> parents) on the family PC with the local Docker dev server, with no outside testers and no formal pilot or gate run. For
> family sessions use `consent_form.md` (adult, parent and child parts) and `family_questionnaire.md`. The rest of this kit
> (moderator script, coding guide, report tool) is kept for a later public test with the target audience.

Everything a moderator and the raters need to run the USP playtest in `qa/test-plans/M1_benchmark_and_playtest.md` Part B. It
covers the pilot of 3 (2027-02) and, once the pilot fixes are in, the gate run of at least 20 testers (2027-03). The build is
described in `../memory_grove_build.md`.

| File | Who uses it | When |
|---|---|---|
| `moderator_script.md` | moderator | the whole session, word for word where marked |
| `consent_form.md` | adult players; a parent for each child | before the first session (organiser Eventnexus OÜ) |
| `family_questionnaire.md` | parent (ages 4–7, asked aloud), children 8–14 | straight after play, in family sessions |
| `questionnaire.md` | tester | straight after the 15 minutes of play |
| `coding_guide.md` | raters A, B and C | after the session, from the recording |
| `coding_sheet.csv` | raters | one row per rater per session |
| `responses.csv` | moderator | one row per session (Q2–Q4 answers) |

## Session checklist
Before the tester arrives (or joins the call):
1. Start the build once with `--playtest` and close it, so the grove is fresh and the telemetry folder exists.
2. Start the screen recording with game audio and the microphone. Remote: the tester shares their screen in the call, and the
   moderator records the call.
3. Have the consent form and questionnaire open. Do not have the design documents, the AI Inspector or this kit on screen.

During the session: follow `moderator_script.md`.

After the session:
1. Note the session id: the last 6 characters of the newest file name in the telemetry folder
   (`%APPDATA%\Godot\app_userdata\Sumoner World\telemetry\session-<time>-<id>.jsonl`). Remote testers send that file.
2. Save the recording and the telemetry file as `<id>.mp4` and `<id>.jsonl` in the project's private playtest storage (not in this
   repo). Record the questionnaire answers in `responses.csv` under the same id.
3. Note the tester's name only on the consent record, never next to the session id. The id links nothing back to a person.

## Data handling
- Recordings and telemetry are kept for **90 days after the session** and then deleted (QA plan B4). The report keeps only
  numbers and anonymous quotes.
- Telemetry holds no personal data (random session id, game events only). Recordings show the tester's voice and possibly face,
  so they are personal data. Access is limited to the moderator and the raters.

## Producing the numbers
When the raters have filled `coding_sheet.csv`:
```
dotnet run --project source/tests/SumonerWorld.PlaytestReport -- <folder with the .jsonl files> coding_sheet.csv responses.csv result.md
```
This gives the noticed share with its 95% Wilson interval and the B7 outcome, Cohen's κ between raters A and B, median
time-to-notice, how many memory moments were on screen, and checks to look at again: a session coded "noticed" with no on-screen
memory moment by 10:00, a coded session without telemetry, a time after 10:00. With fewer than 20 coded sessions it states that
this is not a gate result. The written findings (top 5 confusion points, quotes) are added by hand to
`qa/playtesting/M1_usp_report.md`.

## What the pilot is for
The pilot does not count towards the gate. After the 3 sessions, check and fix:
- **Script:** did any wording hint at memory? Did a tester need help that the script does not cover?
- **Questionnaire:** were any questions misunderstood? Q1 must come before Q2, so no tester sees Q2 before answering Q1.
- **Coding guide:** code the 3 sessions with both raters. Where the raters disagreed, sharpen the rule or add an example.
- **Staging:** use the report's staging lines. If a tester never saw a memory moment on screen, the grove layout needs a fix, not
  the creatures' behaviour.
- **Build:** crashes, confusing controls, frame rate on the tester's PC.

Record what changed in the changelog at the end of each file.

## Not ready yet
- A public test: deferred (DEC-003). Recruiting, payment and tester hardware are closed questions for now.

## Changelog
- **0.2 (2026-10-21):** DEC-003: family testing; family questionnaire; consent rewritten.
- **0.1 (2026-10-21):** First version.
