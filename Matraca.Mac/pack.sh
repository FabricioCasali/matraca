#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
DOTNET="${DOTNET:-/usr/local/share/dotnet/dotnet}"
PUBLISH="$ROOT/bin/publish"
APP="$ROOT/bin/Matraca.app"
VERSION="${1:-${MATRACA_VERSION:-}}"
if [[ -z "$VERSION" ]]; then
  VERSION="$($DOTNET msbuild "$ROOT/Matraca.Mac.csproj" -getProperty:Version -nologo | tr -d '\r')"
fi
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid version: $VERSION" >&2
  exit 1
fi
ZIP="$ROOT/bin/Matraca-${VERSION}-macos-arm64.zip"
BUNDLE_ID="io.github.fabriciocasali.matraca"

if [[ -n "${MATRACA_SIGN_IDENTITY:-}" ]]; then
  IDENTITY="$MATRACA_SIGN_IDENTITY"
elif security find-identity -v -p codesigning 2>/dev/null | grep -q '"Matraca Dev"'; then
  IDENTITY="Matraca Dev"
else
  echo "Matraca Dev signing identity was not found." >&2
  exit 1
fi

rm -rf "$PUBLISH" "$APP" "$ZIP"
"$DOTNET" publish "$ROOT/Matraca.Mac.csproj" -c Release -r osx-arm64 \
  --self-contained true -p:Version="$VERSION" -o "$PUBLISH"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources/Web"
cp "$ROOT/Info.plist" "$APP/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$VERSION" "$APP/Contents/Info.plist"
cp "$ROOT/Resources/Matraca.icns" "$APP/Contents/Resources/Matraca.icns"
cp -R "$ROOT/Resources/pt-BR.lproj" "$APP/Contents/Resources/pt-BR.lproj"
cp -R "$ROOT/Resources/en.lproj" "$APP/Contents/Resources/en.lproj"
cp -R "$PUBLISH"/ "$APP/Contents/MacOS/"
if [[ ! -d "$APP/Contents/MacOS/Web" ]]; then
  echo "Published web assets were not found." >&2
  exit 1
fi
mv "$APP/Contents/MacOS/Web"/* "$APP/Contents/Resources/Web/"
rmdir "$APP/Contents/MacOS/Web"
chmod +x "$APP/Contents/MacOS/Matraca"

if [[ -d "$APP/Contents/MacOS/runtimes" ]]; then
  find "$APP/Contents/MacOS/runtimes" -mindepth 1 -maxdepth 1 -type d \
    ! -name 'macos-arm64' -exec rm -rf {} +
fi

while IFS= read -r -d '' file; do
  codesign -f -s "$IDENTITY" --timestamp=none "$file" >/dev/null 2>&1
done < <(find "$APP/Contents/MacOS" -type f ! -name 'Matraca' -print0)

codesign -f -s "$IDENTITY" --timestamp=none --identifier "$BUNDLE_ID" "$APP"
codesign --verify --deep --strict "$APP"
ditto -c -k --norsrc --keepParent "$APP" "$ZIP"
echo "$APP"
echo "$ZIP"
