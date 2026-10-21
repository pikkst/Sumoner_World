# Moderator script: Memory Grove session (≈ 30 min)

Lines in **quotes and bold** are read word for word. Everything else is guidance for the moderator. The test only works if the
tester is never told, and never guesses from us, that creatures remember. **Never say "memory", "remember", "relationship",
"trust" or "bond"**, not even when the tester uses those words first. If they do, respond neutrally ("mm-hm", "go on").

## 0. Before (2 min)
- Recording ready but **not started**. Build closed. Questionnaire ready but not shown.
- **"Hi, thanks for coming. Today you'll play an early prototype of a game for about fifteen minutes, then answer a few questions.
  The whole thing takes about half an hour. We're testing the game, not you, so there are no wrong ways to play."**

## 1. Consent (3 min)
- Give the consent form (`consent_form.md`). Let them read it. Answer questions about the form only.
- If they do not agree to the recording, thank them and end the session. A session without a recording cannot be scored.
- When the form is signed: start the recording.
- **"The recording is on now."**

## 2. Briefing (1 min): read word for word, nothing more
- **"This is an early prototype. Explore and interact with the creatures however you like. Please think aloud if you can."**
- Then the controls, also word for word:
  **"You walk with W, A, S and D. Hold the right mouse button and move the mouse to turn the camera, and use the mouse wheel to
  zoom. When you are close to a creature, a line at the bottom shows the keys you can use with it."**
- Start the build (`--playtest`). Note the clock time when the tester can first move: that is 0:00 of play.

## 3. Free play (15 min)
- Say nothing unless the tester speaks to you. Do not point at the screen. Do not react to what they do (no laughing, no "oh!").
- If they go quiet for more than a minute: **"What are you thinking right now?"** No other prompts.
- Questions, and what to answer:

| Tester asks | Moderator answers |
|---|---|
| anything about what creatures do, think or want ("Why did it run?", "Does it like me?") | **"I can't say, sorry. What do you think?"** |
| what a key does / how to do something | repeat the controls line from the briefing, nothing more |
| "Am I doing it right?" / "What should I do?" | **"There's no right way. Do whatever you like."** |
| the game crashes or freezes | note the time, restart with `--playtest`, add the restart to the notes. Play time continues from where it stopped |
| technical (sound, mouse, too dark) | help, as long as it is not about creatures |

- At 15:00: **"Thanks, that's time. Let's stop here."** Close the build. Leave the recording running.

## 4. Questionnaire (8 min)
- Give `questionnaire.md` **one page at a time**. Page 1 (Q1) is collected before page 2 is shown, so Q2 cannot steer Q1.
- Do not explain the questions beyond reading them aloud again. If a tester asks what Q2 means: **"However you understand it."**

## 5. Debrief (1 min)
- **"Thank you, that was really helpful. Please don't tell other people what's in the game or what we asked, since they may test
  it too."**
- Only now may you answer the tester's questions about the game, but **not** the purpose of the test, if they may tell other
  testers. Stop the recording.

## 6. Notes, straight after (5 min)
On a sheet that stays with the session id (never with the tester's name):
- session id, start clock time of play, crashes or restarts (time), anything the tester did that the script did not cover.
- Your impressions are **not** codes: the raters code from the recording, blind to your notes.

## Changelog
- **0.1 (2026-10-21):** First version.
