using Avalonia;
using LightingShowcase.CommandLine;

namespace LightingShowcase.Composer;

internal static class Program
{
    internal static string[] StartupArguments { get; private set; } = Array.Empty<string>();

    [STAThread]
    public static int Main(string[] args)
    {
        if (RenderJobRunner.TryHandleRendererProcessArgument(args, out int infrastructureExitCode))
            return infrastructureExitCode;

        if (args.Length > 0)
        {
            string command = args[0].ToLowerInvariant();
            if (command is "render" or "headless" or "formats" or "help" or "--help" or "-h")
                return RunCommandLineAsync(args).GetAwaiter().GetResult();

            if (command == "compose")
                args = args.Skip(1).ToArray();
        }

        StartupArguments = args;
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    private static async Task<int> RunCommandLineAsync(string[] args)
    {
        using CancellationTokenSource cancellation = new();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            string command = args[0].ToLowerInvariant();
            if (command == "formats")
            {
                foreach (string extension in SupportedSceneFormats.Extensions)
                    Console.WriteLine(extension);
                return 0;
            }

            if (command is "help" or "--help" or "-h" ||
                args.Length == 1 ||
                args.Skip(1).Any(argument => argument is "--help" or "-h"))
            {
                return PrintHelp();
            }

            CommandLineArguments values = CommandLineArguments.Parse(args.Skip(1).ToArray());
            RenderRequest request = RenderRequest.FromCommandLine(values);
            RenderJobResult result = await new RenderJobRunner().RunAsync(request, cancellation.Token).ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }));
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("LIGHTINGSHOWCASE_VERBOSE_ERRORS") == "1")
                Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
            RenderJobRunner.DisposeSharedResources();
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
LightingShowcase Avalonia Composer

GUI:
  LightingShowcase.Composer
  LightingShowcase.Composer compose [scene-or-model]
  LightingShowcase.Composer [scene-or-model]

Headless render:
  LightingShowcase.Composer render <scene> [options]
  LightingShowcase.Composer headless <scene> [options]

Common render options:
  --output <path>
  --renderer raster|raster-vulkan|vulkan|cpu
  --width <pixels> --height <pixels>
  --samples <count> --bounces <count>
  --fov <degrees> --exposure <value>

Other:
  LightingShowcase.Composer formats
""");
        return 0;
    }
}
