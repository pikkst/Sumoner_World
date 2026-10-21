# Coding guide: "Did the tester notice creature memory?" (QA plan B5)

Two raters (A and B) code every session **independently**, from the recording and the tester's written answers. They do not talk
about a session until both have entered their codes. Rater C codes only the sessions where A and B disagree, and C's code is
final. The moderator's notes are not used for coding, except for crash times.

## The question per session
**Noticed = yes** when all three hold:
1. **A specific reaction.** The tester describes (in think-aloud, Q1 or Q2) something a particular creature or group did:
   came closer, followed them, kept away, growled, ran, the herd left, it looked happy to see them.
2. **Linked to their own earlier action, correctly.** The tester says, or clearly implies, that the reaction happened *because of
   something they did before*, and that action really happened before the reaction in the recording.
3. **In time.** The reaction they describe happened **at or before 10:00 of play** (0:00 = when the tester could first move).

Otherwise **noticed = no**.

### What the creatures can remember (for the raters only)
| Tester's action (key) | What the creature may then do |
|---|---|
| feed (E) | after several meals: comes closer, Trust emote |
| hit (F) | keeps its distance, fear, or anger in bold creatures |
| kill (third hit) | others of its kind nearby flee or get angry; the herd may avoid the player |
| free from a snare (R) | Affection emote, follows or stays near the player; herd-mates may come closer, curious |
| shout near predators (Q) | predators keep away, fear |
| offer a Pact (P) | accepted or refused |

## Examples
| Tester says | Code | Why |
|---|---|---|
| "The little one I freed keeps following me." (and it did) | **yes** | specific creature, linked to freeing it |
| "That fox is scared of me now, I hit it earlier." | **yes** | specific, linked |
| "After I killed one, the rest of them run whenever I get close." | **yes** | a group reaction counts |
| "The animals seem smart." | no | not specific, no link |
| "This one likes me." (with no earlier action mentioned) | no | no link to their own action |
| "It ran because I walked at it." (the reaction is to the current action) | no | a reaction to now, not to earlier |
| "I fed it and it ate." | no | the immediate result, not a later change |
| "It followed me because I fed it." but the recording shows the tester never fed it (it was the freed calf) | no | the link is wrong. Note it as "misattributed" |
| Q2: "Yes" with no description | no | not specific |
| Describes it at 12:30 of a reaction that first happened at 11:50 | no | after 10:00 |
| Describes it at 12:30 of a reaction that the video shows at 8:10 | **yes** | the reaction was in time, even though they said it later |

When unsure between yes and no, code **no** and write why in `notes`. Rater C settles it.

## Time-to-notice
For a "yes": the earlier of (a) the first think-aloud mention of the remembered reaction, and (b) the first clear change in the
tester's behaviour in response to it (for example they start approaching the calf they freed, or avoid the creature they hit).
Write it as play time `m:ss` (subtract the 0:00 offset; leave out crash downtime). Leave blank for "no".

## Filling `coding_sheet.csv`
One row per rater per session:

| Column | Content |
|---|---|
| `session` | the 6-character session id |
| `rater` | A, B or C |
| `noticed` | yes / no |
| `time_to_notice` | `m:ss` of play, blank for no |
| `reaction` | short: which creature, what it did, at what time |
| `linked_action` | short: the tester's earlier action they linked it to |
| `notes` | why, especially for borderline cases. Use quotes in a spreadsheet for text with commas |

## Agreement
After every 5 sessions (and after the pilot), run the report tool. If Cohen's κ between A and B is **below 0.7**, stop, discuss
the disagreements, sharpen this guide (add the case to the examples), and **re-code all sessions** with the new version.

## Telemetry check
The report flags sessions coded "yes" where the telemetry shows no memory moment on the tester's screen before 10:00. Look at the
video again: either the tester saw it in a way the camera check missed (code stays), or the code was wrong (fix it). Both
outcomes are fine; write which in `notes`.

## Changelog
- **0.1 (2026-10-21):** First version, before the pilot.
