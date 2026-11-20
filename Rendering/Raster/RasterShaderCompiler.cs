// File: Rendering/Raster/RasterShaderCompiler.cs
// Purpose: Compiles the embedded raster GLSL shaders to SPIR-V.

using System.Reflection;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace LightingShowcase.Rendering;

internal sealed class RasterShaderCompiler
{
    private readonly RasterStageLog log;

    public RasterShaderCompiler(RasterStageLog log)
    {
        this.log = log;
    }

    /// <summary>Creates the shared raster vertex shader from the embedded source.</summary>
    public Shader CreateVertexShader(ResourceFactory factory)
    {
        ShaderDescription vertex = new(ShaderStages.Vertex, Encoding.UTF8.GetBytes(LoadSource("Raster.raster.vert.glsl")), "main");
        try
        {
            log.Stage("Compile/create Vulkan raster vertex shader");
            return factory.CreateFromSpirv(vertex);
        }
        catch (Exception ex)
        {
            log.Stage("Vulkan raster vertex shader compile/create failed");
            log.Stage(ex.ToString());
            throw new InvalidOperationException("Vulkan raster vertex shader compilation failed. See " + RasterStageLog.LogPath + ".", ex);
        }
    }

    /// <summary>Creates a raster fragment shader variant, optionally with a compile define.</summary>
    public Shader CreateFragmentShader(ResourceFactory factory, string? define)
    {
        string source = InjectDefine(LoadSource("Raster.raster.frag.glsl"), define);
        ShaderDescription fragment = new(ShaderStages.Fragment, Encoding.UTF8.GetBytes(source), "main");
        try
        {
            log.Stage("Compile/create Vulkan raster fragment shader " + (define ?? "default"));
            return factory.CreateFromSpirv(fragment);
        }
        catch (Exception ex)
        {
            log.Stage("Vulkan raster fragment shader compile/create failed");
            log.Stage(ex.ToString());
            try
            {
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "LightingShowcase-vulkan-raster-failed.frag"), source);
            }
            catch { }
            throw new InvalidOperationException("Vulkan raster fragment shader compilation failed. See " + RasterStageLog.LogPath + " and %TEMP%\\LightingShowcase-vulkan-raster-failed.frag.", ex);
        }
    }

    /// <summary>Inserts a compile define after the GLSL version line.</summary>
    private static string InjectDefine(string source, string? define)
    {
        if (string.IsNullOrEmpty(define))
            return source;
        int lineStart = 0;
        while (lineStart < source.Length)
        {
            int newline = source.IndexOf('\n', lineStart);
            if (newline < 0)
                break;
            string line = source.Substring(lineStart, newline - lineStart);
            if (line.TrimStart().StartsWith("#version", StringComparison.Ordinal))
                return source.Insert(newline + 1, "#define " + define + " 1\n");
            lineStart = newline + 1;
        }
        throw new InvalidOperationException("Cannot inject '" + define + "': no #version line in fragment shader source.");
    }

    private static string LoadSource(string resourceName)
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream == null)
            throw new InvalidOperationException($"Embedded raster shader '{resourceName}' was not found.");
        using StreamReader reader = new(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
