using System.Globalization;
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

/// <summary>
/// Testable transform action shared by the Avalonia Apply button and the test suite.
/// It proves that text entered in the inspector reaches the authoritative scene model.
/// </summary>
internal sealed record ComposerTransformRequest(Vec3 Position, Vec3 RotationRadians, Vec3 Scale)
{
    public static ComposerTransformRequest Parse(
        string? positionX, string? positionY, string? positionZ,
        string? rotationXDegrees, string? rotationYDegrees, string? rotationZDegrees,
        string? scaleX, string? scaleY, string? scaleZ)
    {
        Vec3 position = new(
            ParseFinite(positionX, "Position X", 0.0),
            ParseFinite(positionY, "Position Y", 0.0),
            ParseFinite(positionZ, "Position Z", 0.0));
        Vec3 rotationDegrees = new(
            ParseFinite(rotationXDegrees, "Rotation X", 0.0),
            ParseFinite(rotationYDegrees, "Rotation Y", 0.0),
            ParseFinite(rotationZDegrees, "Rotation Z", 0.0));
        Vec3 scale = new(
            ParsePositive(scaleX, "Scale X", 1.0),
            ParsePositive(scaleY, "Scale Y", 1.0),
            ParsePositive(scaleZ, "Scale Z", 1.0));

        return new ComposerTransformRequest(
            position,
            rotationDegrees * (Math.PI / 180.0),
            scale);
    }

    public bool Apply(
        ComposerSceneSession session,
        int objectId,
        string name,
        bool visible)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UpdateObject(objectId, name, visible, Position, RotationRadians, Scale);
    }

    private static double ParseFinite(string? text, string label, double blankValue)
    {
        if (string.IsNullOrWhiteSpace(text))
            return blankValue;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariant) && double.IsFinite(invariant))
            return invariant;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double current) && double.IsFinite(current))
            return current;
        throw new FormatException($"{label} must be a finite number.");
    }

    private static double ParsePositive(string? text, string label, double blankValue)
    {
        double value = ParseFinite(text, label, blankValue);
        if (value <= 0.0)
            throw new ArgumentOutOfRangeException(label, $"{label} must be greater than zero.");
        return value;
    }
}
