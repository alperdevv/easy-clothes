#!/usr/bin/env bash
# Builds the Windows installer from WSL: publishes the app and the MCP server into one folder on the
# Windows side and packs it with Inno Setup 6 (ISCC.exe). The setup ends up in dist/.
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.Build.props")"

win_temp="$(wslpath "$(cmd.exe /c 'echo %TEMP%' 2>/dev/null | tr -d '\r')")"
staging="$win_temp/easyclothes-$version"
rm -rf "$staging"
for project in App Mcp; do
  dotnet publish "$root/src/EasyClothes.$project" -c Release -r win-x64 --self-contained -o "$staging" --nologo -v quiet
done

iscc=""
for candidate in "/mnt/c/Program Files (x86)/Inno Setup 6/ISCC.exe" "/mnt/c/Program Files/Inno Setup 6/ISCC.exe" \
  "$(wslpath "$(cmd.exe /c 'echo %LOCALAPPDATA%' 2>/dev/null | tr -d '\r')")/Programs/Inno Setup 6/ISCC.exe"; do
  if [[ -f "$candidate" ]]; then iscc="$candidate"; break; fi
done
if [[ -z "$iscc" ]]; then
  echo "No se encuentra Inno Setup 6. Instálalo con: winget install JRSoftware.InnoSetup" >&2
  exit 1
fi

mkdir -p "$root/dist"
# ISCC is a Windows program: give it Windows paths. Its output goes next to the staging folder, then into dist/.
"$iscc" /Q "/DAppVersion=$version" "/DSourceDir=$(wslpath -w "$staging")" "/O$(wslpath -w "$win_temp")" "$(wslpath -w "$root/installer/EasyClothes.iss")"
mv "$win_temp/EasyClothes-$version-setup.exe" "$root/dist/"
echo "Instalador: $root/dist/EasyClothes-$version-setup.exe"
