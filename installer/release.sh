#!/usr/bin/env bash
# Publishes the version in Directory.Build.props as a GitHub release with its installer attached.
# The app's "Actualizar" button looks for the latest release, so this is what reaches users.
# Usage: installer/release.sh "Novedades de esta versión"
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")"
notes="${1:?Indica las novedades de la versión entre comillas}"
setup="$root/dist/EasyClothes-$version-setup.exe"

if gh release view "v$version" >/dev/null 2>&1; then
  echo "La versión $version ya está publicada; sube <Version> en Directory.Build.props." >&2
  exit 1
fi

"$root/installer/build.sh"
gh release create "v$version" "$setup" --title "EasyClothes $version" --notes "$notes"
