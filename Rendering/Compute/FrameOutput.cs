// File: Rendering/Compute/FrameOutput.cs
// Purpose: Output buffer clearing and staging readback into render images.

using Veldrid;

namespace LightingShowcase.Rendering;

internal sealed class FrameOutput
{
    private readonly ComputeStageLog log;

    public FrameOutput(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Clears the GPU output buffer in small chunks.</summary>
    public void Zero(GraphicsDevice gd, DeviceBuffer outputBuffer, uint outputBytes)
    {
        // Avoid allocating one giant byte[width * height * 4] just to clear the
        // output. Update the GPU buffer in small reusable chunks instead.
        const int chunkBytes = 1024 * 1024;
        byte[] zeroChunk = new byte[Math.Min(chunkBytes, checked((int)Math.Min(outputBytes, int.MaxValue)))];
        uint offset = 0;
        while (offset < outputBytes)
        {
            int count = checked((int)Math.Min((uint)zeroChunk.Length, outputBytes - offset));
            if (count == zeroChunk.Length)
            {
                gd.UpdateBuffer(outputBuffer, offset, zeroChunk);
            }
            else
            {
                // Last chunk only; keep this allocation below 1 MB.
                gd.UpdateBuffer(outputBuffer, offset, new byte[count]);
            }
            offset += (uint)count;
        }
    }

    /// <summary>Reads the GPU output buffer back into a render image.</summary>
    public RenderImage ReadBack(
        GraphicsDevice gd, ResourceFactory factory, DeviceBuffer outputBuffer, DeviceBuffer stagingBuffer,
        uint outputBytes, int width, int height, CancellationToken token, string stageName)
    {
        log.Stage($"Copy output buffer to staging buffer ({stageName})");
        using (CommandList copyCommandList = factory.CreateCommandList())
        {
            copyCommandList.Begin();
            copyCommandList.CopyBuffer(outputBuffer, 0, stagingBuffer, 0, outputBytes);
            copyCommandList.End();
            gd.SubmitCommands(copyCommandList);
            gd.WaitForIdle();
        }
        log.Stage($"GPU copy/readback idle returned ({stageName})");

        log.ThrowIfCancellationRequested(token, "Map staging buffer for readback: " + stageName);
        MappedResourceView<uint> mapped = gd.Map<uint>(stagingBuffer, MapMode.Read);
        try
        {
            log.Stage("Convert readback to bitmap: " + stageName);
            return ToRenderImage(mapped, width, height);
        }
        finally
        {
            gd.Unmap(stagingBuffer);
        }
    }

    /// <summary>Copies mapped GPU pixels into a render image.</summary>
    private static RenderImage ToRenderImage(MappedResourceView<uint> pixels, int width, int height)
    {
        uint[] copy = new uint[checked(width * height)];
        for (int i = 0; i < copy.Length; i++)
            copy[i] = pixels[i];
        return new RenderImage(width, height, copy);
    }
}
