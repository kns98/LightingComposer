// -----------------------------------------------------------------------------
// File: Rendering/VulkanLoader.cs
// Purpose: Fast Vulkan loader presence probe for renderer defaults.
//
// Unlike VeldridVulkanDevicePreflight (which creates a real device in an
// isolated child process), this only checks that a Vulkan loader library can
// be found and loaded. Device creation is still guarded lazily by the
// preflight when a Vulkan renderer first runs.
// -----------------------------------------------------------------------------
using System.Runtime.InteropServices;

namespace LightingShowcase.Rendering;

public static class VulkanLoader
{
    /// <summary>Returns true when a Vulkan loader library is present. Never throws.</summary>
    public static bool IsPresent()
    {
        try
        {
            foreach (string name in CandidateNames())
            {
                if (NativeLibrary.TryLoad(name, typeof(VulkanLoader).Assembly, null, out IntPtr handle))
                {
                    NativeLibrary.Free(handle);
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static IEnumerable<string> CandidateNames()
    {
        if (OperatingSystem.IsWindows())
            yield return "vulkan-1.dll";
        if (OperatingSystem.IsLinux())
            yield return "libvulkan.so.1";
        if (OperatingSystem.IsMacOS())
        {
            yield return "libvulkan.dylib";
            yield return "libMoltenVK.dylib";
        }
        // Fallbacks in case the OS check misses (e.g., Wine); failed loads are cheap.
        yield return "vulkan-1.dll";
        yield return "libvulkan.so.1";
    }
}
