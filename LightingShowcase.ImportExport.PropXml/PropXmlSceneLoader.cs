/*
 * Importing PROPXML is a translation problem, not a file-copy operation. The code parses the external
 * representation, resolves indices/resources/transforms, and creates Composer triangles, object groups, materials,
 * and textures in the coordinate and ownership conventions expected by the scene layer.
 */
using System.IO;
using System.Globalization;
using System.Xml.Linq;
using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

// PropXmlSceneLoader owns parsing and translation from its external file format into Composer scene objects;
// parser-specific intermediate state stays here instead of leaking into the renderer-neutral scene model.
/// <summary>Loads the native .prop.xml scene format.</summary>
public static class PropXmlSceneLoader
{
    public static void LoadIntoScene(Scene scene, string filePath)
    {
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A file path is required.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("Prop XML file was not found.", filePath);

        XDocument document = XDocument.Load(filePath);
        XElement root = document.Root ?? throw new InvalidDataException("XML document has no root element.");
        if (!string.Equals(root.Name.LocalName, "PropScene", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Expected a PropScene XML document.");

        scene.Clear();

        XElement? lights = root.Element("Lights");
        if (lights != null)
        {
            foreach (XElement lightElement in lights.Elements("Light"))
                scene.Lights.Add(ReadLight(lightElement));
        }

        XElement? objects = root.Element("Objects");
        if (objects != null)
        {
            foreach (XElement objectElement in objects.Elements("Object"))
                ReadObject(scene, objectElement, parent: null, sceneFilePath: filePath);
        }

        scene.RebuildWorldGeometry();
    }

    // ReadLight reads light from the external stream/document, advancing through the format in the order required
    // to resolve references and produce valid internal data.
    /// <summary>Reads light from user input or serialized data.</summary>
    private static SceneLight ReadLight(XElement element)
    {
        string id = (string?)element.Attribute("id") ?? "light";
        Vec3 position = ReadVec(element, "position", Vec3.Zero);
        Vec3 color = ReadVec(element, "color", new Vec3(1, 1, 1));
        double intensity = ReadDouble(element.Attribute("intensity"), 1.0);
        bool enabled = ReadBool(element.Attribute("enabled"), true);
        SceneLightKind kind = ReadLightKind(element.Attribute("kind"));
        Vec3 direction = ReadVec(element, "direction", new Vec3(0.0, 0.0, -1.0));
        double range = ReadDouble(element.Attribute("range"), 0.0);
        double innerConeAngle = ReadDouble(element.Attribute("innerConeAngle"), 0.0);
        double outerConeAngle = ReadDouble(element.Attribute("outerConeAngle"), Math.PI / 4.0);
        return new SceneLight(id, position, color, intensity, enabled, kind, direction, range, innerConeAngle, outerConeAngle);
    }


    // ReadLightKind reads light kind from the external stream/document, advancing through the format in the order
    // required to resolve references and produce valid internal data.
    private static SceneLightKind ReadLightKind(XAttribute? attribute)
    {
        string value = ((string?)attribute ?? "point").Trim();
        return value.Equals("directional", StringComparison.OrdinalIgnoreCase)
            ? SceneLightKind.Directional
            : value.Equals("spot", StringComparison.OrdinalIgnoreCase)
                ? SceneLightKind.Spot
                : SceneLightKind.Point;
    }

    // ReadObject reads object from the external stream/document, advancing through the format in the order required
    // to resolve references and produce valid internal data.
    /// <summary>Reads object from user input or serialized data.</summary>
    private static SceneObjectGroup ReadObject(Scene scene, XElement element, SceneObjectGroup? parent, string sceneFilePath)
    {
        string name = (string?)element.Attribute("name") ?? "Object";
        bool selectable = ReadBool(element.Attribute("selectable"), true);
        bool visible = ReadBool(element.Attribute("visible"), true);
        SceneObjectGroup group = scene.AddImportedGroup(name, selectable);
        group.Visible = visible;
        group.PrimitiveKind = (string?)element.Attribute("primitiveKind");
        group.PrimitiveSourceName = (string?)element.Attribute("primitiveSource");
        ReadPrimitiveParameters(element.Element("PrimitiveParameters"), group);
        if (parent != null)
        {
            scene.ObjectGroups.Remove(group);
            parent.AddChild(group);
        }

        XElement? transformElement = element.Element("Transform");
        Material? colorOverride = ReadOptionalMaterial(element.Element("ColorOverride"), sceneFilePath);

        XElement? triangles = element.Element("Triangles");
        if (triangles != null)
        {
            foreach (XElement triangleElement in triangles.Elements("Triangle"))
            {
                Vec3 a = ReadVec(triangleElement, "a", Vec3.Zero);
                Vec3 b = ReadVec(triangleElement, "b", Vec3.Zero);
                Vec3 c = ReadVec(triangleElement, "c", Vec3.Zero);
                Vec2 uvA = ReadVec2(triangleElement, "uvA", new Vec2(0, 0));
                Vec2 uvB = ReadVec2(triangleElement, "uvB", new Vec2(1, 0));
                Vec2 uvC = ReadVec2(triangleElement, "uvC", new Vec2(0, 1));
                Material material = colorOverride ?? ReadMaterial(triangleElement.Element("Material"), sceneFilePath);
                group.AddTriangle(a, b, c, uvA, uvB, uvC, material);
            }
        }

        XElement? children = element.Element("Children");
        if (children != null)
        {
            foreach (XElement childElement in children.Elements("Object"))
                ReadObject(scene, childElement, group, sceneFilePath);
        }

        group.RecalculatePivot();

        if (transformElement != null)
        {
            group.Position = ReadVec(transformElement, "position", Vec3.Zero);
            group.Rotation = ReadVec(transformElement, "rotationRadians", Vec3.Zero);
            group.Scale = ReadVec(transformElement, "scale", new Vec3(1, 1, 1));

            // Parametric objects keep transform metadata so a saved prop can reopen as
            // an editable primitive with authored parameters. Older mesh-only props
            // keep the previous behavior and bake transform into triangles.
            if (!group.HasParametricPrimitive)
                group.BakeCurrentTransform();
        }

        return group;
    }


    // ReadPrimitiveParameters reads primitive parameters from the external stream/document, advancing through the
    // format in the order required to resolve references and produce valid internal data.
    private static void ReadPrimitiveParameters(XElement? element, SceneObjectGroup group)
    {
        if (element == null) return;
        foreach (XElement parameter in element.Elements("Parameter"))
        {
            string? name = (string?)parameter.Attribute("name");
            if (string.IsNullOrWhiteSpace(name))
                continue;
            group.PrimitiveParameters[name] = ReadDouble(parameter.Attribute("value"), 0.0);
        }
    }

    // ReadMaterial reads material from the external stream/document, advancing through the format in the order
    // required to resolve references and produce valid internal data.
    /// <summary>Reads material from user input or serialized data.</summary>
    private static Material ReadMaterial(XElement? element, string sceneFilePath)
    {
        if (element == null) return new Material(new Vec3(0.78, 0.76, 0.72));
        return ReadMaterialAttributes(element, sceneFilePath);
    }

    // ReadOptionalMaterial reads optional material from the external stream/document, advancing through the format
    // in the order required to resolve references and produce valid internal data.
    /// <summary>Reads optional material from user input or serialized data.</summary>
    private static Material? ReadOptionalMaterial(XElement? element, string sceneFilePath) =>
        element == null ? null : ReadMaterialAttributes(element, sceneFilePath);

    // ReadMaterialAttributes reads material attributes from the external stream/document, advancing through the
    // format in the order required to resolve references and produce valid internal data.
    /// <summary>Reads material attributes from user input or serialized data.</summary>
    private static Material ReadMaterialAttributes(XElement element, string sceneFilePath)
    {
        Vec3 color = new(
            ReadDouble(element.Attribute("colorR"), 0.78),
            ReadDouble(element.Attribute("colorG"), 0.76),
            ReadDouble(element.Attribute("colorB"), 0.72));
        double emission = ReadDouble(element.Attribute("emission"), 0.0);
        string? lightId = (string?)element.Attribute("lightId");
        if (string.IsNullOrWhiteSpace(lightId)) lightId = null;
        TextureMap? texture = ReadTexture(element, sceneFilePath);
        return new Material(color, emission, lightId, texture);
    }

    // ReadTexture reads texture from the external stream/document, advancing through the format in the order
    // required to resolve references and produce valid internal data.
    private static TextureMap? ReadTexture(XElement element, string sceneFilePath)
    {
        string? kind = (string?)element.Attribute("textureKind");
        if (string.Equals(kind, "checker", StringComparison.OrdinalIgnoreCase))
        {
            int width = Math.Max(2, ReadInt(element.Attribute("textureWidth"), 160));
            int height = Math.Max(2, ReadInt(element.Attribute("textureHeight"), 96));
            string name = (string?)element.Attribute("textureName") ?? "Built-in checker";
            return TextureMap.CreateChecker(name, width, height);
        }

        string? path = (string?)element.Attribute("texturePath");
        if (string.IsNullOrWhiteSpace(path)) return null;

        string candidate = path;
        if (!Path.IsPathRooted(candidate))
            candidate = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(sceneFilePath)) ?? Environment.CurrentDirectory, candidate);

        try
        {
            return File.Exists(candidate) ? TextureMap.FromFile(candidate) : null;
        }
        catch
        {
            return null;
        }
    }

    // ReadVec reads vec from the external stream/document, advancing through the format in the order required to
    // resolve references and produce valid internal data.
    /// <summary>Reads vec from user input or serialized data.</summary>
    private static Vec3 ReadVec(XElement element, string prefix, Vec3 fallback) => new(
        ReadDouble(element.Attribute(prefix + "X"), fallback.X),
        ReadDouble(element.Attribute(prefix + "Y"), fallback.Y),
        ReadDouble(element.Attribute(prefix + "Z"), fallback.Z));


    // ReadVec2 reads vec2 from the external stream/document, advancing through the format in the order required to
    // resolve references and produce valid internal data.
    /// <summary>Reads UV texture coordinates from serialized triangle attributes.</summary>
    private static Vec2 ReadVec2(XElement element, string prefix, Vec2 fallback) => new(
        ReadDouble(element.Attribute(prefix + "U"), fallback.U),
        ReadDouble(element.Attribute(prefix + "V"), fallback.V));

    // ReadDouble reads double from the external stream/document, advancing through the format in the order required
    // to resolve references and produce valid internal data.
    /// <summary>Reads double from user input or serialized data.</summary>
    private static double ReadDouble(XAttribute? attribute, double fallback)
    {
        if (attribute == null) return fallback;
        // Native scene XML is deliberately locale-independent: numbers are parsed with invariant culture so a saved
        // file means the same thing on systems with different decimal separators.
        return double.TryParse(attribute.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : fallback;
    }


    // ReadInt reads int from the external stream/document, advancing through the format in the order required to
    // resolve references and produce valid internal data.
    private static int ReadInt(XAttribute? attribute, int fallback)
    {
        if (attribute == null) return fallback;
        // Native scene XML is deliberately locale-independent: numbers are parsed with invariant culture so a saved
        // file means the same thing on systems with different decimal separators.
        return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
    }

    // ReadBool reads bool from the external stream/document, advancing through the format in the order required to
    // resolve references and produce valid internal data.
    /// <summary>Reads bool from user input or serialized data.</summary>
    private static bool ReadBool(XAttribute? attribute, bool fallback)
    {
        if (attribute == null) return fallback;
        return bool.TryParse(attribute.Value, out bool value) ? value : fallback;
    }
}
