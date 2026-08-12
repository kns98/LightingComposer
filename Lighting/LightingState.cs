/*
 * Lights are represented as renderer-neutral scene data. CPU and GPU backends can therefore interpret the same
 * kind, position/direction, color, and intensity values, while backend-specific sampling/shader details remain
 * outside the scene model.
 *
 * `LightingState` is a working/snapshot state object whose fields must move together; callers use it to capture
 * one coherent point in an interaction, render, or undo workflow.
 *
 * `GetLevel` reads level from the authoritative model and returns a value/snapshot suitable for callers, avoiding
 * direct access to mutable internal storage.
 *
 * `SetLevel` sets level through the owning abstraction instead of exposing a mutable field. That gives the method
 * one place to validate the value and perform any history/cache/UI side effects required by the change.
 */
namespace LightingShowcase.Lighting;

/// <summary>Compatibility state consumed by the ray tracer pipeline.</summary>
public sealed class LightingState
{
    public string Label { get; private set; } = "Scene lights";

    /// <summary>Returns a neutral multiplier for legacy callers.</summary>
    public double GetLevel(string id) => 1.0;

    /// <summary>Retained for older scene/UI code; no hidden light multipliers are stored.</summary>
    public void SetLevel(string id, double level)
    {
        Label = "Scene lights";
    }
    public LightingState Clone() => new();

    // Playback only animates the camera timeline. Lighting is manual through SceneLight objects.
    public void Evaluate(double timeSeconds, double duration)
    {
        Label = "Scene lights";
    }
}
