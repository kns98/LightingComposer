$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet restore .\LightingShowcase.Composer.sln
dotnet build .\LightingShowcase.Composer.sln -c Debug --no-restore
dotnet test .\LightingShowcase.Composer.sln -c Debug --no-build --logger "console;verbosity=normal"
