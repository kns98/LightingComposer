// -----------------------------------------------------------------------------
// File: Scene/MaterialPreset.cs
// Purpose: Common physically based material presets for the editor material library.
// -----------------------------------------------------------------------------

using LightingShowcase.Math3D;

namespace LightingShowcase.SceneGraph;

/// <summary>Named PBR-style material preset that can be applied to selected scene geometry.</summary>
public sealed class MaterialPreset
{
    public string Name { get; }
    public string Category { get; }
    public Material Material { get; }
    public string Summary { get; }

    /// <summary>Creates a material preset with the given category, name, material, and summary.</summary>
    public MaterialPreset(string category, string name, Material material, string summary)
    {
        Category = category;
        Name = name;
        Material = material;
        Summary = summary;
    }

    /// <summary>Returns the preset label as its category and name.</summary>
    public override string ToString() => $"{Category} - {Name}";
}

/// <summary>Shared material preset library exposed by the Composer material editor.</summary>
public static class MaterialPresetLibrary
{
    public static IReadOnlyList<MaterialPreset> Common { get; } = new List<MaterialPreset>
    {
        P("Metals", "Aluminum Brushed", C(0.82, 0.80, 0.76), 1.00, 0.32, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Light brushed metal; bright, moderately soft reflection."),
        P("Metals", "Aluminum Polished", C(0.91, 0.90, 0.86), 1.00, 0.16, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Clean polished aluminum with sharper highlights."),
        P("Metals", "Steel", C(0.63, 0.64, 0.64), 1.00, 0.24, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Neutral engineering steel."),
        P("Metals", "Stainless Steel", C(0.75, 0.75, 0.73), 1.00, 0.18, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Bright stainless steel for appliances and fixtures."),
        P("Metals", "Chrome", C(0.95, 0.95, 0.93), 1.00, 0.06, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Mirror-like plated metal."),
        P("Metals", "Iron", C(0.43, 0.42, 0.40), 1.00, 0.38, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Darker utilitarian iron."),
        P("Metals", "Cast Iron", C(0.18, 0.17, 0.16), 1.00, 0.62, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Rough dark cast metal."),
        P("Metals", "Copper", C(0.96, 0.54, 0.30), 1.00, 0.22, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Warm reflective copper."),
        P("Metals", "Bronze", C(0.73, 0.44, 0.22), 1.00, 0.32, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Brown-gold bronze."),
        P("Metals", "Brass", C(0.95, 0.74, 0.34), 1.00, 0.24, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Yellow metal hardware finish."),
        P("Metals", "Gold", C(1.00, 0.76, 0.34), 1.00, 0.18, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Classic polished gold."),
        P("Metals", "Silver", C(0.91, 0.90, 0.86), 1.00, 0.14, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Bright silver jewelry or trim."),
        P("Paint", "White Matte Paint", C(0.88, 0.86, 0.82), 0.00, 0.82, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Soft interior wall paint."),
        P("Paint", "Black Matte Paint", C(0.01, 0.01, 0.012), 0.00, 0.88, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Deep non-metallic black."),
        P("Paint", "Glossy White Paint", C(0.95, 0.94, 0.90), 0.00, 0.18, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Smooth glossy white coating."),
        P("Paint", "Glossy Black Paint", C(0.005, 0.005, 0.006), 0.00, 0.16, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Piano-black lacquer finish."),
        P("Paint", "Red Automotive Paint", C(0.86, 0.05, 0.03), 0.00, 0.12, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Shiny red car-paint style surface."),
        P("Paint", "Blue Automotive Paint", C(0.03, 0.12, 0.75), 0.00, 0.14, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Shiny blue car-paint style surface."),
        P("Paint", "Rubberized Coating", C(0.025, 0.025, 0.024), 0.00, 0.74, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Soft rough black coating."),
        P("Plastics", "White Plastic", C(0.90, 0.88, 0.84), 0.00, 0.45, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Common molded white plastic."),
        P("Plastics", "Black Plastic", C(0.01, 0.011, 0.012), 0.00, 0.42, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Common molded black plastic."),
        P("Plastics", "Clear Plastic", C(0.80, 0.92, 1.00), 0.00, 0.08, 0.55, 0.38, true, 0.0, C(1, 1, 1), "Transparent acrylic/polycarbonate-like surface."),
        P("Plastics", "Frosted Plastic", C(0.84, 0.92, 1.00), 0.00, 0.55, 0.32, 0.62, true, 0.0, C(1, 1, 1), "Translucent rough plastic diffuser."),
        P("Plastics", "ABS Plastic", C(0.08, 0.08, 0.075), 0.00, 0.52, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Slightly rough consumer-product ABS."),
        P("Glass", "Clear Glass", C(0.90, 0.97, 1.00), 0.00, 0.02, 0.82, 0.24, true, 0.0, C(1, 1, 1), "Low-roughness transparent glass."),
        P("Glass", "Frosted Glass", C(0.86, 0.93, 0.96), 0.00, 0.46, 0.62, 0.42, true, 0.0, C(1, 1, 1), "Rough translucent glass."),
        P("Glass", "Green Bottle Glass", C(0.34, 0.70, 0.48), 0.00, 0.07, 0.70, 0.34, true, 0.0, C(1, 1, 1), "Tinted green glass."),
        P("Glass", "Amber Glass", C(0.85, 0.48, 0.17), 0.00, 0.08, 0.68, 0.36, true, 0.0, C(1, 1, 1), "Warm brown transparent bottle glass."),
        P("Glass", "Mirror", C(0.98, 0.98, 0.96), 1.00, 0.02, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Metal-backed mirror-like surface."),
        P("Stone", "Concrete", C(0.50, 0.49, 0.46), 0.00, 0.86, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Plain grey concrete."),
        P("Stone", "Polished Concrete", C(0.58, 0.57, 0.54), 0.00, 0.34, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Smoother sealed concrete."),
        P("Stone", "Granite", C(0.42, 0.40, 0.38), 0.00, 0.48, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark polished stone base."),
        P("Stone", "Marble White", C(0.86, 0.84, 0.78), 0.00, 0.28, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Polished white marble-like surface."),
        P("Stone", "Slate", C(0.18, 0.20, 0.21), 0.00, 0.78, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark rough slate."),
        P("Stone", "Ceramic Tile", C(0.84, 0.82, 0.76), 0.00, 0.22, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Glossy neutral ceramic."),
        P("Organic", "Oak Wood", C(0.72, 0.49, 0.25), 0.00, 0.58, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Light warm wood tone."),
        P("Organic", "Walnut Wood", C(0.34, 0.20, 0.10), 0.00, 0.46, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark furniture wood tone."),
        P("Organic", "Pine Wood", C(0.78, 0.62, 0.36), 0.00, 0.64, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Soft pale wood tone."),
        P("Organic", "Leather Brown", C(0.30, 0.13, 0.055), 0.00, 0.42, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Brown semi-gloss leather."),
        P("Organic", "Leather Black", C(0.015, 0.012, 0.010), 0.00, 0.38, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Black semi-gloss leather."),
        P("Organic", "Cotton Fabric", C(0.72, 0.68, 0.62), 0.00, 0.92, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Very rough woven-cloth look."),
        P("Organic", "Denim Fabric", C(0.07, 0.16, 0.34), 0.00, 0.88, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Matte blue denim."),
        P("Organic", "Velvet", C(0.34, 0.02, 0.11), 0.00, 0.96, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Deep rough velvet approximation."),
        P("Organic", "Skin Warm", C(0.86, 0.55, 0.38), 0.00, 0.68, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Warm matte skin-tone material."),
        P("Organic", "Leaves", C(0.09, 0.42, 0.12), 0.00, 0.72, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Matte green plant foliage."),
        P("Liquids", "Water", C(0.55, 0.82, 1.00), 0.00, 0.02, 0.82, 0.24, true, 0.0, C(1, 1, 1), "Clear blue-tinted water."),
        P("Emission", "Warm LED", C(1.00, 0.78, 0.42), 0.00, 0.24, 0.0, 1.0, false, 3.0, C(1.00, 0.72, 0.36), "Warm glowing LED surface."),
        P("Emission", "Cool LED", C(0.64, 0.82, 1.00), 0.00, 0.20, 0.0, 1.0, false, 2.6, C(0.70, 0.86, 1.00), "Cool blue-white glowing surface."),
        P("Emission", "Neon Red", C(1.00, 0.05, 0.04), 0.00, 0.16, 0.0, 1.0, false, 4.2, C(1.00, 0.10, 0.06), "Strong red emissive sign material."),
        P("Emission", "Neon Blue", C(0.05, 0.24, 1.00), 0.00, 0.16, 0.0, 1.0, false, 4.0, C(0.10, 0.30, 1.00), "Strong blue emissive sign material."),
        P("Ceramic", "White Porcelain", C(0.93, 0.92, 0.89), 0.00, 0.10, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Glossy white porcelain."),
        P("Ceramic", "Bone China", C(0.96, 0.93, 0.85), 0.00, 0.14, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Warm ivory bone china."),
        P("Ceramic", "Glazed Stoneware", C(0.45, 0.55, 0.62), 0.00, 0.20, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Blue-grey glazed stoneware."),
        P("Ceramic", "Matte Ceramic Black", C(0.03, 0.03, 0.035), 0.00, 0.70, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Unglazed matte black ceramic."),
        P("Ceramic", "Ceramic Teal Glaze", C(0.10, 0.45, 0.48), 0.00, 0.12, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Vibrant teal glossy glaze."),
        P("Ceramic", "Speckled Ceramic", C(0.80, 0.74, 0.64), 0.00, 0.35, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Sandy speckled oatmeal glaze."),
        P("Gems", "Diamond", C(0.92, 0.96, 1.00), 0.00, 0.02, 0.85, 0.20, true, 0.0, C(1, 1, 1), "Brilliant clear diamond."),
        P("Gems", "Ruby", C(0.75, 0.05, 0.12), 0.00, 0.05, 0.55, 0.45, true, 0.0, C(1, 1, 1), "Deep red transparent ruby."),
        P("Gems", "Sapphire", C(0.08, 0.18, 0.72), 0.00, 0.05, 0.55, 0.45, true, 0.0, C(1, 1, 1), "Deep blue transparent sapphire."),
        P("Gems", "Emerald", C(0.05, 0.55, 0.28), 0.00, 0.06, 0.55, 0.45, true, 0.0, C(1, 1, 1), "Rich green transparent emerald."),
        P("Gems", "Pearl", C(0.93, 0.90, 0.86), 0.00, 0.22, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Lustrous white pearl."),
        P("Paper", "White Paper", C(0.94, 0.93, 0.90), 0.00, 0.90, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Plain matte white paper."),
        P("Paper", "Kraft Cardboard", C(0.66, 0.48, 0.28), 0.00, 0.88, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Brown kraft cardboard."),
        P("Paper", "Newsprint", C(0.82, 0.79, 0.70), 0.00, 0.92, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Grey-beige newsprint."),
        P("Paper", "Parchment", C(0.88, 0.80, 0.62), 0.00, 0.75, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Aged yellowed parchment."),
        P("Food", "Apple Skin", C(0.72, 0.10, 0.08), 0.00, 0.28, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Glossy red apple skin."),
        P("Food", "Bread Crust", C(0.62, 0.38, 0.16), 0.00, 0.80, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Rough golden bread crust."),
        P("Food", "Dark Chocolate", C(0.16, 0.07, 0.04), 0.00, 0.32, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Smooth dark chocolate."),
        P("Food", "Cheese", C(0.95, 0.78, 0.30), 0.00, 0.55, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Waxy yellow cheese."),
        P("Food", "Coffee", C(0.10, 0.05, 0.02), 0.00, 0.15, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark glossy coffee liquid."),
        P("Food", "Milk", C(0.97, 0.96, 0.93), 0.00, 0.40, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Creamy white milk."),
        P("Nature", "Tree Bark", C(0.25, 0.16, 0.10), 0.00, 0.95, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Rough dark tree bark."),
        P("Nature", "Moss", C(0.20, 0.38, 0.10), 0.00, 0.95, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Soft green moss."),
        P("Nature", "Sand", C(0.85, 0.74, 0.52), 0.00, 0.92, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dry pale sand."),
        P("Nature", "Snow", C(0.93, 0.94, 0.97), 0.00, 0.60, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Bright fresh snow."),
        P("Nature", "Soil", C(0.18, 0.11, 0.06), 0.00, 0.98, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark damp soil."),
        P("Nature", "Grass Blade", C(0.22, 0.50, 0.12), 0.00, 0.80, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Fresh green grass."),
        P("Nature", "Clay", C(0.58, 0.32, 0.20), 0.00, 0.85, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Reddish modeling clay."),
        P("Nature", "Ice", C(0.75, 0.88, 0.96), 0.00, 0.08, 0.70, 0.35, true, 0.0, C(1, 1, 1), "Clear blue-tinted ice."),
        P("Liquids", "Olive Oil", C(0.55, 0.52, 0.10), 0.00, 0.05, 0.45, 0.55, true, 0.0, C(1, 1, 1), "Golden translucent olive oil."),
        P("Liquids", "Honey", C(0.85, 0.50, 0.08), 0.00, 0.10, 0.40, 0.60, true, 0.0, C(1, 1, 1), "Thick amber honey."),
        P("Liquids", "Orange Juice", C(0.98, 0.55, 0.10), 0.00, 0.25, 0.25, 0.75, true, 0.0, C(1, 1, 1), "Cloudy orange juice."),
        P("Stone", "Concrete Dark", C(0.32, 0.31, 0.29), 0.00, 0.88, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Weathered dark concrete."),
        P("Stone", "Concrete Light", C(0.72, 0.71, 0.68), 0.00, 0.82, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Pale architectural concrete."),
        P("Stone", "Terracotta", C(0.68, 0.32, 0.18), 0.00, 0.80, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Fired orange clay terracotta."),
        P("Stone", "Basalt", C(0.10, 0.10, 0.11), 0.00, 0.70, 0.0, 1.0, false, 0.0, C(1, 1, 1), "Dark volcanic basalt."),
    };

    /// <summary>Creates a color vector from the given red, green, and blue channels.</summary>
    private static Vec3 C(double r, double g, double b) => new(r, g, b);

    /// <summary>Builds a material preset entry from the given PBR values.</summary>
    private static MaterialPreset P(string category, string name, Vec3 color, double metallic, double roughness, double transmission, double alpha, bool alphaBlend, double emission, Vec3 emissionColor, string summary)
    {
        return new MaterialPreset(category, name, new Material(color, emission, null, null, emissionColor, null, alpha, alphaBlend, metallic, roughness, transmission), summary);
    }
}
