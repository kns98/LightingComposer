# Vulkan component-edit build fix

## Error corrected

The compiler reported `CS0122` because the containing `VulkanRasterRenderer`
accessed `PreparedRasterScene.ActiveMeshEdit`, while that nested member had the
default `private` accessibility.

The declaration is now:

```csharp
public MeshEditPatchState? ActiveMeshEdit { get; set; }
```

`PreparedRasterScene` itself remains a private implementation type, so this does
not add a public API to the renderer assembly. It only permits the containing
renderer implementation to read and update the active GPU mesh-edit patch state.

## Target validation

```bash
dotnet build LightingShowcase.Core/LightingShowcase.Core.csproj -c Release
dotnet build LightingShowcase.Composer.Avalonia/LightingShowcase.Composer.Avalonia.csproj -c Release
```
