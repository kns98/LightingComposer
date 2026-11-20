// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/HeadlessLighting.cs
// Purpose: Headless-only default light placement relative to the camera.
//
// Imported and explicit lights stay where they are. Lights flagged as defaults
// are moved next to the camera so they always project onto the visible area,
// no matter which model or camera target was requested.
// -----------------------------------------------------------------------------

using LightingShowcase.CameraSystem;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;
using LightingShowcase.SceneGraph;

namespace LightingShowcase.CommandLine;

internal static class HeadlessLighting
{
    /// <summary>Applies explicit request lights or seats default lights around the camera.</summary>
    public static void Resolve(Scene scene, CameraDefinition camera, RenderRequest request)
    {
        if (request.Lights.Count > 0)
        {
            scene.Lights.Clear();
            foreach (LightRequest light in request.Lights)
                scene.Lights.Add(light.ToSceneLight());
            return;
        }

        CameraBasis basis = camera.ToBasis();
        double dist = Math.Max(0.5, (camera.Position - camera.Target).Length());
        Vec3 keyPosition = camera.Position + basis.Up * (dist * 0.55) + basis.Right * (dist * 0.40);
        Vec3 fillPosition = camera.Position - basis.Right * (dist * 0.65) + basis.Up * (dist * 0.18);

        bool anyEnabled = false;
        int placed = 0;
        foreach (SceneLight light in scene.Lights)
        {
            if (!light.Enabled)
                continue;
            anyEnabled = true;
            if (!light.IsDefault || placed >= 2)
                continue;
            light.Position = placed == 0 ? keyPosition : fillPosition;
            if (light.Kind == SceneLightKind.Spot)
                light.Direction = (camera.Target - light.Position).Normalize();
            placed++;
        }

        if (!anyEnabled)
        {
            scene.Lights.Add(new SceneLight("cli_key", keyPosition, new Vec3(1.0, 0.96, 0.88), 5.0, isDefault: true));
            scene.Lights.Add(new SceneLight("cli_fill", fillPosition, new Vec3(0.75, 0.85, 1.0), 2.2, isDefault: true));
        }
    }
}
