// File: Rendering/Compute/ComputeShaderCompiler.cs
// Purpose: Locates and compiles the compute GLSL shader with diagnostics.

using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace LightingShowcase.Rendering;

internal sealed class ComputeShaderCompiler
{
    private readonly ComputeStageLog log;

    public ComputeShaderCompiler(ComputeStageLog log)
    {
        this.log = log;
    }

    /// <summary>Creates the compute shader with diagnostic logging on failure.</summary>
    public Shader Create(ResourceFactory factory)
    {
        log.Stage("Locate compute shader source");
        string shaderPath = Path.Combine(AppContext.BaseDirectory, "Shaders", "vulkan_scene_primary.comp");
        if (!File.Exists(shaderPath))
            shaderPath = Path.Combine("Shaders", "vulkan_scene_primary.comp");
        if (!File.Exists(shaderPath))
            throw new FileNotFoundException("Vulkan scene compute shader was not found.", shaderPath);

        log.Stage($"Read compute shader: {shaderPath}");
        string shaderSource = File.ReadAllText(shaderPath);
        ShaderDescription computeDesc = new(ShaderStages.Compute, Encoding.UTF8.GetBytes(shaderSource), "main");

        log.Stage("Compile GLSL to SPIR-V / create shared shader");
        try
        {
            log.Stage($"Shader source length: {shaderSource.Length} chars");
            log.Stage("Shader first line: " + (shaderSource.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<empty>"));
            Shader shader = factory.CreateFromSpirv(computeDesc);
            log.Stage("Shader compile/create succeeded");
            return shader;
        }
        catch (Exception ex)
        {
            log.Stage("Shader compile/create failed");
            log.Stage("Shader path: " + shaderPath);
            log.Stage("Shader exception type: " + ex.GetType().FullName);
            log.Stage("Shader exception message: " + ex.Message);
            log.Stage("Shader exception full text:");
            log.Stage(ex.ToString());

            try
            {
                string diagnosticShaderCopy = Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp");
                File.WriteAllText(diagnosticShaderCopy, shaderSource);
                log.Stage("Failed shader source copied to: " + diagnosticShaderCopy);
            }
            catch (Exception copyEx)
            {
                log.Stage("Could not copy failed shader source: " + copyEx.GetType().Name + ": " + copyEx.Message);
            }

            throw new InvalidOperationException(
                "Vulkan compute shader compilation failed. See " + ComputeStageLog.LogPath + " and " + Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-failed-shader.comp") + " for details.",
                ex);
        }
    }
}
