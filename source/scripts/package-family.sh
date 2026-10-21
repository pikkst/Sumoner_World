#!/usr/bin/env bash
# Builds the family test package (DEC-003, M1-23e): a Windows release export of the game, the launchers and README from
# packaging/family/, zipped as builds/SumonerWorld-family-<commit>.zip at the repository root (builds/ is not committed).
# Needs: Godot 4.7.2 .NET with its export templates installed, and Terrain3D (scripts/fetch-terrain3d.sh) for the real terrain.
#   GODOT=/path/to/Godot_v4.7.2-stable_mono_win64_console.exe bash scripts/package-family.sh
set -euo pipefail
cd "$(dirname "$0")/.."

godot="${GODOT:?Set GODOT to the Godot 4.7.2 .NET console executable}"
commit="$(git rev-parse --short HEAD)"
out="../builds/family"
zip="../builds/SumonerWorld-family-${commit}.zip"

rm -rf "$out"
mkdir -p "$out"
# stdin closed: otherwise the console wrapper can keep waiting after the export has finished.
"$godot" --headless --path project-sumoner-worl --export-release "Windows Desktop" "../$out/SumonerWorld.exe" < /dev/null
test -f "$out/SumonerWorld.exe"
cp packaging/family/*.cmd packaging/family/README.txt "$out/"
echo "$commit" > "$out/VERSION.txt"

rm -f "$zip"
powershell.exe -NoProfile -Command "Compress-Archive -Path '$(cygpath -w "$out")\\*' -DestinationPath '$(cygpath -w "$zip")'"
echo "Package: $zip"
