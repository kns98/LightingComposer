// File: Rendering/Compute/PreviewTileDispatcher.cs
// Purpose: Dispatches compute tiles while publishing progress previews.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class PreviewTileDispatcher
{
    private readonly ComputeStageLog log;

    public PreviewTileDispatcher(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Dispatches compute tiles while publishing progress previews.</summary>
    public void Dispatch(
        GraphicsDevice gd, Pipeline pipeline, ResourceSet resourceSet, DeviceBuffer constantsBuffer,
        DeviceBuffer outputBuffer, DeviceBuffer stagingBuffer, uint outputBytes,
        PreparedComputeScene prepared, ComputeFrameRequest request, int tileRows,
        Action<RenderImage, string> progressCallback)
    {
        for (int tileY = 0; tileY < request.Height; tileY += tileRows)
        {
            log.ThrowIfCancellationRequested(request.Token, $"Dispatch tile y={tileY}");
            int rows = Math.Min(tileRows, request.Height - tileY);
            gd.UpdateBuffer(constantsBuffer, 0, new CameraConstants(
                request.CameraPosition, request.Basis, request.Width, request.Height,
                prepared.TriangleCount, prepared.LightCount, request.BounceCount, request.SampleIndex,
                tileY, rows, prepared.BvhNodeCount, request.SampleCount,
                request.Settings!, request.FieldOfViewDegrees));

            using CommandList tileCommandList = gd.ResourceFactory.CreateCommandList();
            tileCommandList.Begin();
            tileCommandList.SetPipeline(pipeline);
            tileCommandList.SetComputeResourceSet(0, resourceSet);
            tileCommandList.Dispatch(((uint)request.Width + 7u) / 8u, ((uint)rows + 7u) / 8u, 1u);
            tileCommandList.End();

            gd.SubmitCommands(tileCommandList);
            gd.WaitForIdle();

            if ((tileY / Math.Max(1, tileRows)) % 8 == 0)
                log.Stage($"Tile completed: y={tileY}, rows={rows}");

            if (ShouldPublishTileProgress(tileY, rows, tileRows, request.Height, request.Width, prepared.TriangleCount))
            {
                log.Stage($"Publish partial Vulkan tile preview: y={tileY}, rows={rows}");
                RenderImage partial = new FrameOutput(log).ReadBack(
                    gd, gd.ResourceFactory, outputBuffer, stagingBuffer, outputBytes,
                    request.Width, request.Height, request.Token, "partial tile readback");
                progressCallback(partial, $"VULKAN GPU - tile {Math.Min(request.Height, tileY + rows)}/{request.Height}, samples {request.SampleIndex + 1}-{request.SampleIndex + request.SampleCount}");
            }
        }
    }

    /// <summary>Returns true when the current tile should publish a progress preview.</summary>
    private static bool ShouldPublishTileProgress(int tileY, int currentTileRows, int tileRows, int height, int width, int triangleCount)
    {
        int completedRows = tileY + currentTileRows;
        if (completedRows >= height)
            return true;

        // Every partial preview hands a full bitmap to the UI. For heavy
        // scenes only publish the final tile of each sample.
        long pixelCount = (long)Math.Max(1, width) * Math.Max(1, height);
        if (triangleCount > 50000 || pixelCount > 1_000_000)
            return false;

        if (tileY == 0)
            return true;

        int publishEveryRows = Math.Max(Math.Max(1, tileRows), Math.Max(1, height / 8));
        return completedRows / publishEveryRows != tileY / publishEveryRows;
    }
}
