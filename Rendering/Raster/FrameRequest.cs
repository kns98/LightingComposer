// File: Rendering/Raster/FrameRequest.cs
// Purpose: Immutable inputs for one raster frame render.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed record FrameRequest(
    Scene Scene,
    Vec3 CameraPosition,
    CameraBasis Basis,
    int Width,
    int Height,
    VulkanRasterTransformPreview? TransformPreview,
    VulkanRasterMeshEditPreview? MeshEditPreview,
    CancellationToken Token,
    bool Wireframe);
