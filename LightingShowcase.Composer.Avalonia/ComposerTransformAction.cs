// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/ComposerTransformAction.cs
// Purpose: Inspector transform parsing and apply action.
//
// Parses position, rotation, and scale text into a ComposerTransformRequest and commits it to the scene model.
// -----------------------------------------------------------------------------

using System.Globalization;
using LightingShowcase.Math3D;

namespace LightingShowcase.Composer;

/// <summary>
/// Transform action used by the Avalonia Apply button to commit inspector values
/// to the authoritative scene model.
/// </summary>
internal sealed record ComposerTransformRequest(Vec3 Position, Vec3 RotationRadians, Vec3 Scale)
{
    /// <summary>Parses inspector position, rotation, and scale text into a transform request.</summary>
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

    /// <summary>Applies the parsed transform to the named scene object in the session.</summary>
    public bool Apply(
        ComposerSceneSession session,
        int objectId,
        string name,
        bool visible)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.UpdateObject(objectId, name, visible, Position, RotationRadians, Scale);
    }

    /// <summary>Parses a finite number, returning the blank value for empty text.</summary>
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

    /// <summary>Parses a finite number and requires it to be greater than zero.</summary>
    private static double ParsePositive(string? text, string label, double blankValue)
    {
        double value = ParseFinite(text, label, blankValue);
        if (value <= 0.0)
            throw new ArgumentOutOfRangeException(label, $"{label} must be greater than zero.");
        return value;
    }
}
