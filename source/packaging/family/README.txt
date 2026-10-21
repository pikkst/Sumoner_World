SUMONER WORLD - family test build
=================================
Private early prototype. Copyright Eventnexus OÜ. Do not share.

The game plays on the local dev server, which runs in Docker on this PC. Nothing goes over the internet.


1. START THE SERVER (once per PC start; an adult does this)
   - Start Docker Desktop and wait until it says it is running.
   - In PowerShell, in the game's source folder (the "source" folder of the repository):
         docker compose -f docker-compose.dev.yml up --build -d
   - The server keeps running in the background and remembers the world (creatures remember you too).
     Stop it:              docker compose -f docker-compose.dev.yml down
     Start a fresh world:  docker compose -f docker-compose.dev.yml down -v   (then "up" again)


2. PLAY
   Double-click "Play.cmd".
   If more people play at the same time on other windows, the second player uses "Play as player 2.cmd", and so on.
   Each player number is its own character, and creatures remember each player separately.


3. CONTROLS
   W A S D                 walk
   Right mouse button      hold and move the mouse to turn the camera
   Mouse wheel             zoom in and out
   Left click              attack where you look (hold the button longer for a heavy attack)
   Shift (hold)            block; raised just before a hit, it parries
   Space                   dodge
   1 2 3 4 5 6 7 8 9 0     the action bar at the bottom of the screen. At first: 1 thornbolt (a thorn
                           that hurts and slows), 2 entangle (roots creatures where you aim, or the one
                           you locked on), 3 mend (heals you or your locked-on companion), 5 berry meal,
                           7 attack, 8 guard me, 9 hold here, 0 come back (commands for your companions).
                           Spells cost mana (the blue bar).
   L                       the ability book: drag a spell or a command onto the bar to put it on a key.
                           Drag a button onto another to swap them, off the bar to clear it.
   Middle click or T       lock the camera on the nearest creature in front (again to let go);
                           your attacks then aim at it by themselves
   Near a creature, a line at the bottom shows what you can do:
   E feed   F hit   R free from a snare   P offer a Pact   Q shout
   Z calls a companion (a creature that accepted your Pact): you draw a glowing sigil for a
   moment and it comes to your side. X sends it back. Your companions are listed at the top left.
   A companion follows you, defends you when something attacks you and helps when you fight.
   An independent one may wander off a little when you stand still for a while.
   Commands for your companions (on the bar): attack (the creature you locked on with T, or the one
   near you), guard me, hold here, come back. Watch how they answer: a companion that trusts you
   obeys at once; one that is unsure pauses or does it its own way; one may even refuse.
   Loot: a defeated creature leaves a bag for everyone who fought it; walk onto your bag to pick it up.
   H gathers from a berry bush, a sweetroot patch or fallen branches nearby (they grow back by the next day).
   I opens your bag and character: drag gear onto the slots on your figure to put it on, and back
   into the bag to take it off. Right-click food to eat it. The tabs at the top show a companion
   at your side: dress it too (a bird wears only a hat and a charm), and drag things into its own
   bag (a bird carries little, a four-legged one more). Drag food from the bag onto the bar.
   B opens crafting: click a recipe to make it. Stand still for a few seconds; if something hits
   you, you stop and lose nothing.
   K opens the companion panel: your companion's level, bond and skill tree. Teach it a skill with
   1-9 or a click. A move it has learned (like Talon dive) can be dragged onto the bar: press it
   in a fight and your companion uses that move on your target, if it listens to you. It likes some branches more than others; a branch it dislikes costs 2 points.
   P opens the Pact menu: choose up to 3 promises with the number keys 1-6 (or click them),
   then Enter (or "Offer the Pact"); Esc closes it. Watch the creature: it answers first with
   its body (it comes closer, or turns away), then a line tells you its answer.
   Close the window to stop.


4. IF SOMETHING GOES WRONG
   - The window stays empty or says nothing: the server is probably not running. Do step 1.
   - The game asks Windows Firewall for access: you can answer "Cancel". The game only talks to this PC.
   - Anything else: write down what happened and roughly when.

Before a test session: see the consent form and family questionnaire (qa/playtesting/pilot_kit/ in the repository).
The game writes an anonymous log only in the offline test mode, never in this build's normal play.
