# This PowerShell entry point packages a repeatable build/run operation. Parameters are translated into the exact underlying command and failures are allowed to propagate as a non-zero result, which makes the same script suitable for both local use and automation.
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet restore .\LightingShowcase.Composer.sln
dotnet build .\LightingShowcase.Composer.sln -c Debug --no-restore
dotnet test .\LightingShowcase.Composer.sln -c Debug --no-build --logger "console;verbosity=normal"
