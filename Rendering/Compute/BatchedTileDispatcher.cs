// File: Rendering/Compute/BatchedTileDispatcher.cs
// Purpose: Dispatches compute tiles in bounded batched submissions.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class BatchedTileDispatcher
{
    private readonly ComputeStageLog log;

    public BatchedTileDispatcher(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Dispatches compute tiles in bounded batched submissions.</summary>
    public void Dispatch(
        GraphicsDevice gd, Pipeline pipeline, ResourceLayout layout, DeviceBuffer outputBuffer,
        PreparedComputeScene prepared, ComputeFrameRequest request, int tileRows, uint constantBytes)
    {
        int tilesPerSubmit = TileScheduler.ChooseTilesPerSubmit(
            tileRows, request.BounceCount, request.SampleCount, prepared.TriangleCount, request.Width, prepared.LightCount);
        log.Stage($"Batch Vulkan tile dispatches in bounded submissions: tileRows={tileRows}, tilesPerSubmit={tilesPerSubmit}");

        for (int tileY = 0; tileY < request.Height;)
        {
            List<(DeviceBuffer Constants, ResourceSet Set, int TileY, int Rows)> tileResources = new();
            try
            {
                for (int tileIndex = 0; tileIndex < tilesPerSubmit && tileY < request.Height; tileIndex++, tileY += tileRows)
                {
                    log.ThrowIfCancellationRequested(request.Token, $"Prepare batched tile y={tileY}");
                    int currentTileRows = Math.Min(tileRows, request.Height - tileY);
                    DeviceBuffer tileConstants = gd.ResourceFactory.CreateBuffer(new BufferDescription(constantBytes, BufferUsage.UniformBuffer));
                    gd.UpdateBuffer(tileConstants, 0, TileConstants(prepared, request, tileY, currentTileRows));
                    ResourceSet tileSet = gd.ResourceFactory.CreateResourceSet(new ResourceSetDescription(
                        layout,
                        outputBuffer,
                        prepared.Buffers.Triangles,
                        prepared.Buffers.BvhNodes,
                        prepared.Buffers.Lights,
                        tileConstants,
                        prepared.Buffers.TexturePixels,
                        prepared.Buffers.TextureInfos));
                    tileResources.Add((tileConstants, tileSet, tileY, currentTileRows));
                }

                using CommandList commandList = gd.ResourceFactory.CreateCommandList();
                commandList.Begin();
                commandList.SetPipeline(pipeline);
                foreach (var tile in tileResources)
                {
                    log.ThrowIfCancellationRequested(request.Token, $"Record batched tile y={tile.TileY}");
                    commandList.SetComputeResourceSet(0, tile.Set);
                    commandList.Dispatch(((uint)request.Width + 7u) / 8u, ((uint)tile.Rows + 7u) / 8u, 1u);
                }
                commandList.End();

                int firstTileY = tileResources.Count == 0 ? tileY : tileResources[0].TileY;
                int lastTileY = tileResources.Count == 0 ? tileY : tileResources[^1].TileY;
                log.Stage($"Submit bounded Vulkan tile batch: count={tileResources.Count}, y={firstTileY}-{lastTileY}");
                gd.SubmitCommands(commandList);
                gd.WaitForIdle();
                log.Stage("Bounded Vulkan tile batch idle returned");
            }
            finally
            {
                for (int i = tileResources.Count - 1; i >= 0; i--)
                {
                    GpuDisposal.Dispose(tileResources[i].Set);
                    GpuDisposal.Dispose(tileResources[i].Constants);
                }
            }
        }
    }

    private static CameraConstants TileConstants(PreparedComputeScene prepared, ComputeFrameRequest request, int tileY, int rows) =>
        new(request.CameraPosition, request.Basis, request.Width, request.Height,
            prepared.TriangleCount, prepared.LightCount, request.BounceCount, request.SampleIndex,
            tileY, rows, prepared.BvhNodeCount, request.SampleCount,
            request.Settings!, request.FieldOfViewDegrees);
}
