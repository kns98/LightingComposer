/*
 * Exporting PROPXML walks the internal scene and rebuilds the format’s object/index/material/resource structures.
 * The implementation must keep indices and references self-consistent and must make deliberate choices about
 * features that do not map one-to-one between Composer and PROPXML.
 */
using System.IO;
using System.Globalization;
using System.Xml.Linq;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

// PropXmlSceneSaver owns translation from Composer scene state into its external file format, including the
// indexing/resource relationships required for another program to reconstruct the exported model.
/// <summary>Saves the native .prop.xml scene format.</summary>
public static class PropXmlSceneSaver
{
    public static void Save(Scene scene, string filePath, SceneSaveOptions? options = null)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A save path is required.", nameof(filePath));

        XDocument document = new(
            new XElement("PropScene",
                new XAttribute("version", "1.4"),
                new XAttribute("createdUtc", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
                new XElement("Description", scene.Description),
                new XElement("Lights", scene.Lights.Select(ToLightElement)),
                new XElement("Objects", scene.ObjectGroups.Select(group => ToObjectElement(group, options?.TexturePathResolver)))));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory);
        document.Save(filePath);
    }
    private static XElement ToLightElement(SceneLight light) =>
        new("Light",
            new XAttribute("id", light.Id),
            VecAttributes("position", light.Position),
            VecAttributes("color", light.Color),
            // Format formats a numeric editor value using a stable concise representation so refreshing a text box
            // does not introduce excessive decimal noise.
            new XAttribute("intensity", Format(light.Intensity)),
            new XAttribute("enabled", light.Enabled),
            new XAttribute("kind", light.Kind.ToString().ToLowerInvariant()),
            VecAttributes("direction", light.Direction),
            new XAttribute("range", Format(light.Range)),
            new XAttribute("innerConeAngle", Format(light.InnerConeAngle)),
            new XAttribute("outerConeAngle", Format(light.OuterConeAngle)));
    private static XElement ToObjectElement(SceneObjectGroup group, Func<TextureMap, string?>? texturePathResolver) =>
        new("Object",
            new XAttribute("id", group.Id),
            new XAttribute("name", group.Name),
            new XAttribute("selectable", group.IsSelectable),
            new XAttribute("visible", group.Visible),
            string.IsNullOrWhiteSpace(group.PrimitiveKind) ? null : new XAttribute("primitiveKind", group.PrimitiveKind),
            string.IsNullOrWhiteSpace(group.PrimitiveSourceName) ? null : new XAttribute("primitiveSource", group.PrimitiveSourceName),
            new XElement("Transform",
                VecAttributes("position", group.Position),
                VecAttributes("rotationRadians", group.Rotation),
                VecAttributes("scale", group.Scale)),
            group.ColorOverride == null ? null : new XElement("ColorOverride", MaterialAttributes(group.ColorOverride, texturePathResolver)),
            group.PrimitiveParameters.Count == 0 ? null : new XElement("PrimitiveParameters",
                group.PrimitiveParameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new XElement("Parameter",
                        new XAttribute("name", p.Key),
                        new XAttribute("value", Format(p.Value))))),
            new XElement("Triangles", group.LocalTriangles.Select(triangle => ToTriangleElement(triangle, texturePathResolver))),
            group.Children.Count == 0 ? null : new XElement("Children", group.Children.Select(child => ToObjectElement(child, texturePathResolver))));
    private static XElement ToTriangleElement(Triangle triangle, Func<TextureMap, string?>? texturePathResolver) =>
        new("Triangle",
            VecAttributes("a", triangle.A),
            VecAttributes("b", triangle.B),
            VecAttributes("c", triangle.C),
            Vec2Attributes("uvA", triangle.UvA),
            Vec2Attributes("uvB", triangle.UvB),
            Vec2Attributes("uvC", triangle.UvC),
            new XElement("Material", MaterialAttributes(triangle.Material, texturePathResolver)));
    internal static object[] VecAttributes(string prefix, Vec3 value) => new object[]
    {
        new XAttribute(prefix + "X", Format(value.X)),
        new XAttribute(prefix + "Y", Format(value.Y)),
        new XAttribute(prefix + "Z", Format(value.Z))
    };


    /// <summary>Writes two-dimensional vector attributes for UV texture coordinates.</summary>
    internal static object[] Vec2Attributes(string prefix, Vec2 value) => new object[]
    {
        new XAttribute(prefix + "U", Format(value.U)),
        new XAttribute(prefix + "V", Format(value.V))
    };
    internal static object[] MaterialAttributes(Material material, Func<TextureMap, string?>? texturePathResolver = null)
    {
        List<object> attributes = new()
        {
            new XAttribute("colorR", Format(material.Color.X)),
            new XAttribute("colorG", Format(material.Color.Y)),
            new XAttribute("colorB", Format(material.Color.Z)),
            new XAttribute("emission", Format(material.Emission)),
            new XAttribute("lightId", material.LightId ?? string.Empty)
        };

        if (material.Texture != null)
        {
            attributes.Add(new XAttribute("textureName", material.Texture.Name));
            string? texturePath = texturePathResolver?.Invoke(material.Texture) ?? material.Texture.SourcePath;
            if (!string.IsNullOrWhiteSpace(texturePath))
                attributes.Add(new XAttribute("texturePath", texturePath.Replace('\\', '/')));
            if (material.Texture.IsBuiltInChecker)
            {
                attributes.Add(new XAttribute("textureKind", "checker"));
                attributes.Add(new XAttribute("textureWidth", material.Texture.Width));
                attributes.Add(new XAttribute("textureHeight", material.Texture.Height));
            }
        }

        return attributes.ToArray();
    }
    internal static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
}
