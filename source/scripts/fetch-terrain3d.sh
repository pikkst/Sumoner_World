#!/usr/bin/env bash
# Installs Terrain3D (MIT, TDD §5.1 / §7) into project-sumoner-worl/addons/terrain_3d, pinned by version and SHA-256.
# The addon is not committed (52 MB of binaries for every platform); only the Windows and Linux x86_64 libraries are kept.
# Run once from source/, then open the project in the editor (or run godot --headless --import) so Godot registers the extension.
set -euo pipefail
cd "$(dirname "$0")/.."

version="1.0.2-stable"
sha256="a071850250ec5e596aa54da61c01d75768774eb379ee997584d426a45f4884a2"
url="https://github.com/TokisanGames/Terrain3D/releases/download/v${version}/Terrain3D_v${version}.zip"
target="project-sumoner-worl/addons/terrain_3d"

if [ -f "$target/.version" ] && [ "$(cat "$target/.version")" = "$version" ]; then
  echo "Terrain3D $version already installed"; exit 0
fi

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
curl -sSL -o "$work/t3d.zip" "$url"
echo "$sha256  $work/t3d.zip" | sha256sum -c -
unzip -q "$work/t3d.zip" -d "$work/x"
find "$work/x/addons/terrain_3d/bin" -mindepth 1 -maxdepth 1 ! -name '*windows*x86_64*' ! -name '*linux*x86_64*' -exec rm -rf {} +
rm -rf "$target"
mkdir -p "$(dirname "$target")"
mv "$work/x/addons/terrain_3d" "$target"
echo "$version" > "$target/.version"
echo "Terrain3D $version installed in $target"
