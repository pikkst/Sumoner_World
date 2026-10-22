# HUD and Settings: System Spec v0.1 (M2-17)

Status: **draft 0.1 (2026-10-22)**. The M2 plan asks for health, stamina, mana, companion status, key hints and settings
(volume, mouse sensitivity), readable for children (testers are 4–14, DEC-003). Everything marked *(filled)* was chosen by
the design team and can be changed by the stakeholder.

## 1. Rules for every word on screen
- **Plain and short:** "Low health! Step back, eat or block." rather than numbers. Exact numbers stay on the server (combat
  spec §11); bars show fractions.
- **Big enough:** body text 15–18 px at 100 %, titles 20–26 px, every label outlined in black so it reads over grass and sky.
- **Never what a creature thinks:** no line names memories, trust or fear (AI Bible); the creature's body tells that.

## 2. Screen layout
| Where | What |
|---|---|
| Bottom left, above the action bar | Combat keys (hint), a warning when a vital is low, **Health**, **Stamina** and **Mana** bars with their names on them, class · rank · level · gold, the experience bar, "F1: keys  Esc: settings" (hint) |
| Bottom middle | The action bar 1–0 (M2-14b) and, above it, the interaction prompt for the creature in reach |
| Top left | One **card per companion**: name, level and life stage; "away (Z to call)" or "with you" with a health bar and its health in words (well, hurt, badly hurt, knocked out); the companion keys under them (hint) |
| Top right | The quest tracker (M2-19) |
| Middle | Windows (bag, crafting, quests, abilities, companion, Academy), the key list (F1) and settings (Esc) |

- A **low** vital is under a quarter. The warning names one problem: health first, then stamina, then mana;
  the health bar also blinks. Nothing warns while the player is down (the "You are down" line says it).
- The vitals sit above the action bar so the bar never covers them, at any text size.

## 3. Key list (F1)
All keys in five groups (Moving, Fighting, Creatures, Your companions, Windows), each key next to what it does. It opens by
itself the **first time** a player starts the game *(filled)*. It takes as many columns as fit and scrolls when taller than
the screen. F1, Esc or Close shut it. A test checks that every key the game listens to is in the list.

## 4. Settings (Esc)
Esc opens the settings when no other window is open (every window closes on Esc first).
| Setting | Range | Default |
|---|---|---|
| Sound volume | 0–100 % (0 = off) | 80 % |
| Mouse speed (camera turning) | 25–300 % | 100 % |
| Text size (all windows and text) | 100 %, 125 %, 150 % | 100 % |
| Show key hints on screen | on / off | on |

- A change applies at once and is **kept per account** on the computer (`user://settings-<account>.txt`), so children who
  share a computer each keep theirs. Broken or unknown values fall back to the default or the nearest allowed value.
- There is no game sound yet; the volume already sets the master sound bus, so sound added later follows it.

## Changelog
- **0.1 (2026-10-22):** first draft with M2-17, filled by the design team.
