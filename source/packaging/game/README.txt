SUMONER WORLD - test build
==========================
Private early version. Copyright Eventnexus OÜ. Do not share.

The game plays on the local dev server, which runs in Docker on this PC. Nothing goes over the internet.


1. START THE SERVER (once per PC start)
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
   F1 shows every key in the game; the list also opens by itself the first time you play.
   Esc opens the settings (sound, mouse speed, text size, key hints) or closes the open window.

   W A S D                 walk
   Right mouse button      hold and move the mouse to turn the camera; mouse wheel zooms
   Left click              attack (hold longer for a heavy attack)
   Shift (hold)            block; raised just before a hit, it parries
   Space                   dodge
   Middle click or T       lock on to the creature in front of you (again to let go)
   1 2 3 4 5 6 7 8 9 0     the action bar: spells, food and commands for your companions
   L                       the ability book: drag spells, commands and companion moves onto the bar
   Near a creature:        E feed   F hit   R free from a snare   P offer a Pact   Q shout
   H                       gather from a berry bush, a root patch or fallen branches
   Z / X                   call a companion (a creature that accepted your Pact) / send it back
   K                       your companion's level, bond and skill tree
   I                       your bag and gear, and your companions' bags and gear
   B                       crafting
   J                       quests (what to do next is shown at the top right)


4. HOW IT STARTS
   New characters start at the Summoner Academy: the Headmaster stands right behind you. Press E to talk
   to him and answer his questions (there are no wrong answers). Your answers decide your class and rank.
   Arena Master Brann sends you to the arena in between. After your rank, Keeper Lisel brings you a young
   creature of your own: feed it (E) and offer it a Pact (P). Then call it with Z.
   After the Academy, the quests lead you on into the world.

   Creatures remember what you do to them, and they tell each other. A creature you have helped comes
   closer; one you have hurt keeps away, and so does its herd.


5. IF SOMETHING GOES WRONG
   - The window stays empty or says nothing: the server is probably not running. Do step 1.
   - "This game package does not match the server": get the newest package and rebuild the server (step 1).
   - The game asks Windows Firewall for access: you can answer "Cancel". The game only talks to this PC.
   - Anything else: write down what happened and roughly when.

Before a test session: see the consent form and questionnaire (qa/playtesting/pilot_kit/ in the repository).
The game writes an anonymous log only in the offline test mode, never in this build's normal play.
