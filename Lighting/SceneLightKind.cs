/*
 * Lights are represented as renderer-neutral scene data. CPU and GPU backends can therefore interpret the same
 * kind, position/direction, color, and intensity values, while backend-specific sampling/shader details remain
 * outside the scene model.
 */
namespace LightingShowcase.Lighting;

// SceneLightKind makes a closed set of choices compiler-visible instead of passing loosely related integers or
// strings. Code that switches over Point, Directional, Spot is where the behavioral meaning of each choice is
// implemented.
/// <summary>Light shape used by imported glTF lights and the ray tracer.</summary>
public enum SceneLightKind
{
    Point,
    Directional,
    Spot
}
