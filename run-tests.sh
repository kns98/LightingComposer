#!/usr/bin/env bash
# This script turns a repeatable repository task into one command. It resolves the required paths/options, invokes the relevant build or Composer executable, and deliberately lets a failing command produce a non-zero exit code so developers and CI do not mistake a partial run for success.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$root"
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Debug --no-restore
dotnet test LightingShowcase.Composer.sln -c Debug --no-build --logger "console;verbosity=normal"
