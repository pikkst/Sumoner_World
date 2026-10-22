@echo off
rem Sumoner World: plays on the local dev server as player 3 (account 1003). Start the server first, see README.txt.
start "" "%~dp0SumonerWorld.exe" -- --connect=127.0.0.1:7777 --account=1003
