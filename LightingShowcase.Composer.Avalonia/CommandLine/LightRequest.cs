// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/CommandLine/LightRequest.cs
// Purpose: Headless CLI light description.
//
// One --light option value: semicolon-separated key=value pairs such as
// "kind=spot;position=3,4,-2;color=1,0.96,0.88;intensity=5.2". Only position
// is required; every other key has a sensible default.
// -----------------------------------------------------------------------------

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.CommandLine;

public sealed class LightRequest
{
    public SceneLightKind Kind { get; init; } = SceneLightKind.Point;
    public required Vec3 Position { get; init; }
    public Vec3 Direction { get; init; } = new(0, 0, -1);
    public Vec3 Color { get; init; } = new(1, 1, 1);
    public double Intensity { get; init; } = 5.0;
    public double Range { get; init; }
    public double InnerCone { get; init; }
    public double OuterCone { get; init; } = Math.PI / 4.0;
    public bool CastsShadow { get; init; } = true;
    public bool Enabled { get; init; } = true;
    public string Id { get; init; } = "light";

    /// <summary>Parses one --light option value; index numbers the option for ids and errors.</summary>
    public static LightRequest Parse(string spec, int index)
    {
        string label = $"--light #{index + 1}";
        if (string.IsNullOrWhiteSpace(spec))
            throw new ArgumentException($"{label} must be semicolon-separated key=value pairs with a position.");

        Vec3? position = null;
        SceneLightKind kind = SceneLightKind.Point;
        Vec3 direction = new(0, 0, -1);
        Vec3 color = new(1, 1, 1);
        double intensity = 5.0;
        double range = 0.0;
        double innerCone = 0.0;
        double outerCone = Math.PI / 4.0;
        bool castsShadow = true;
        bool enabled = true;
        string id = $"light{index + 1}";

        foreach (string part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
                throw new ArgumentException($"{label} has a malformed entry '{part}'; use key=value.");
            string key = part[..equals].Trim().ToLowerInvariant();
            string value = part[(equals + 1)..].Trim();
            switch (key)
            {
                case "kind":
                    kind = value.ToLowerInvariant() switch
                    {
                        "point" => SceneLightKind.Point,
                        "spot" => SceneLightKind.Spot,
                        "directional" or "direction" or "sun" => SceneLightKind.Directional,
                        _ => throw new ArgumentException($"{label} kind must be point, spot, or directional.")
                    };
                    break;
                case "position":
                    position = RenderRequest.ParseVector(value, $"{label} position");
                    break;
                case "direction":
                    direction = RenderRequest.ParseVector(value, $"{label} direction") ?? new Vec3(0, 0, -1);
                    break;
                case "color":
                    color = RenderRequest.ParseColor(value, $"{label} color") ?? new Vec3(1, 1, 1);
                    break;
                case "intensity":
                    intensity = ParseFinite(value, $"{label} intensity");
                    break;
                case "range":
                    range = ParseFinite(value, $"{label} range");
                    break;
                case "inner":
                    innerCone = ParseFinite(value, $"{label} inner cone");
                    break;
                case "outer":
                    outerCone = ParseFinite(value, $"{label} outer cone");
                    break;
                case "shadows":
                    castsShadow = ParseBool(value, $"{label} shadows");
                    break;
                case "enabled":
                    enabled = ParseBool(value, $"{label} enabled");
                    break;
                case "id":
                    if (string.IsNullOrWhiteSpace(value))
                        throw new ArgumentException($"{label} id cannot be empty.");
                    id = value;
                    break;
                default:
                    throw new ArgumentException($"{label} has an unknown key '{key}'.");
            }
        }

        if (range < 0.0)
            throw new ArgumentException($"{label} range cannot be negative.");
        return new LightRequest
        {
            Kind = kind,
            Position = position ?? throw new ArgumentException($"{label} requires a position, for example position=3,4,-2."),
            Direction = direction,
            Color = color,
            Intensity = intensity,
            Range = range,
            InnerCone = innerCone,
            OuterCone = outerCone,
            CastsShadow = castsShadow,
            Enabled = enabled,
            Id = id
        };
    }

    /// <summary>Converts this request into a scene light.</summary>
    public SceneLight ToSceneLight() => new(
        Id, Position, Color, Intensity, Enabled, Kind, Direction, Range, InnerCone, OuterCone, CastsShadow);

    private static double ParseFinite(string value, string name)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))
            throw new ArgumentException($"{name} must be a finite invariant-culture number.");
        return parsed;
    }

    private static bool ParseBool(string value, string name) =>
        value.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => throw new ArgumentException($"{name} must be true/false, yes/no, on/off, or 1/0.")
        };
}
