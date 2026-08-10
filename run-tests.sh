#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Debug --no-restore
dotnet test LightingShowcase.Composer.sln -c Debug --no-build --logger "console;verbosity=normal"
