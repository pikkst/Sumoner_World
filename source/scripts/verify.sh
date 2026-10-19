#!/usr/bin/env sh
# Local run of the same gates as CI. Required before any task is marked Done (R55).
set -e
cd "$(dirname "$0")/.."
dotnet restore SumonerWorld.sln
dotnet format SumonerWorld.sln --verify-no-changes --no-restore
dotnet build SumonerWorld.sln -c Release --no-restore
dotnet test SumonerWorld.sln -c Release --no-build
echo "All quality gates passed."
