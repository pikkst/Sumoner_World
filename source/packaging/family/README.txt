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
   Near a creature, a line at the bottom shows what you can do:
   E feed   F hit   R free from a snare   P offer a Pact   Q shout
   Close the window to stop.


4. IF SOMETHING GOES WRONG
   - The window stays empty or says nothing: the server is probably not running. Do step 1.
   - The game asks Windows Firewall for access: you can answer "Cancel". The game only talks to this PC.
   - Anything else: write down what happened and roughly when.

Before a test session: see the consent form and family questionnaire (qa/playtesting/pilot_kit/ in the repository).
The game writes an anonymous log only in the offline test mode, never in this build's normal play.
