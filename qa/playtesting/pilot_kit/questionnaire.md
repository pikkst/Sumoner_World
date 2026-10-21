# Questionnaire (QA plan B4)

Shown **one page at a time**. The moderator collects page 1 before handing out page 2. The order is fixed: open questions come
before the prompted one, so the prompted question cannot steer the open answers.

Session id (moderator fills in): ________

---

## Page 1

**1. Describe anything the creatures did that surprised you.**

_______________________________________________________________________

_______________________________________________________________________

_______________________________________________________________________

---

## Page 2

**2. Did any creature seem to treat you differently because of something you did earlier?**

☐ Yes  ☐ No  ☐ Not sure

If yes, describe it: what had you done, and what did the creature do afterwards?

_______________________________________________________________________

_______________________________________________________________________

---

## Page 3

**3. How much did you care about what happened to the creatures?**

Not at all  ① ② ③ ④ ⑤ ⑥ ⑦  Very much

**4. How much do you want to keep playing?**

Not at all  ① ② ③ ④ ⑤ ⑥ ⑦  Very much

**5. What was confusing?**

_______________________________________________________________________

_______________________________________________________________________

---

## Entering the answers
In `responses.csv`: `session` (id), `q2` (yes / no; "not sure" is left blank), `q3` and `q4` (1–7). Q1, Q2's description and Q5
are typed into the session's notes for the raters and for the confusion points in the report.

## Changelog
- **0.1 (2026-10-21):** First version. Added "Not sure" to Q2, so a tester who is unsure is not pushed into yes or no; it counts as
  no answer.
