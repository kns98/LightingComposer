namespace LightingShowcase.Composer.Navigation;

public static class ViewportNavigationInputFactory
{
    public static IViewportNavigationInput Create() =>
        new TrackpadViewportNavigationInput();
}
