// File: Rendering/Compute/ComputeFrameDetails.cs
// Purpose: Human-readable compute trace summary with counts and settings.

namespace LightingShowcase.Rendering;

internal static class ComputeFrameDetails
{
    public static string Build(ComputeFrameRequest request, PreparedComputeScene prepared, int tileRows)
    {
        string triangleTruncated = prepared.SourceTriangleCount > prepared.TriangleCount ? $", triangles truncated from {prepared.SourceTriangleCount}" : string.Empty;
        string lightTruncated = prepared.SourceLightCount > prepared.LightCount ? $", lights truncated from {prepared.SourceLightCount}" : string.Empty;
        return $"VULKAN GPU COMPUTE BVH TRACE - {request.Width}x{request.Height}, revision={prepared.CacheStamp.Revision}, {prepared.TriangleCount} triangles{triangleTruncated}, {prepared.BvhNodeCount} BVH nodes, {prepared.LightCount} lights{lightTruncated}, textures={prepared.TextureCount}, material=linear-srgb+pbr+smooth-normal+normal-map+occlusion+ibl, bounces={Math.Clamp(request.BounceCount, 0, 8)}, samples={request.SampleIndex + 1}-{request.SampleIndex + request.SampleCount}, fov={request.FieldOfViewDegrees:0.##}, exposure={request.Settings!.Exposure:0.###}, ambient={request.Settings.AmbientStrength:0.###}, shadows={request.Settings.UseShadows}, tileRows={tileRows}";
    }
}
