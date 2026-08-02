#!/usr/bin/env bash
set -euo pipefail
root="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec dotnet run --project "$root/LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj" -- "$@"
