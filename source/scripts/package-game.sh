#!/usr/bin/env bash
# Builds the test package (DEC-003, M1-23e, E1): a Windows release export of the game, the launchers and README from
# packaging/game/, zipped as builds/SumonerWorld-<commit>.zip at the repository root (builds/ is not committed).
# Needs: Godot 4.7.2 .NET with its export templates installed, and Terrain3D (scripts/fetch-terrain3d.sh) for the real terrain.
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_win64_console.exe bash scripts/package-game.sh
set -euo pipefail
cd "$(dirname "$0")/.."

godot="${GODOT:?Set GODOT to the Godot 4.7.2 .NET console executable}"
commit="$(git rev-parse --short HEAD)"
out="../builds/game"
zip="../builds/SumonerWorld-${commit}.zip"

rm -rf "$out"
mkdir -p "$out"
# The export builds the C# project; MSBuild worker nodes and the build server outlive it and would keep inherited pipes open, so
# they are turned off, and Godot's output goes to a log file (an open stdin or stdout pipe made the console wrapper wait forever).
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
if ! "$godot" --headless --path project-sumoner-worl --export-release "Windows Desktop" "../$out/SumonerWorld.exe" < /dev/null > "../builds/export.log" 2>&1; then
  echo "Export failed, see builds/export.log"; exit 1
fi
test -f "$out/SumonerWorld.exe"
cp packaging/game/*.cmd packaging/game/README.txt "$out/"
echo "$commit" > "$out/VERSION.txt"

rm -f "$zip"
powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$out")\\*' -DestinationPath '$(cygpath -w "$zip")'"
echo "Package: $zip"
