# Validation notes for rotation and scale gizmos

## Completed in this package

- Verified every project file is well-formed XML.
- Verified the GitHub Actions workflow is well-formed YAML.
- Checked changed C# files for balanced lexical delimiters after excluding strings and comments.
- Checked the generated patch for whitespace errors.
- Added normal regression coverage for deferred move, rotation, and non-uniform scale commits.
- Added an opt-in Vulkan raster integration test for live pending rotation and scaling.

## Required target-machine verification

The packaging environment did not contain the .NET 8 SDK, a C# compiler, a GLSL compiler, or a Vulkan device. Build and GPU timing therefore need to be run on the target machine:

```bash
dotnet restore LightingShowcase.Composer.sln
dotnet build LightingShowcase.Composer.sln -c Release
dotnet test LightingShowcase.Composer.sln -c Release
LIGHTINGSHOWCASE_RUN_GPU_TESTS=1 dotnet test \
  LightingShowcase.Composer.Tests/LightingShowcase.Composer.Tests.csproj \
  -c Release --filter Category=Gpu
```

For the interactive performance check, use Vulkan raster, select a representative object, press `R` or `S`, and drag a handle. The details line should contain `cache=hot` and `live-transform=<selection id>`. It reports uniform update, command recording, GPU wait, readback, and total frame time separately. A vertex refresh should occur only after pointer release.
