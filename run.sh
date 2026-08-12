#!/usr/bin/env bash
# This script turns a repeatable repository task into one command. It resolves the required paths/options, invokes the relevant build or Composer executable, and deliberately lets a failing command produce a non-zero exit code so developers and CI do not mistake a partial run for success.
set -euo pipefail
root="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec dotnet run --project "$root/LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj" -- "$@"
