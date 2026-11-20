// File: Rendering/Compute/ComputeFrameRequest.cs
// Purpose: Immutable inputs for one compute trace render.

using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

internal sealed record ComputeFrameRequest(
    Scene Scene,
    Vec3 CameraPosition,
    CameraBasis Basis,
    int Width,
    int Height,
    int BounceCount,
    int SampleIndex,
    int SampleCount,
    CancellationToken Token,
    Action<RenderImage, string>? ProgressCallback,
    RenderSettings? Settings,
    double FieldOfViewDegrees);
