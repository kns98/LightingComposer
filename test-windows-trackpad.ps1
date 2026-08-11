$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "LightingShowcase.Composer.Avalonia\LightingShowcase.Composer.Avalonia.csproj"
$solution = Join-Path $PSScriptRoot "LightingShowcase.Composer.sln"

$env:LIGHTINGSHOWCASE_NAV_DIAGNOSTICS = "1"

Write-Host "Building Lighting Composer (.NET 10 / Avalonia 12.1.1)..."
dotnet restore $solution
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $solution -c Debug --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host ""
Write-Host "Navigation diagnostics enabled."
Write-Host "Test: two-finger move = orbit; pinch = zoom."
Write-Host "Watch for [NAV] zoom source=pinch, touchpad-magnify, or ctrl-wheel."
Write-Host ""

dotnet run --project $project --no-build
exit $LASTEXITCODE
