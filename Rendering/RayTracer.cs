// -----------------------------------------------------------------------------
// File: Rendering/RayTracer.cs
// Purpose: Ray tracing engine.
//
// Finds intersections, shades surfaces, samples lights, handles texture lookup,
// shadows, alpha/transmission blending, and camera ray generation.
// -----------------------------------------------------------------------------

// Import camera basis types
using LightingShowcase.CameraSystem;
// Import lighting state types
using LightingShowcase.Lighting;
// Import scene graph types
using LightingShowcase.SceneGraph;
// Import vector math types
using LightingShowcase.Math3D;

// Declare rendering namespace
namespace LightingShowcase.Rendering;

/// <summary>Core ray tracer responsible for ray/scene intersection and surface lighting.</summary>
// Declare core ray tracer class
public sealed class RayTracer
// Begin RayTracer class
{
    // Limit transparency recursion depth
    private const int MaxTransparencyDepth = 4;
    // Cache pi constant
    private const double Pi = Math.PI;
    // Store traced scene reference
    private readonly Scene scene;
    // World units per pixel at unit distance for wireframe lines
    public double WireframePixelWorld { get; set; } = 0.002;
    // Background gradient endpoints for missed rays
    public Vec3 BackgroundTop { get; set; } = RenderSettings.DefaultBackgroundTop;
    public Vec3 BackgroundBottom { get; set; } = RenderSettings.DefaultBackgroundBottom;
    // Bright color for visible wireframe lines
    private static readonly Vec3 WireframeLineColor = new Vec3(0.85, 0.92, 1.0);
    // Dim color for dashed hidden wireframe lines
    private static readonly Vec3 WireframeHiddenColor = new Vec3(0.30, 0.38, 0.52);

    /// <summary>Layered specular, sheen, anisotropy, and iridescence inputs for one surface hit.</summary>
    // Declare layered shading inputs record
    private sealed record SurfaceShading(
        // Declare dielectric base reflectance field
        Vec3 DielectricBaseF0,
        // Declare specular weight field
        double SpecularWeight,
        // Declare default specular fast-path flag
        bool IsDefaultSpecular,
        // Declare sheen albedo field
        Vec3 SheenColor,
        // Declare sheen roughness field
        double SheenRoughness,
        // Declare sheen albedo maximum field
        double SheenMax,
        // Declare anisotropy strength field
        double Anisotropy,
        // Declare anisotropy tangent field
        Vec3 AnisoTangent,
        // Declare anisotropy bitangent field
        Vec3 AnisoBitangent,
        // Declare iridescence strength field
        double Iridescence,
        // Declare thin-film IOR field
        double IridescenceIor,
        // Declare thin-film thickness field
        double IridescenceThickness,
        // Declare clearcoat layer weight field
        double Clearcoat,
        // Declare clearcoat roughness field
        double ClearcoatRoughness);

    /// <summary>Initializes a new ray tracer for the given scene.</summary>
    // Construct tracer for scene
    public RayTracer(Scene scene, LightingState lighting)
    // Begin RayTracer constructor
    {
        // Assign scene field
        this.scene = scene;
    // End RayTracer constructor
    }

    /// <summary>Traces a ray through the scene and returns its shaded color.</summary>
    // Trace ray with defaults
    public Vec3 Trace(Ray ray) => Trace(ray, 0);

    /// <summary>Traces a ray using an explicit render/debug mode.</summary>
    // Trace ray in render mode
    public Vec3 Trace(Ray ray, RenderMode mode)
    // Begin Trace overload
    {
        // Dispatch lit wireframe or debug tracing
        return mode switch
        {
            // Trace direct lighting ray
            RenderMode.Lit => Trace(ray, 0),
            // Trace wireframe hidden-line view
            RenderMode.Wireframe => TraceWireframe(ray),
            // Trace remaining debug views
            _ => TraceDebug(ray, mode)
        };
    // End Trace overload
    }


    /// <summary>Traces a ray with optional indirect path bounces. A bounce count of zero is equivalent to the existing direct raytracer.</summary>
    // Trace ray with indirect bounces
    public Vec3 TracePath(Ray ray, int bounceCount, int pixelX, int pixelY, int sampleIndex)
    // Begin TracePath
    {
        // Fall back without bounces
        if (bounceCount <= 0)
            // Trace single direct ray
            return Trace(ray, 0);

        // Initialize accumulated radiance
        Vec3 radiance = Vec3.Zero;
        // Initialize path throughput
        Vec3 throughput = new(1.0, 1.0, 1.0);
        // Start from camera ray
        Ray currentRay = ray;
        // Seed per-pixel random generator
        uint rng = Seed(pixelX, pixelY, sampleIndex);

        // Loop over path bounces
        for (int bounce = 0; bounce <= bounceCount; bounce++)
        // Begin bounce loop
        {
            // Intersect ray with scene
            Hit? hit = scene.Intersect(currentRay);
            // Handle missed ray
            if (hit == null)
            // Begin miss branch
            {
                // Add background contribution
                radiance += throughput.Multiply(Background(currentRay));
                // Exit bounce loop
                break;
            // End miss branch
            }

            // Sample surface albedo
            Vec3 surfaceColor = hit.Material.SampleLinear(hit.TextureU, hit.TextureV);
            // Sample surface emission
            Vec3 emission = hit.Material.SampleEmissionLinear(hit.TextureU, hit.TextureV);
            // Check for emissive surface
            if (emission.X > 0.0 || emission.Y > 0.0 || emission.Z > 0.0)
                // Add emissive contribution
                radiance += throughput.Multiply(emission);

            // Preserve the existing local/direct lighting look at every visible hit,
            // then add stochastic indirect transport through the selected bounces.
            // Compute direct lighting minus emission
            Vec3 direct = ShadeHit(hit, surfaceColor, currentRay.Direction) - emission;
            // Add direct lighting contribution
            radiance += throughput.Multiply(direct);

            // Check final bounce
            if (bounce == bounceCount)
                // Stop after last bounce
                break;

            // Fetch perturbed surface normal
            Vec3 normal = ApplyNormalMap(hit).Normalize();
            // Check normal facing
            if (normal.Dot(currentRay.Direction) > 0.0)
                // Flip normal toward ray
                normal = normal * -1.0;

            // Sample metalness and roughness
            (double metallic, double roughness) = hit.Material.SampleMetallicRoughness(hit.TextureU, hit.TextureV);
            // Derive specular bounce probability
            double specularProbability = Math.Clamp(metallic + (1.0 - roughness) * 0.18, 0.05, 0.9);
            // Roll bounce type
            double chooseSpecular = Next01(ref rng);

            // Declare bounce direction
            Vec3 bounceDirection;
            // Declare bounce weight
            Vec3 bounceWeight;
            // Branch on specular bounce
            if (chooseSpecular < specularProbability)
            // Begin specular branch
            {
                // Mirror ray about normal
                Vec3 reflected = Reflect(currentRay.Direction, normal).Normalize();
                // Jitter reflection with diffuse sample
                Vec3 diffuseAroundReflection = CosineHemisphere(ref rng, reflected);
                // Blend reflection by roughness
                bounceDirection = Vec3.Lerp(reflected, diffuseAroundReflection, Math.Clamp(roughness * roughness, 0.0, 1.0)).Normalize();
                // Compute base reflectance
                Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
                // Weight specular bounce
                bounceWeight = f0 / specularProbability;
            // End specular branch
            }
            // Handle diffuse bounce
            else
            // Begin diffuse branch
            {
                // Sample cosine hemisphere direction
                bounceDirection = CosineHemisphere(ref rng, normal);
                // Weight diffuse bounce
                bounceWeight = surfaceColor * ((1.0 - metallic) / Math.Max(1e-6, 1.0 - specularProbability));
            // End diffuse branch
            }

            // Accumulate path throughput
            throughput = throughput.Multiply(bounceWeight);
            // Find brightest throughput channel
            double maxChannel = Math.Max(throughput.X, Math.Max(throughput.Y, throughput.Z));
            // Check throughput cutoff
            if (maxChannel < 0.002)
                // Terminate dim path
                break;

            // Spawn offset bounce ray
            currentRay = new Ray(hit.Point + bounceDirection * 0.003, bounceDirection);
        // End bounce loop
        }

        // Return accumulated radiance
        return radiance;
    // End TracePath
    }

    /// <summary>Traces one ray with transparency blending up to the depth limit.</summary>
    // Trace ray with transparency depth
    private Vec3 Trace(Ray ray, int depth)
    // Begin Trace
    {
        // Intersect ray with scene
        Hit? hit = scene.Intersect(ray);
        // Return background on miss
        if (hit == null) return Background(ray);

        // Sample surface albedo
        Vec3 surfaceColor = hit.Material.SampleLinear(hit.TextureU, hit.TextureV);
        // Sample surface alpha
        double alpha = hit.Material.SampleAlpha(hit.TextureU, hit.TextureV);
        // Sample transmission factor
        double transmission = hit.Material.SampleTransmission(hit.TextureU, hit.TextureV);
        // Combine alpha and transmission
        double visibleOpacity = Math.Clamp(alpha * (1.0 - transmission * 0.72), 0.0, 1.0);

        // Shade surface hit
        Vec3 shaded = ShadeHit(hit, surfaceColor, ray.Direction);

        // glTF transmission is true refractive transport.  This compact renderer
        // uses a practical approximation: blend the shaded glass/surface with the
        // color seen straight through it.  It makes lamp shades and glass samples
        // read correctly without turning the editor into a full path tracer.
        // Check transparency blend case
        if ((hit.Material.AlphaBlend || transmission > 0.0 || alpha < 0.999) && visibleOpacity < 0.995 && depth < MaxTransparencyDepth)
        // Begin transparency branch
        {
            // Build ray through surface
            Ray throughRay = new(hit.Point + ray.Direction * 0.004, ray.Direction);
            // Trace transmitted ray
            Vec3 through = Trace(throughRay, depth + 1);
            // Derive glass tint strength
            double tintStrength = Math.Clamp(transmission * 0.55, 0.0, 0.75);
            // Tint transmitted color
            Vec3 tintedThrough = Vec3.Lerp(through, through.Multiply(surfaceColor), tintStrength);
            // Check volumetric absorption
            if (transmission > 0.001 && hit.Material.Thickness > 0.0 && hit.Material.AttenuationDistance > 0.0)
                // Attenuate transmitted color by volume
                tintedThrough = tintedThrough.Multiply(VolumeAttenuation(hit, ray.Direction));
            // Blend behind and shaded colors
            return Vec3.Lerp(tintedThrough, shaded, visibleOpacity);
        // End transparency branch
        }

        // Return opaque shaded color
        return shaded;
    // End Trace
    }


    /// <summary>Reflects a direction about a surface normal.</summary>
    // Reflect direction about normal
    private static Vec3 Reflect(Vec3 direction, Vec3 normal)
    // Begin Reflect
    {
        // Apply reflection formula
        return direction - normal * (2.0 * direction.Dot(normal));
    // End Reflect
    }

    /// <summary>Samples a cosine-weighted hemisphere direction about a normal.</summary>
    // Sample cosine-weighted hemisphere direction
    private static Vec3 CosineHemisphere(ref uint state, Vec3 normal)
    // Begin CosineHemisphere
    {
        // Draw first random number
        double r1 = Next01(ref state);
        // Draw second random number
        double r2 = Next01(ref state);
        // Map sample to azimuth
        double phi = 2.0 * Math.PI * r1;
        // Map sample to disk radius
        double radius = Math.Sqrt(r2);
        // Compute tangent component
        double x = Math.Cos(phi) * radius;
        // Compute bitangent component
        double y = Math.Sin(phi) * radius;
        // Compute normal component
        double z = Math.Sqrt(Math.Max(0.0, 1.0 - r2));

        // Normalize input normal
        Vec3 n = normal.Normalize();
        // Pick stable tangent basis
        Vec3 tangent = Math.Abs(n.Y) < 0.9
            // Build tangent from up axis
            ? new Vec3(0, 1, 0).Cross(n).Normalize()
            // Build tangent from X axis
            : new Vec3(1, 0, 0).Cross(n).Normalize();
        // Build orthogonal bitangent
        Vec3 bitangent = n.Cross(tangent).Normalize();
        // Transform sample to world
        return (tangent * x + bitangent * y + n * z).Normalize();
    // End CosineHemisphere
    }

    /// <summary>Hashes pixel coordinates and sample index into a random seed.</summary>
    // Hash pixel into RNG seed
    private static uint Seed(int x, int y, int sampleIndex)
    // Begin Seed
    {
        // Allow hash overflow
        unchecked
        // Begin unchecked block
        {
            // Start FNV offset basis
            uint hash = 2166136261u;
            // Mix pixel X
            hash = (hash ^ (uint)x) * 16777619u;
            // Mix pixel Y
            hash = (hash ^ (uint)y) * 16777619u;
            // Mix sample index
            hash = (hash ^ (uint)sampleIndex) * 16777619u;
            // Avoid zero seed
            return hash == 0 ? 1u : hash;
        // End unchecked block
        }
    // End Seed
    }

    /// <summary>Advances the xor-shift state and returns a value in [0, 1).</summary>
    // Generate unit random value
    private static double Next01(ref uint state)
    // Begin Next01
    {
        // Allow shift overflow
        unchecked
        // Begin unchecked block
        {
            // Xorshift left thirteen
            state ^= state << 13;
            // Xorshift right seventeen
            state ^= state >> 17;
            // Xorshift left five
            state ^= state << 5;
            // Scale state to unit interval
            return (state & 0x00FFFFFFu) / 16777216.0;
        // End unchecked block
        }
    // End Next01
    }

    /// <summary>Traces one ray for the given debug render mode.</summary>
    // Trace ray for debug view
    private Vec3 TraceDebug(Ray ray, RenderMode mode)
    // Begin TraceDebug
    {
        // Intersect ray with scene
        Hit? hit = scene.Intersect(ray);
        // Return background on miss
        if (hit == null) return Background(ray);

        // Dispatch debug render mode
        return mode switch
        // Begin mode switch
        {
            // Show raw albedo
            RenderMode.Unlit => hit.Material.SampleLinear(hit.TextureU, hit.TextureV),
            // Map normals to color
            RenderMode.NormalDebug => (hit.Normal.Normalize() + new Vec3(1, 1, 1)) * 0.5,
            // Map UVs to color
            RenderMode.UvDebug => new Vec3(Frac(hit.TextureU), Frac(hit.TextureV), 0.25),
            // Map material terms to color
            RenderMode.MaterialDebug => new Vec3(hit.Material.Metallic, hit.Material.Roughness, hit.Material.Emission > 0.0 ? 1.0 : 0.0),
            // Shade by hit distance
            RenderMode.Depth => DepthColor((hit.Point - ray.Origin).Length()),
            // Shade by light influence
            RenderMode.LightDebug => LightDebug(hit),
            // Fall back to lit trace
            _ => Trace(ray, 0)
        // End mode switch
        };
    // End TraceDebug
    }

    /// <summary>Returns the fractional part of the given value.</summary>
    // Return fractional part
    private static double Frac(double value) => value - Math.Floor(value);

    /// <summary>Maps a hit distance to a grayscale debug color.</summary>
    // Map distance to grayscale
    private static Vec3 DepthColor(double distance)
    // Begin DepthColor
    {
        // Normalize distance to range
        double t = Math.Clamp(distance / 25.0, 0.0, 1.0);
        // Build grayscale color
        return new Vec3(1.0 - t, 1.0 - t, 1.0 - t);
    // End DepthColor
    }

    /// <summary>Accumulates per-light influence at a hit for the light debug view.</summary>
    // Accumulate light debug color
    private Vec3 LightDebug(Hit hit)
    // Begin LightDebug
    {
        // Initialize debug color
        Vec3 color = Vec3.Zero;
        // Loop over scene lights
        foreach (SceneLight light in scene.Lights)
        // Begin light loop
        {
            // Skip disabled lights
            if (!light.Enabled) continue;
            // Compute direction toward light
            Vec3 toLight = light.Kind == SceneLightKind.Directional ? light.Direction * -1.0 : light.Position - hit.Point;
            // Compute Lambert influence factor
            double influence = Math.Max(0.0, hit.Normal.Normalize().Dot(toLight.Normalize()));
            // Accumulate light contribution
            color += light.Color * Math.Clamp(influence * light.Intensity * 0.12, 0.0, 1.0);
        // End light loop
        }
        // Return debug color
        return color;
    // End LightDebug
    }

    /// <summary>Shades a surface hit with direct, environment, and emission lighting.</summary>
    // Shade surface with all lighting
    private Vec3 ShadeHit(Hit hit, Vec3 surfaceColor, Vec3 viewDirection)
    // Begin ShadeHit
    {
        // Fetch perturbed normal
        Vec3 normal = ApplyNormalMap(hit);
        // Compute view direction
        Vec3 view = (-viewDirection).Normalize();
        // Check normal orientation
        if (normal.Dot(view) < 0.0)
            // Flip normal toward viewer
            normal = -normal;

        // Sample metalness and roughness
        (double metallic, double roughness) = hit.Material.SampleMetallicRoughness(hit.TextureU, hit.TextureV);
        // Build layered shading inputs
        SurfaceShading shading = BuildSurfaceShading(hit, normal);
        // Initialize direct lighting
        Vec3 direct = Vec3.Zero;
        // Loop over scene lights
        foreach (SceneLight light in scene.Lights)
        // Begin light loop
        {
            // Check light enabled
            if (light.Enabled)
                // Accumulate direct light
                direct += DirectLight(hit, light, surfaceColor, normal, view, metallic, roughness, shading);
        // End light loop
        }

        // Sample ambient occlusion
        double occlusion = hit.Material.SampleOcclusion(hit.TextureU, hit.TextureV);
        // Compute occluded environment light
        Vec3 indirect = EnvironmentLighting(surfaceColor, normal, view, metallic, roughness, shading) * occlusion;
        // Sample surface emission
        Vec3 emission = hit.Material.SampleEmissionLinear(hit.TextureU, hit.TextureV);
        // Combine lighting terms
        return direct + indirect + emission;
    // End ShadeHit
    }

    /// <summary>Perturbs the hit normal using the material normal map.</summary>
    // Perturb normal from normal map
    private static Vec3 ApplyNormalMap(Hit hit)
    // Begin ApplyNormalMap
    {
        // Normalize geometric normal
        Vec3 normal = hit.Normal.Normalize();
        // Check for normal map
        if (hit.Material.NormalTexture == null)
            // Keep geometric normal
            return normal;

        // Normalize hit tangent
        Vec3 tangent = hit.Tangent.Normalize();
        // Normalize hit bitangent
        Vec3 bitangent = hit.Bitangent.Normalize();
        // Check degenerate tangents
        if (tangent.Length() < 1e-8 || bitangent.Length() < 1e-8)
            // Rebuild tangents from fallback axis
            FallbackTangentFrame(normal, out tangent, out bitangent);

        // Sample normal map texel
        Vec3 sample = hit.Material.SampleNormalMap(hit.TextureU, hit.TextureV);
        // Read normal strength
        double scale = hit.Material.NormalScale;
        // Begin tangent-space normal
        Vec3 tangentNormal = new(
            // Unpack tangent X component
            (sample.X * 2.0 - 1.0) * scale,
            // Unpack tangent Y component
            (sample.Y * 2.0 - 1.0) * scale,
            // Unpack tangent Z component
            sample.Z * 2.0 - 1.0);
        // Normalize tangent normal
        tangentNormal = tangentNormal.Normalize();
        // Begin world transform
        return (
            // Add tangent contribution
            tangent * tangentNormal.X +
            // Add bitangent contribution
            bitangent * tangentNormal.Y +
            // Add normal contribution
            normal * tangentNormal.Z).Normalize();
    // End ApplyNormalMap
    }

    /// <summary>Builds a stable tangent frame from a fallback axis.</summary>
    // Rebuild tangents from fallback axis
    private static void FallbackTangentFrame(Vec3 normal, out Vec3 tangent, out Vec3 bitangent)
    // Begin FallbackTangentFrame
    {
        // Pick stable fallback axis
        Vec3 axis = Math.Abs(normal.Z) < 0.999
            // Use Z axis fallback
            ? new Vec3(0.0, 0.0, 1.0)
            // Use Y axis fallback
            : new Vec3(0.0, 1.0, 0.0);
        // Rebuild tangent from axis
        tangent = axis.Cross(normal).Normalize();
        // Rebuild bitangent from tangent
        bitangent = normal.Cross(tangent).Normalize();
    // End FallbackTangentFrame
    }

    /// <summary>Builds the layered shading inputs for one surface hit.</summary>
    // Build layered shading inputs
    private static SurfaceShading BuildSurfaceShading(Hit hit, Vec3 normal)
    // Begin BuildSurfaceShading
    {
        // Fetch hit material
        Material material = hit.Material;
        // Sample specular weight and color
        (double specularWeight, Vec3 specularColor) = material.SampleSpecular(hit.TextureU, hit.TextureV);
        // Compute dielectric base reflectance
        double iorF0 = material.Ior == 1.5 ? 0.04 : IorToF0(material.Ior);
        // Clamp tinted base reflectance
        Vec3 dielectricBase = new(
            // Clamp red base channel
            Math.Min(iorF0 * specularColor.X, 1.0),
            // Clamp green base channel
            Math.Min(iorF0 * specularColor.Y, 1.0),
            // Clamp blue base channel
            Math.Min(iorF0 * specularColor.Z, 1.0));
        // Detect default specular fast path
        bool isDefaultSpecular = specularWeight == 1.0 &&
            // Check white specular color
            specularColor.X == 1.0 && specularColor.Y == 1.0 && specularColor.Z == 1.0 &&
            // Check default IOR
            material.Ior == 1.5;
        // Sample sheen albedo and roughness
        (Vec3 sheenColor, double sheenRoughness) = material.SampleSheen(hit.TextureU, hit.TextureV);
        // Measure sheen albedo maximum
        double sheenMax = Math.Max(sheenColor.X, Math.Max(sheenColor.Y, sheenColor.Z));
        // Sample anisotropy strength and direction
        (double anisotropy, Vec2 anisotropyDirection) = material.SampleAnisotropy(hit.TextureU, hit.TextureV);
        // Normalize hit tangent
        Vec3 tangent = hit.Tangent.Normalize();
        // Normalize hit bitangent
        Vec3 bitangent = hit.Bitangent.Normalize();
        // Check degenerate tangents
        if (tangent.Length() < 1e-8 || bitangent.Length() < 1e-8)
            // Rebuild tangents from fallback axis
            FallbackTangentFrame(normal, out tangent, out bitangent);
        // Rotate tangent frame by anisotropy direction
        Vec3 anisoTangent = (tangent * anisotropyDirection.U + bitangent * anisotropyDirection.V).Normalize();
        // Rebuild bitangent from shading normal
        Vec3 anisoBitangent = normal.Cross(anisoTangent).Normalize();
        // Sample iridescence strength and thickness
        (double iridescence, double iridescenceThickness) = material.SampleIridescence(hit.TextureU, hit.TextureV);
        // Read clearcoat layer weight
        double clearcoat = material.Clearcoat;
        // Reuse transmission mask for shared clearcoat
        if (material.ClearcoatUsesTransmissionTexture && material.TransmissionTexture != null)
            // Scale clearcoat by mask red channel
            clearcoat *= Math.Clamp(material.TransmissionTexture.Sample(hit.TextureU, hit.TextureV).X, 0.0, 1.0);
        // Return layered shading inputs
        return new SurfaceShading(
            // Pass dielectric base reflectance
            dielectricBase,
            // Pass specular weight
            specularWeight,
            // Pass default specular flag
            isDefaultSpecular,
            // Pass sheen albedo
            sheenColor,
            // Pass sheen roughness
            sheenRoughness,
            // Pass sheen albedo maximum
            sheenMax,
            // Pass anisotropy strength
            anisotropy,
            // Pass anisotropy tangent
            anisoTangent,
            // Pass anisotropy bitangent
            anisoBitangent,
            // Pass iridescence strength
            iridescence,
            // Pass thin-film IOR
            material.IridescenceIor,
            // Pass thin-film thickness
            iridescenceThickness,
            // Pass clearcoat layer weight
            Math.Clamp(clearcoat, 0.0, 1.0),
            // Pass clearcoat roughness
            Math.Clamp(material.ClearcoatRoughness, 0.045, 1.0));
    // End BuildSurfaceShading
    }

    /// <summary>Computes direct lighting from one light using its point, spot, or directional model.</summary>
    // Dispatch light by kind
    private Vec3 DirectLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness, SurfaceShading shading)
    // Begin DirectLight
    {
        // Switch on light kind
        return light.Kind switch
        // Begin light switch
        {
            // Shade directional light
            SceneLightKind.Directional => DirectDirectionalLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness, shading),
            // Shade spot light
            SceneLightKind.Spot => DirectSpotLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness, shading),
            // Shade point light
            _ => DirectPointLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness, shading)
        // End light switch
        };
    // End DirectLight
    }

    /// <summary>Computes direct lighting from one point light.</summary>
    // Shade one point light
    private Vec3 DirectPointLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness, SurfaceShading shading)
    // Begin DirectPointLight
    {
        // Compute vector toward light
        Vec3 toLight = light.Position - hit.Point;
        // Measure light distance
        double distance = toLight.Length();
        // Reject out-of-range light
        if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
            // Return no contribution
            return Vec3.Zero;

        // Normalize light direction
        Vec3 lightDir = toLight / distance;
        // Evaluate shadow factor
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, distance) : 1.0;
        // Evaluate distance attenuation
        double attenuation = DistanceAttenuation(distance, light.Range);
        // Compute shaded contribution
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * shadow, metallic, roughness, shading);
    // End DirectPointLight
    }

    /// <summary>Computes direct lighting from one directional light.</summary>
    // Shade one directional light
    private Vec3 DirectDirectionalLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness, SurfaceShading shading)
    // Begin DirectDirectionalLight
    {
        // glTF directional lights shine along local -Z. SceneLight.Direction is
        // the direction light travels, so surfaces are lit from the opposite ray.
        // Reverse travel into light direction
        Vec3 lightDir = (light.Direction * -1.0).Normalize();
        // Evaluate shadow factor
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, double.PositiveInfinity) : 1.0;
        // Compute shaded contribution
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * shadow, metallic, roughness, shading);
    // End DirectDirectionalLight
    }

    /// <summary>Computes direct lighting from one spot light.</summary>
    // Shade one spot light
    private Vec3 DirectSpotLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness, SurfaceShading shading)
    // Begin DirectSpotLight
    {
        // Compute vector toward light
        Vec3 toLight = light.Position - hit.Point;
        // Measure light distance
        double distance = toLight.Length();
        // Reject out-of-range light
        if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
            // Return no contribution
            return Vec3.Zero;

        // Normalize light direction
        Vec3 lightDir = toLight / distance;
        // Reverse light direction
        Vec3 lightToSurface = lightDir * -1.0;
        // Evaluate cone falloff
        double cone = SpotConeFactor(light, lightToSurface);
        // Check cone cutoff
        if (cone <= 0.0)
            // Return no contribution
            return Vec3.Zero;

        // Evaluate shadow factor
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, distance) : 1.0;
        // Evaluate distance attenuation
        double attenuation = DistanceAttenuation(distance, light.Range);
        // Compute shaded contribution
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * cone * shadow, metallic, roughness, shading);
    // End DirectSpotLight
    }

    /// <summary>Computes the diffuse and specular contribution of one light.</summary>
    // Compute PBR light contribution
    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 view, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness, SurfaceShading shading)
    // Begin LightContribution
    {
        // Clamp normal light alignment
        double nDotL = Math.Max(0.0, normal.Dot(lightDir));
        // Clamp normal view alignment
        double nDotV = Math.Max(0.0001, normal.Dot(view));
        // Reject backlit light
        if (nDotL <= 0.0 || strength <= 0.0)
            // Return no contribution
            return Vec3.Zero;

        // Compute halfway vector
        Vec3 halfVector = (lightDir + view).Normalize();
        // Clamp normal half alignment
        double nDotH = Math.Max(0.0, normal.Dot(halfVector));
        // Clamp view half alignment
        double vDotH = Math.Max(0.0, view.Dot(halfVector));
        // Square perceptual roughness
        double alphaRoughness = roughness * roughness;
        // Declare distribution term
        double distribution;
        // Declare visibility term
        double visibility;
        // Check anisotropic lobe
        if (shading.Anisotropy > 0.0)
        // Begin anisotropic branch
        {
            // Project halfway on tangent
            double tDotH = shading.AnisoTangent.Dot(halfVector);
            // Project halfway on bitangent
            double bDotH = shading.AnisoBitangent.Dot(halfVector);
            // Stretch tangent roughness by anisotropy
            double alphaT = alphaRoughness + (1.0 - alphaRoughness) * shading.Anisotropy * shading.Anisotropy;
            // Evaluate anisotropic distribution
            distribution = DistributionGgxAnisotropic(nDotH, tDotH, bDotH, alphaT, alphaRoughness);
            // Evaluate anisotropic visibility
            visibility = VisibilitySmithGgxAnisotropic(
                // Pass light view alignment
                nDotL, nDotV,
                // Pass bitangent view alignment
                shading.AnisoBitangent.Dot(view),
                // Pass tangent view alignment
                shading.AnisoTangent.Dot(view),
                // Pass tangent light alignment
                shading.AnisoTangent.Dot(lightDir),
                // Pass bitangent light alignment
                shading.AnisoBitangent.Dot(lightDir),
                // Pass tangent bitangent roughness
                alphaT, alphaRoughness);
        // End anisotropic branch
        }
        else
        // Begin isotropic branch
        {
            // Evaluate GGX distribution
            distribution = DistributionGgx(nDotH, alphaRoughness);
            // Evaluate Smith visibility
            visibility = VisibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        // End isotropic branch
        }

        // Declare specular BRDF term
        Vec3 specular;
        // Declare diffuse BRDF term
        Vec3 diffuse;
        // Check default specular fast path
        if (shading.IsDefaultSpecular && shading.Iridescence <= 0.0)
        // Begin default specular branch
        {
            // Interpolate base reflectance
            Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
            // Evaluate Schlick fresnel
            Vec3 fresnel = FresnelSchlick(vDotH, f0);
            // Combine specular BRDF terms
            specular = fresnel * (distribution * visibility);
            // Compute diffuse BRDF term
            diffuse = (new Vec3(1.0, 1.0, 1.0) - fresnel).Multiply(surfaceColor) * ((1.0 - metallic) / Pi);
        // End default specular branch
        }
        else
        // Begin layered specular branch
        {
            // Evaluate fresnel blend weight
            double fresnelWeight = Math.Pow(1.0 - vDotH, 5.0);
            // Evaluate dielectric fresnel color
            Vec3 dielectricFresnel = shading.DielectricBaseF0 + (new Vec3(1.0, 1.0, 1.0) - shading.DielectricBaseF0) * fresnelWeight;
            // Weight dielectric fresnel by specular
            dielectricFresnel *= shading.SpecularWeight;
            // Evaluate metal fresnel color
            Vec3 metalFresnel = FresnelSchlick(vDotH, surfaceColor);
            // Check thin-film layer
            if (shading.Iridescence > 0.0)
            // Begin iridescence branch
            {
                // Evaluate dielectric film color
                Vec3 dielectricFilm = IridescentFresnel(1.0, shading.IridescenceIor, shading.DielectricBaseF0 * shading.SpecularWeight, shading.IridescenceThickness, vDotH);
                // Blend dielectric fresnel toward film
                dielectricFresnel = Vec3.Lerp(dielectricFresnel, dielectricFilm, shading.Iridescence);
                // Evaluate conductor film color
                Vec3 conductorFilm = IridescentFresnel(1.0, shading.IridescenceIor, surfaceColor, shading.IridescenceThickness, vDotH);
                // Blend metal fresnel toward film
                metalFresnel = Vec3.Lerp(metalFresnel, conductorFilm, shading.Iridescence);
            // End iridescence branch
            }
            // Blend specular weight by metalness
            Vec3 specWeight = Vec3.Lerp(dielectricFresnel, metalFresnel, metallic);
            // Combine layered specular terms
            specular = specWeight * (distribution * visibility);
            // Measure dielectric energy remainder
            double dielectricRemainder = 1.0 - Math.Max(dielectricFresnel.X, Math.Max(dielectricFresnel.Y, dielectricFresnel.Z));
            // Compute layered diffuse term
            diffuse = surfaceColor * (dielectricRemainder * (1.0 - metallic) / Pi);
        // End layered specular branch
        }

        // Combine base BRDF terms
        Vec3 brdf = diffuse + specular;
        // Check sheen layer
        if (shading.SheenMax > 0.0)
        // Begin sheen branch
        {
            // Evaluate Charlie sheen distribution
            double sheenDistribution = DistributionCharlie(nDotH, shading.SheenRoughness);
            // Evaluate Charlie sheen visibility
            double sheenVisibility = VisibilitySheen(nDotV, nDotL, shading.SheenRoughness);
            // Scale base layer by sheen albedo
            brdf = brdf * Math.Clamp(1.0 - shading.SheenMax, 0.0, 1.0) + shading.SheenColor * (sheenDistribution * sheenVisibility);
        // End sheen branch
        }
        // Check clearcoat layer
        if (shading.Clearcoat > 0.001)
        // Begin clearcoat branch
        {
            // Square clearcoat roughness
            double clearcoatAlpha = shading.ClearcoatRoughness * shading.ClearcoatRoughness;
            // Evaluate clearcoat distribution
            double clearcoatDistribution = DistributionGgx(nDotH, clearcoatAlpha);
            // Evaluate clearcoat visibility
            double clearcoatVisibility = VisibilitySmithGgxCorrelated(nDotV, nDotL, clearcoatAlpha);
            // Evaluate clearcoat fresnel
            Vec3 clearcoatFresnel = FresnelSchlick(vDotH, new Vec3(0.04, 0.04, 0.04));
            // Evaluate clearcoat view fresnel
            double clearcoatViewFresnel = 0.04 + 0.96 * Math.Pow(1.0 - nDotV, 5.0);
            // Layer clearcoat over scaled base
            brdf = brdf * (1.0 - shading.Clearcoat * clearcoatViewFresnel) +
                clearcoatFresnel * (shading.Clearcoat * clearcoatDistribution * clearcoatVisibility);
        // End clearcoat branch
        }

        // Scale light by strength
        Vec3 radiance = lightColor * (strength * 0.18);
        // Modulate BRDF by radiance
        return brdf.Multiply(radiance) * nDotL;
    // End LightContribution
    }

    /// <summary>Computes diffuse and specular environment lighting for a surface.</summary>
    // Compute IBL for surface
    private static Vec3 EnvironmentLighting(Vec3 baseColor, Vec3 normal, Vec3 view, double metallic, double roughness, SurfaceShading shading)
    // Begin EnvironmentLighting
    {
        // Clamp normal view alignment
        double nDotV = Math.Max(0.0001, normal.Dot(view));
        // Interpolate layered base reflectance
        Vec3 f0 = Vec3.Lerp(shading.DielectricBaseF0 * shading.SpecularWeight, baseColor, metallic);
        // Check thin-film layer
        if (shading.Iridescence > 0.0)
            // Blend reflectance toward film color
            f0 = Vec3.Lerp(f0, IridescentFresnel(1.0, shading.IridescenceIor, f0, shading.IridescenceThickness, nDotV), shading.Iridescence);
        // Compute grazing angle factor
        double grazing = Math.Pow(1.0 - nDotV, 5.0);
        // Scale grazing cap by specular weight
        double grazingCap = (1.0 - roughness) * shading.SpecularWeight;
        // Begin roughness reflectance cap
        Vec3 maximum = new(
            // Cap red reflectance channel
            Math.Max(grazingCap, f0.X),
            // Cap green reflectance channel
            Math.Max(grazingCap, f0.Y),
            // Cap blue reflectance channel
            Math.Max(grazingCap, f0.Z));
        // Interpolate grazing fresnel
        Vec3 fresnel = f0 + (maximum - f0) * grazing;
        // Compute diffuse energy remainder
        Vec3 oneMinusFresnel = new Vec3(1.0, 1.0, 1.0) - fresnel;
        // Compute IBL diffuse term
        Vec3 diffuse = DiffuseEnvironment(normal).Multiply(baseColor).Multiply(oneMinusFresnel) * (1.0 - metallic);
        // Mirror view about normal
        Vec3 reflection = Reflect(-view, normal).Normalize();
        // Sample sharp studio reflection
        Vec3 sharpSpecular = StudioEnvironment(reflection);
        // Sample broad diffuse reflection
        Vec3 broadSpecular = DiffuseEnvironment(normal);
        // Blend specular by roughness
        Vec3 specular = Vec3.Lerp(sharpSpecular, broadSpecular, roughness * roughness).Multiply(fresnel);
        // Combine indirect lighting
        Vec3 indirect = diffuse + specular;
        // Check clearcoat layer
        if (shading.Clearcoat > 0.001)
        // Begin clearcoat branch
        {
            // Evaluate clearcoat view fresnel
            double clearcoatViewFresnel = 0.04 + 0.96 * Math.Pow(1.0 - nDotV, 5.0);
            // Blend clearcoat environment reflections
            Vec3 clearcoatIbl = Vec3.Lerp(sharpSpecular, broadSpecular, shading.ClearcoatRoughness * shading.ClearcoatRoughness) * (clearcoatViewFresnel * shading.Clearcoat);
            // Scale indirect by clearcoat layer
            indirect = indirect * (1.0 - shading.Clearcoat * clearcoatViewFresnel);
            // Scale combined IBL output
            indirect *= 0.72;
            // Add clearcoat indirect
            indirect += clearcoatIbl;
            // Return layered indirect lighting
            return indirect;
        // End clearcoat branch
        }
        // Scale combined IBL output
        return indirect * 0.72;
    // End EnvironmentLighting
    }

    /// <summary>Evaluates the GGX normal distribution term.</summary>
    // Evaluate GGX distribution term
    private static double DistributionGgx(double nDotH, double alphaRoughness)
    // Begin DistributionGgx
    {
        // Square roughness parameter
        double a2 = alphaRoughness * alphaRoughness;
        // Compute denominator factor
        double f = nDotH * nDotH * (a2 - 1.0) + 1.0;
        // Return normalized distribution
        return a2 / Math.Max(Pi * f * f, 1e-8);
    // End DistributionGgx
    }

    /// <summary>Evaluates the correlated Smith GGX visibility term.</summary>
    // Evaluate Smith visibility term
    private static double VisibilitySmithGgxCorrelated(double nDotV, double nDotL, double alphaRoughness)
    // Begin visibility function
    {
        // Square roughness parameter
        double a2 = alphaRoughness * alphaRoughness;
        // Compute view geometry term
        double gv = nDotL * Math.Sqrt(Math.Max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
        // Compute light geometry term
        double gl = nDotV * Math.Sqrt(Math.Max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
        // Return correlated visibility
        return 0.5 / Math.Max(gv + gl, 1e-8);
    // End visibility function
    }

    /// <summary>Evaluates the Schlick fresnel approximation.</summary>
    // Evaluate Schlick fresnel term
    private static Vec3 FresnelSchlick(double vDotH, Vec3 f0)
    // Begin FresnelSchlick
    {
        // Compute fifth-power falloff
        double factor = Math.Pow(1.0 - Math.Clamp(vDotH, 0.0, 1.0), 5.0);
        // Blend base to white
        return f0 + (new Vec3(1.0, 1.0, 1.0) - f0) * factor;
    // End FresnelSchlick
    }

    /// <summary>Converts an IOR pair to normal-incidence Fresnel reflectance.</summary>
    // Convert IOR pair to F0
    private static double Fresnel0FromIor(double transmittedIor, double incidentIor)
    // Begin Fresnel0FromIor
    {
        // Evaluate IOR ratio
        double ratio = (transmittedIor - incidentIor) / (transmittedIor + incidentIor);
        // Return squared ratio
        return ratio * ratio;
    // End Fresnel0FromIor
    }

    /// <summary>Converts a single IOR against air to normal-incidence Fresnel reflectance.</summary>
    // Convert IOR to F0
    private static double IorToF0(double ior) => Fresnel0FromIor(ior, 1.0);

    /// <summary>Evaluates the anisotropic GGX normal distribution term.</summary>
    // Evaluate anisotropic GGX distribution
    private static double DistributionGgxAnisotropic(double nDotH, double tDotH, double bDotH, double at, double ab)
    // Begin DistributionGgxAnisotropic
    {
        // Multiply tangent bitangent roughness
        double a2 = at * ab;
        // Build scaled halfway vector
        double fx = ab * tDotH;
        // Scale bitangent component
        double fy = at * bDotH;
        // Scale normal component
        double fz = a2 * nDotH;
        // Measure guarded squared length
        double lengthSquared = Math.Max(fx * fx + fy * fy + fz * fz, 1e-12);
        // Evaluate slope weight
        double w2 = a2 / lengthSquared;
        // Return normalized distribution
        return a2 * w2 * w2 / Pi;
    // End DistributionGgxAnisotropic
    }

    /// <summary>Evaluates the anisotropic Smith GGX visibility term.</summary>
    // Evaluate anisotropic visibility term
    private static double VisibilitySmithGgxAnisotropic(double nDotL, double nDotV, double bDotV, double tDotV, double tDotL, double bDotL, double at, double ab)
    // Begin VisibilitySmithGgxAnisotropic
    {
        // Compute view direction length
        double viewLength = Math.Sqrt(Math.Max(at * at * tDotV * tDotV + ab * ab * bDotV * bDotV + nDotV * nDotV, 0.0));
        // Compute light direction length
        double lightLength = Math.Sqrt(Math.Max(at * at * tDotL * tDotL + ab * ab * bDotL * bDotL + nDotL * nDotL, 0.0));
        // Compute view geometry term
        double ggxV = nDotL * viewLength;
        // Compute light geometry term
        double ggxL = nDotV * lightLength;
        // Return clamped correlated visibility
        return Math.Clamp(0.5 / Math.Max(ggxV + ggxL, 1e-8), 0.0, 1.0);
    // End VisibilitySmithGgxAnisotropic
    }

    /// <summary>Evaluates the Charlie sheen normal distribution term.</summary>
    // Evaluate Charlie sheen distribution
    private static double DistributionCharlie(double nDotH, double sheenRoughness)
    // Begin DistributionCharlie
    {
        // Square sheen roughness with floor
        double alphaG = Math.Max(sheenRoughness * sheenRoughness, 1e-4);
        // Invert roughness parameter
        double invR = 1.0 / alphaG;
        // Compute sine squared term
        double sin2H = Math.Max(1.0 - nDotH * nDotH, 0.0);
        // Return normalized distribution
        return (2.0 + invR) * Math.Pow(sin2H, invR * 0.5) / (2.0 * Pi);
    // End DistributionCharlie
    }

    /// <summary>Evaluates the Charlie sheen lambda term for one direction.</summary>
    // Evaluate sheen lambda term
    private static double SheenLambda(double cosTheta, double alphaG)
    // Begin SheenLambda
    {
        // Square roughness complement
        double oneMinusAlphaSq = (1.0 - alphaG) * (1.0 - alphaG);
        // Blend fit coefficient a
        double a = 21.5473 + (25.3245 - 21.5473) * oneMinusAlphaSq;
        // Blend fit coefficient b
        double b = 3.82987 + (3.32435 - 3.82987) * oneMinusAlphaSq;
        // Blend fit coefficient c
        double c = 0.19823 + (0.16801 - 0.19823) * oneMinusAlphaSq;
        // Blend fit coefficient d
        double d = -1.97760 + (-1.27393 + 1.97760) * oneMinusAlphaSq;
        // Blend fit coefficient e
        double e = -4.32054 + (-4.85967 + 4.32054) * oneMinusAlphaSq;
        // Evaluate fit for grazing angles
        double fit(double x) => a / (1.0 + b * Math.Pow(Math.Max(x, 0.0), c)) + d * x + e;
        // Mirror fit past halfway
        return Math.Abs(cosTheta) < 0.5 ? Math.Exp(fit(cosTheta)) : Math.Exp(2.0 * fit(0.5) - fit(1.0 - cosTheta));
    // End SheenLambda
    }

    /// <summary>Evaluates the Charlie sheen visibility term.</summary>
    // Evaluate sheen visibility term
    private static double VisibilitySheen(double nDotV, double nDotL, double sheenRoughness)
    // Begin VisibilitySheen
    {
        // Square sheen roughness with floor
        double alphaG = Math.Max(sheenRoughness * sheenRoughness, 1e-4);
        // Evaluate view lambda term
        double lambdaV = SheenLambda(nDotV, alphaG);
        // Evaluate light lambda term
        double lambdaL = SheenLambda(nDotL, alphaG);
        // Return normalized visibility
        return 1.0 / ((1.0 + lambdaV + lambdaL) * (4.0 * nDotV * nDotL));
    // End VisibilitySheen
    }

    /// <summary>Evaluates thin-film iridescence Fresnel reflectance at one angle.</summary>
    // Evaluate iridescent fresnel color
    private static Vec3 IridescentFresnel(double outsideIor, double iridescenceIor, Vec3 baseF0, double thicknessNm, double cosTheta1)
    // Begin IridescentFresnel
    {
        // Clamp incident cosine
        cosTheta1 = Math.Clamp(cosTheta1, 0.0, 1.0);
        // Clamp film base reflectance
        Vec3 filmBase = new(
            // Clamp red film channel
            Math.Clamp(baseF0.X, 0.0, 1.0),
            // Clamp green film channel
            Math.Clamp(baseF0.Y, 0.0, 1.0),
            // Clamp blue film channel
            Math.Clamp(baseF0.Z, 0.0, 1.0));
        // Refract into thin film
        double eta = outsideIor / iridescenceIor;
        // Compute squared refracted sine
        double sinTheta2Sq = eta * eta * (1.0 - cosTheta1 * cosTheta1);
        // Compute squared refracted cosine
        double cosTheta2Sq = 1.0 - sinTheta2Sq;
        // Handle total internal reflection
        if (cosTheta2Sq < 0.0)
            // Return full reflectance
            return new Vec3(1.0, 1.0, 1.0);
        // Compute refracted cosine
        double cosTheta2 = Math.Sqrt(cosTheta2Sq);
        // Evaluate first interface reflectance
        double r0 = Fresnel0FromIor(iridescenceIor, outsideIor);
        // Evaluate first interface fresnel
        double r12 = r0 + (1.0 - r0) * Math.Pow(1.0 - cosTheta1, 5.0);
        // Evaluate first interface transmittance
        double t121 = 1.0 - r12;
        // Convert guarded base reflectance to IOR
        Vec3 baseIor = new(
            // Convert red channel
            Fresnel0ToIor(filmBase.X),
            // Convert green channel
            Fresnel0ToIor(filmBase.Y),
            // Convert blue channel
            Fresnel0ToIor(filmBase.Z));
        // Evaluate second interface reflectance
        Vec3 r1 = new(
            // Convert red interface
            Fresnel0FromIor(baseIor.X, iridescenceIor),
            // Convert green interface
            Fresnel0FromIor(baseIor.Y, iridescenceIor),
            // Convert blue interface
            Fresnel0FromIor(baseIor.Z, iridescenceIor));
        // Evaluate second interface fresnel
        double fresnel2Weight = Math.Pow(1.0 - cosTheta2, 5.0);
        // Blend second interface reflectance
        Vec3 r23 = r1 + (new Vec3(1.0, 1.0, 1.0) - r1) * fresnel2Weight;
        // Evaluate first interface phase
        double phi12 = iridescenceIor < outsideIor ? Pi : 0.0;
        // Evaluate return phase
        double phi21 = Pi - phi12;
        // Evaluate second interface phases
        Vec3 phi23 = new(
            // Evaluate red phase
            baseIor.X < iridescenceIor ? Pi : 0.0,
            // Evaluate green phase
            baseIor.Y < iridescenceIor ? Pi : 0.0,
            // Evaluate blue phase
            baseIor.Z < iridescenceIor ? Pi : 0.0);
        // Combine interface phases
        Vec3 phi = new(phi21 + phi23.X, phi21 + phi23.Y, phi21 + phi23.Z);
        // Evaluate optical path difference
        double opd = 2.0 * iridescenceIor * thicknessNm * cosTheta2;
        // Clamp compound reflectance
        Vec3 r123 = new(
            // Clamp red compound channel
            Math.Clamp(r12 * r23.X, 1e-5, 0.9999),
            // Clamp green compound channel
            Math.Clamp(r12 * r23.Y, 1e-5, 0.9999),
            // Clamp blue compound channel
            Math.Clamp(r12 * r23.Z, 1e-5, 0.9999));
        // Root compound reflectance
        Vec3 sqrtR123 = new(Math.Sqrt(r123.X), Math.Sqrt(r123.Y), Math.Sqrt(r123.Z));
        // Evaluate multiple scattering term
        double t121Sq = t121 * t121;
        // Scale scattered reflectance
        Vec3 rs = new(
            // Scale red scattered channel
            t121Sq * r23.X / (1.0 - r123.X),
            // Scale green scattered channel
            t121Sq * r23.Y / (1.0 - r123.Y),
            // Scale blue scattered channel
            t121Sq * r23.Z / (1.0 - r123.Z));
        // Evaluate DC reflectance term
        Vec3 intensity = new(r12 + rs.X, r12 + rs.Y, r12 + rs.Z);
        // Initialize series coefficient
        Vec3 cm = rs - new Vec3(t121, t121, t121);
        // Accumulate interference orders
        for (int m = 1; m <= 2; m++)
        // Begin interference loop
        {
            // Decay series coefficient
            cm = cm.Multiply(sqrtR123);
            // Evaluate spectral sensitivity
            Vec3 sm = EvalIridescenceSensitivity(m * opd, new Vec3(m * phi.X, m * phi.Y, m * phi.Z)) * 2.0;
            // Accumulate interference term
            intensity += cm.Multiply(sm);
        // End interference loop
        }
        // Clamp reflectance to unit range
        return new Vec3(
            // Clamp red reflectance
            Math.Clamp(intensity.X, 0.0, 1.0),
            // Clamp green reflectance
            Math.Clamp(intensity.Y, 0.0, 1.0),
            // Clamp blue reflectance
            Math.Clamp(intensity.Z, 0.0, 1.0));
    // End IridescentFresnel
    }

    /// <summary>Converts normal-incidence Fresnel reflectance back to IOR.</summary>
    // Convert F0 back to IOR
    private static double Fresnel0ToIor(double f0)
    // Begin Fresnel0ToIor
    {
        // Root guarded reflectance below one
        double sqrtF0 = Math.Min(Math.Sqrt(Math.Max(f0, 0.0) + 0.0001), 1.0 - 1e-7);
        // Return inverted fresnel ratio
        return (1.0 + sqrtF0) / (1.0 - sqrtF0);
    // End Fresnel0ToIor
    }

    /// <summary>Evaluates the thin-film spectral sensitivity for one order.</summary>
    // Evaluate spectral sensitivity
    private static Vec3 EvalIridescenceSensitivity(double opd, Vec3 shift)
    // Begin EvalIridescenceSensitivity
    {
        // Scale optical path to phase
        double phase = 2.0 * Pi * opd * 1.0e-9;
        // Evaluate red tristimulus term
        double x = 5.4856e-13 * Math.Sqrt(2.0 * Pi * 4.3278e09) * Math.Cos(1.6810e06 * phase + shift.X) * Math.Exp(-phase * phase * 4.3278e09);
        // Evaluate green tristimulus term
        double y = 4.4201e-13 * Math.Sqrt(2.0 * Pi * 9.3046e09) * Math.Cos(1.7953e06 * phase + shift.Y) * Math.Exp(-phase * phase * 9.3046e09);
        // Evaluate blue tristimulus term
        double z = 5.2481e-13 * Math.Sqrt(2.0 * Pi * 6.6121e09) * Math.Cos(2.2084e06 * phase + shift.Z) * Math.Exp(-phase * phase * 6.6121e09);
        // Add red side lobe term
        x += 9.7470e-14 * Math.Sqrt(2.0 * Pi * 4.5282e09) * Math.Cos(2.2399e06 * phase + shift.X) * Math.Exp(-4.5282e09 * phase * phase);
        // Normalize by illuminant energy
        x /= 1.0685e-7;
        // Normalize green tristimulus
        y /= 1.0685e-7;
        // Normalize blue tristimulus
        z /= 1.0685e-7;
        // Convert XYZ tristimulus to linear RGB
        return new Vec3(
            // Convert red channel
            3.2404542 * x - 1.5371385 * y - 0.4985314 * z,
            // Convert green channel
            -0.9692660 * x + 1.8760108 * y + 0.0415560 * z,
            // Convert blue channel
            0.0556434 * x - 0.2040259 * y + 1.0572252 * z);
    // End EvalIridescenceSensitivity
    }

    /// <summary>Samples the analytic studio environment in the given direction.</summary>
    // Sample studio environment map
    private static Vec3 StudioEnvironment(Vec3 direction)
    // Begin StudioEnvironment
    {
        // Normalize sample direction
        direction = direction.Normalize();
        // Blend ground to sky
        double skyAmount = SmoothStep(-0.25, 0.65, direction.Y);
        // Build gradient radiance
        Vec3 radiance = Vec3.Lerp(new Vec3(0.035, 0.040, 0.050), new Vec3(0.32, 0.39, 0.49), skyAmount);
        // Define key light direction
        Vec3 keyDirection = new Vec3(-0.55, 0.62, -0.56).Normalize();
        // Define rim light direction
        Vec3 rimDirection = new Vec3(0.78, 0.28, 0.56).Normalize();
        // Compute key lobe
        double key = Math.Pow(Math.Max(direction.Dot(keyDirection), 0.0), 96.0);
        // Compute rim lobe
        double rim = Math.Pow(Math.Max(direction.Dot(rimDirection), 0.0), 180.0);
        // Combine gradient and lobes
        return radiance + new Vec3(5.2, 4.8, 4.2) * key + new Vec3(2.0, 2.7, 3.8) * rim;
    // End StudioEnvironment
    }

    /// <summary>Samples the diffuse gradient environment for the given normal.</summary>
    // Sample diffuse environment map
    private static Vec3 DiffuseEnvironment(Vec3 normal)
    // Begin DiffuseEnvironment
    {
        // Map normal to sky blend
        double skyAmount = Math.Clamp(normal.Y * 0.5 + 0.5, 0.0, 1.0);
        // Blend ground and sky
        return Vec3.Lerp(new Vec3(0.055, 0.060, 0.070), new Vec3(0.42, 0.47, 0.54), skyAmount);
    // End DiffuseEnvironment
    }

    /// <summary>Evaluates a clamped smoothstep interpolation.</summary>
    // Evaluate smoothstep interpolation
    private static double SmoothStep(double edge0, double edge1, double value)
    // Begin SmoothStep
    {
        // Normalize and clamp input
        double t = Math.Clamp((value - edge0) / Math.Max(edge1 - edge0, 1e-8), 0.0, 1.0);
        // Apply cubic smoothing
        return t * t * (3.0 - 2.0 * t);
    // End SmoothStep
    }

    /// <summary>Computes falloff attenuation for a light distance and range.</summary>
    // Compute distance light falloff
    private static double DistanceAttenuation(double distance, double range)
    // Begin DistanceAttenuation
    {
        // Apply inverse square falloff
        double attenuation = 1.0 / (1.0 + 0.11 * distance * distance);
        // Check unlimited range
        if (range <= 0.0)
            // Return unclipped attenuation
            return attenuation;

        // Normalize distance by range
        double normalized = Math.Clamp(distance / range, 0.0, 1.0);
        // Compute range edge fade
        double edgeFade = 1.0 - normalized * normalized;
        // Apply squared edge fade
        return attenuation * edgeFade * edgeFade;
    // End DistanceAttenuation
    }

    /// <summary>Evaluates Beer-Lambert volume attenuation for transmitted light.</summary>
    // Evaluate volume attenuation
    private static Vec3 VolumeAttenuation(Hit hit, Vec3 rayDirection)
    // Begin VolumeAttenuation
    {
        // Measure view alignment magnitude
        double alignment = Math.Abs(hit.Normal.Normalize().Dot(rayDirection * -1.0));
        // Compute view dependent path length
        double pathLength = hit.Material.Thickness / Math.Max(alignment, 0.15);
        // Normalize path by attenuation distance
        double density = pathLength / hit.Material.AttenuationDistance;
        // Apply Beer Lambert absorption
        return new Vec3(
            // Attenuate red channel
            Math.Pow(Math.Max(hit.Material.AttenuationColor.X, 0.0001), density),
            // Attenuate green channel
            Math.Pow(Math.Max(hit.Material.AttenuationColor.Y, 0.0001), density),
            // Attenuate blue channel
            Math.Pow(Math.Max(hit.Material.AttenuationColor.Z, 0.0001), density));
    // End VolumeAttenuation
    }

    /// <summary>Computes the inner-to-outer cone falloff for a spot light.</summary>
    // Compute spot cone falloff
    private static double SpotConeFactor(SceneLight light, Vec3 lightToSurface)
    // Begin SpotConeFactor
    {
        // Compute cone alignment cosine
        double theta = light.Direction.Normalize().Dot(lightToSurface.Normalize());
        // Compute outer cone cosine
        double outer = Math.Cos(light.OuterConeAngle);
        // Compute inner cone cosine
        double inner = Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle));
        // Reject outside cone
        if (theta <= outer) return 0.0;
        // Accept inside inner cone
        if (theta >= inner) return 1.0;
        // Interpolate penumbra falloff
        return (theta - outer) / Math.Max(1e-8, inner - outer);
    // End SpotConeFactor
    }

    /// <summary>Computes the shadowed light multiplier along a shadow ray.</summary>
    // Compute shadowed light multiplier
    private double ShadowFactor(Hit hit, Vec3 lightDir, double distance)
    // Begin ShadowFactor
    {
        // Build offset shadow ray
        Ray shadowRay = new(hit.Point + hit.Normal * 0.002, lightDir);
        // Sample scene shadow opacity
        double opacity = scene.ShadowOpacity(shadowRay, distance, maxSamples: 8);
        // Clamp transmitted light
        return Math.Clamp(1.0 - opacity * 0.82, 0.18, 1.0);
    // End ShadowFactor
    }

    /// <summary>Returns the vertical-gradient background color for a missed ray.</summary>
    // Compute background gradient color
    private Vec3 Background(Ray ray)
    // Begin Background
    {
        // Map direction to blend
        double t = Math.Clamp(ray.Direction.Y * 0.5 + 0.5, 0.0, 1.0);
        // Blend horizon gradient
        return Vec3.Lerp(BackgroundBottom, BackgroundTop, t);
    // End Background
    }

    /// <summary>Converts linear HDR ray-traced color to display-referred sRGB using Khronos PBR Neutral tone mapping.</summary>
    // Convert linear to display sRGB
    public static Vec3 ToDisplayColor(Vec3 linearColor, double exposure = 1.0)
    // Begin ToDisplayColor
    {
        // Apply exposure and sanitize
        Vec3 color = SanitizeLinear(linearColor * Math.Max(0.0, exposure));
        // Apply neutral tone mapping
        color = PbrNeutralToneMap(color);
        // Begin sRGB color
        return new Vec3(
            // Convert red channel
            LinearChannelToSrgb(color.X),
            // Convert green channel
            LinearChannelToSrgb(color.Y),
            // Convert blue channel
            LinearChannelToSrgb(color.Z));
    // End ToDisplayColor
    }

    /// <summary>Applies Khronos PBR neutral tone mapping to a linear color.</summary>
    // Apply PBR neutral tone map
    private static Vec3 PbrNeutralToneMap(Vec3 color)
    // Begin PbrNeutralToneMap
    {
        // Set highlight compression start
        const double startCompression = 0.76;
        // Set highlight desaturation amount
        const double desaturation = 0.15;
        // Find minimum color channel
        double minimum = Math.Min(color.X, Math.Min(color.Y, color.Z));
        // Compute toe offset
        double offset = minimum < 0.08 ? minimum - 6.25 * minimum * minimum : 0.04;
        // Subtract toe offset
        color -= new Vec3(offset, offset, offset);

        // Find peak color channel
        double peak = Math.Max(color.X, Math.Max(color.Y, color.Z));
        // Check compression threshold
        if (peak < startCompression)
            // Return uncompressed color
            return SanitizeLinear(color);

        // Compute compression range
        double d = 1.0 - startCompression;
        // Compress peak toward white
        double newPeak = 1.0 - d * d / (peak + d - startCompression);
        // Scale color to peak
        color *= newPeak / Math.Max(peak, 1e-8);
        // Compute desaturation blend
        double grayMix = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
        // Blend toward gray peak
        return SanitizeLinear(Vec3.Lerp(color, new Vec3(newPeak, newPeak, newPeak), grayMix));
    // End PbrNeutralToneMap
    }

    /// <summary>Clamps a linear color to the finite half-float range.</summary>
    // Sanitize linear color channels
    private static Vec3 SanitizeLinear(Vec3 color) => new(
        // Sanitize red channel
        SanitizeLinearChannel(color.X),
        // Sanitize green channel
        SanitizeLinearChannel(color.Y),
        // Sanitize blue channel
        SanitizeLinearChannel(color.Z));

    /// <summary>Clamps one linear channel to the finite half-float range.</summary>
    // Clamp single linear channel
    private static double SanitizeLinearChannel(double value)
    // Begin SanitizeLinearChannel
    {
        // Reject nonpositive or infinite
        if (!double.IsFinite(value) || value <= 0.0)
            // Return black channel
            return 0.0;
        // Clamp to half maximum
        return Math.Min(value, 65504.0);
    // End SanitizeLinearChannel
    }

    /// <summary>Converts one linear channel to sRGB.</summary>
    // Convert linear channel to sRGB
    private static double LinearChannelToSrgb(double value)
    // Begin LinearChannelToSrgb
    {
        // Clamp negative values
        value = Math.Max(value, 0.0);
        // Branch on linear segment
        return value <= 0.0031308
            // Scale dark linear segment
            ? value * 12.92
            // Apply gamma curve segment
            : 1.055 * Math.Pow(value, 1.0 / 2.4) - 0.055;
    // End LinearChannelToSrgb
    }

    /// <summary>Builds a world-space ray direction for a pixel through the camera basis.</summary>
    // Declare integer pixel ray helper
    public static Vec3 RayDirection(int x, int y, int width, int height, CameraBasis basis)
        // Offset to pixel center
        => RayDirection(x + 0.5, y + 0.5, width, height, basis);

    /// <summary>Builds a world-space ray direction for a pixel through the camera basis.</summary>
    // Overload using default 72-degree field of view
    public static Vec3 RayDirection(double x, double y, int width, int height, CameraBasis basis)
        // Delegates to full overload with default FOV
        => RayDirection(x, y, width, height, basis, 72.0);

    /// <summary>Builds a camera ray using an explicit vertical field of view.</summary>
    // Builds camera ray with explicit vertical FOV
    public static Vec3 RayDirection(double x, double y, int width, int height, CameraBasis basis, double fieldOfViewDegrees)
    // Begin camera ray direction method
    {
        // Computes viewport aspect ratio from dimensions
        double aspect = width / (double)height;
        // Clamps field of view to valid range
        double safeFov = Math.Clamp(fieldOfViewDegrees, 1.0, 179.0);
        // Converts half FOV to tangent scale factor
        double fov = Math.Tan((safeFov * Math.PI / 180.0) / 2.0);
        // Match the interactive viewport's screen handedness. The camera projects
        // positive screen X opposite the previous software-render path, which made
        // the raytraced render and saved images appear left/right mirrored compared
        // with the editable viewport.  Keep the vertical mapping unchanged and only
        // flip the horizontal screen coordinate at ray generation time so picking,
        // progressive preview, final render, and image export all stay consistent.
        // Maps pixel X to flipped horizontal offset
        double u = (1.0 - 2.0 * x / width) * aspect * fov;
        // Maps pixel Y to vertical offset
        double v = (1.0 - 2.0 * y / height) * fov;
        // Combines basis vectors into normalized ray
        return (basis.Forward + basis.Right * u + basis.Up * v).Normalize();
    // End camera ray direction method
    }

    /// <summary>Traces a wireframe view ray, marching past hits to find dashed hidden lines.</summary>
    // Trace wireframe hidden-line view
    private Vec3 TraceWireframe(Ray ray)
    // Begin TraceWireframe
    {
        // Limit marched hits per ray
        const int MaxMarchHits = 6;
        // Start marching from camera ray
        Ray current = ray;
        // March through successive scene hits
        for (int depth = 0; depth < MaxMarchHits; depth++)
        // Begin hit marching loop
        {
            // Find nearest remaining hit
            Hit? hit = scene.Intersect(current);
            // Missed everything shows background
            if (hit == null)
                // Return background gradient color
                return Background(ray);
            // Measure world distance to triangle edge
            double edgeDistance = EdgeDistanceToTriangle(hit);
            // Scale pixel width by hit distance
            double pixelWorld = WireframePixelWorld * Math.Max(hit.T, 1e-6);
            // Test hit against line half width
            if (edgeDistance <= pixelWorld * 0.75)
            // Begin edge pixel branch
            {
                // Nearest edge draws solid line
                if (depth == 0)
                    // Return solid wireframe color
                    return WireframeLineColor;
                // Phase dashes through world position
                double phase = (hit.Point.X * 0.37 + hit.Point.Y * 0.73 + hit.Point.Z * 0.91) / Math.Max(pixelWorld * 6.0, 1e-9);
                // Fold phase into unit interval
                double frac = phase - Math.Floor(phase);
                // Draw dash segment color
                if (frac < 0.55)
                    // Return dashed hidden color
                    return WireframeHiddenColor;
                // Dash gaps keep marching deeper
            // End edge pixel branch
            }
            // Advance ray origin past hit
            current = new Ray(
                // Offset past hit point
                hit.Point + current.Direction * Math.Max(1e-4, hit.T * 1e-4),
                // Preserve ray direction
                current.Direction);
        // End hit marching loop
        }
        // Return background when march exhausts
        return Background(ray);
    // End TraceWireframe
    }

    /// <summary>Measures the world-space distance from a hit point to its triangle edges.</summary>
    // Measure hit distance to edges
    private static double EdgeDistanceToTriangle(Hit hit)
    // Begin EdgeDistanceToTriangle
    {
        // Read source triangle vertices
        Triangle source = hit.SourceTriangle;
        // Read hit world position
        Vec3 p = hit.Point;
        // Measure distance to first edge
        double ab = PointSegmentDistance(p, source.A, source.B);
        // Measure distance to second edge
        double bc = PointSegmentDistance(p, source.B, source.C);
        // Measure distance to third edge
        double ca = PointSegmentDistance(p, source.C, source.A);
        // Return nearest edge distance
        return Math.Min(ab, Math.Min(bc, ca));
    // End EdgeDistanceToTriangle
    }

    /// <summary>Measures the distance from a point to a line segment.</summary>
    // Measure point to segment distance
    private static double PointSegmentDistance(Vec3 p, Vec3 a, Vec3 b)
    // Begin PointSegmentDistance
    {
        // Build segment direction vector
        Vec3 ab = b - a;
        // Compute squared segment length
        double denom = ab.Dot(ab);
        // Degenerate segment measures to endpoint
        if (denom < 1e-18)
            // Return distance to endpoint
            return (p - a).Length();
        // Project point onto segment line
        double t = Math.Clamp((p - a).Dot(ab) / denom, 0.0, 1.0);
        // Return distance to closest point
        return (p - (a + ab * t)).Length();
    // End PointSegmentDistance
    }
// End RayTracer class
}
