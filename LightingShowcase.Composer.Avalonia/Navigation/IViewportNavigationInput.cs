using Avalonia.Controls;

namespace LightingShowcase.Composer.Navigation;

public readonly record struct OrbitInput(double X, double Y);

public readonly record struct ZoomInput(double Amount, string Source);

public interface IViewportNavigationInput : IDisposable
{
    string BackendName { get; }
    bool IsAvailable { get; }

    event EventHandler<OrbitInput>? Orbit;
    event EventHandler<ZoomInput>? Zoom;

    void Attach(Control viewport);
    void Detach();
}
