#!/usr/bin/env bash
# This script turns a repeatable repository task into one command. It resolves the required paths/options, invokes the relevant build or Composer executable, and deliberately lets a failing command produce a non-zero exit code so developers and CI do not mistake a partial run for success.
set -euo pipefail
root="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
rid="${1:-linux-x64}"
out="${2:-$root/publish/$rid}"
rm -rf "$out"
dotnet restore "$root/LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj" --runtime "$rid"
dotnet publish "$root/LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj" \
  --configuration Release \
  --runtime "$rid" \
  --self-contained false \
  -p:SelfContained=false \
  -p:UseAppHost=true \
  --output "$out" \
  --no-restore
"$out/LightingShowcase.Composer" --help >/dev/null
printf 'Published to %s\n' "$out"
