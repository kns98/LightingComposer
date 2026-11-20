// File: Rendering/Compute/Gpu/CameraConstants.cs
// Purpose: Per-tile camera and sampling constants for the compute shader.

using System.Numerics;
using System.Runtime.InteropServices;
using LightingShowcase.CameraSystem;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CameraConstants
{
    public readonly Vector4 CameraPosition;
    public readonly Vector4 CameraForward;
    public readonly Vector4 CameraRight;
    public readonly Vector4 CameraUp;
    public readonly uint Width;
    public readonly uint Height;
    public readonly uint TriangleCount;
    public readonly uint LightCount;
    public readonly uint BounceCount;
    public readonly uint SampleIndex;
    public readonly uint TileYOffset;
    public readonly uint TileHeight;
    public readonly uint BvhNodeCount;
    public readonly uint SampleCount;
    public readonly uint Reserved1;
    public readonly uint Reserved2;
    public readonly Vector4 BackgroundTopFov;
    public readonly Vector4 BackgroundBottomExposure;
    public readonly Vector4 LightingOptions;

    /// <summary>Packs the camera and tile constants for the compute shader.</summary>
    public CameraConstants(
        Vec3 position, CameraBasis basis, int width, int height,
        int triangleCount, int lightCount, int bounceCount, int sampleIndex,
        int tileYOffset, int tileHeight, int bvhNodeCount, int sampleCount,
        RenderSettings settings, double fieldOfViewDegrees)
    {
        CameraPosition = position.ToVector4(1.0f);
        CameraForward = basis.Forward.ToVector4(0.0f);
        CameraRight = basis.Right.ToVector4(0.0f);
        CameraUp = basis.Up.ToVector4(0.0f);
        Width = checked((uint)width);
        Height = checked((uint)height);
        TriangleCount = checked((uint)triangleCount);
        LightCount = checked((uint)lightCount);
        BounceCount = checked((uint)Math.Clamp(bounceCount, 0, 8));
        SampleIndex = checked((uint)Math.Max(0, sampleIndex));
        TileYOffset = checked((uint)Math.Max(0, tileYOffset));
        TileHeight = checked((uint)Math.Max(1, tileHeight));
        BvhNodeCount = checked((uint)Math.Max(0, bvhNodeCount));
        SampleCount = checked((uint)Math.Clamp(sampleCount, 1, 4096));
        Reserved1 = 0;
        Reserved2 = 0;

        double clampedFov = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
        float fovScale = (float)Math.Tan(clampedFov * Math.PI / 360.0);
        BackgroundTopFov = new Vector4(
            (float)Math.Max(0.0, settings.BackgroundTop.X),
            (float)Math.Max(0.0, settings.BackgroundTop.Y),
            (float)Math.Max(0.0, settings.BackgroundTop.Z),
            fovScale);
        BackgroundBottomExposure = new Vector4(
            (float)Math.Max(0.0, settings.BackgroundBottom.X),
            (float)Math.Max(0.0, settings.BackgroundBottom.Y),
            (float)Math.Max(0.0, settings.BackgroundBottom.Z),
            (float)Math.Clamp(settings.Exposure, 0.01, 100.0));
        LightingOptions = new Vector4(
            (float)Math.Clamp(settings.AmbientStrength, 0.0, 100.0),
            settings.UseShadows ? 1.0f : 0.0f,
            settings.Mode == RenderMode.Wireframe ? 1.0f : 0.0f,
            0.0f);
    }
}
