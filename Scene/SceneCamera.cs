// -----------------------------------------------------------------------------
// File: Scene/SceneCamera.cs
// Purpose: Named camera stored in the scene for the animation studio.
// -----------------------------------------------------------------------------
using LightingShowcase.CameraSystem;

namespace LightingShowcase.SceneGraph;

/// <summary>Named renderer-neutral camera saved with the scene.</summary>
public sealed class SceneCamera
{
    public int Id { get; }
    public string Name { get; set; }
    public CameraDefinition Definition { get; set; }

    /// <summary>Creates a stored camera with the given id, name, and definition.</summary>
    public SceneCamera(int id, string name, CameraDefinition definition)
    {
        Id = id;
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("A camera name is required.", nameof(name));
        Name = name.Trim();
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    /// <summary>Returns the camera name for list display.</summary>
    public override string ToString() => Name;
}
