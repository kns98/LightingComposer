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
    // Bright color for visible wireframe lines
    private static readonly Vec3 WireframeLineColor = new Vec3(0.85, 0.92, 1.0);
    // Dim color for dashed hidden wireframe lines
    private static readonly Vec3 WireframeHiddenColor = new Vec3(0.30, 0.38, 0.52);

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
        // Read transmission factor
        double transmission = hit.Material.Transmission;
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
        // Initialize direct lighting
        Vec3 direct = Vec3.Zero;
        // Loop over scene lights
        foreach (SceneLight light in scene.Lights)
        // Begin light loop
        {
            // Check light enabled
            if (light.Enabled)
                // Accumulate direct light
                direct += DirectLight(hit, light, surfaceColor, normal, view, metallic, roughness);
        // End light loop
        }

        // Sample ambient occlusion
        double occlusion = hit.Material.SampleOcclusion(hit.TextureU, hit.TextureV);
        // Compute occluded environment light
        Vec3 indirect = EnvironmentLighting(surfaceColor, normal, view, metallic, roughness) * occlusion;
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
        // Begin fallback branch
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
        // End fallback branch
        }

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

    /// <summary>Computes direct lighting from one light using its point, spot, or directional model.</summary>
    // Dispatch light by kind
    private Vec3 DirectLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    // Begin DirectLight
    {
        // Switch on light kind
        return light.Kind switch
        // Begin light switch
        {
            // Shade directional light
            SceneLightKind.Directional => DirectDirectionalLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness),
            // Shade spot light
            SceneLightKind.Spot => DirectSpotLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness),
            // Shade point light
            _ => DirectPointLight(hit, light, surfaceColor, normal, viewDirection, metallic, roughness)
        // End light switch
        };
    // End DirectLight
    }

    /// <summary>Computes direct lighting from one point light.</summary>
    // Shade one point light
    private Vec3 DirectPointLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
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
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * shadow, metallic, roughness);
    // End DirectPointLight
    }

    /// <summary>Computes direct lighting from one directional light.</summary>
    // Shade one directional light
    private Vec3 DirectDirectionalLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
    // Begin DirectDirectionalLight
    {
        // glTF directional lights shine along local -Z. SceneLight.Direction is
        // the direction light travels, so surfaces are lit from the opposite ray.
        // Reverse travel into light direction
        Vec3 lightDir = (light.Direction * -1.0).Normalize();
        // Evaluate shadow factor
        double shadow = light.CastsShadow ? ShadowFactor(hit, lightDir, double.PositiveInfinity) : 1.0;
        // Compute shaded contribution
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * shadow, metallic, roughness);
    // End DirectDirectionalLight
    }

    /// <summary>Computes direct lighting from one spot light.</summary>
    // Shade one spot light
    private Vec3 DirectSpotLight(Hit hit, SceneLight light, Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, double metallic, double roughness)
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
        return LightContribution(surfaceColor, normal, viewDirection, lightDir, light.Color, light.Intensity * attenuation * cone * shadow, metallic, roughness);
    // End DirectSpotLight
    }

    /// <summary>Computes the diffuse and specular contribution of one light.</summary>
    // Compute PBR light contribution
    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 view, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
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
        // Evaluate GGX distribution
        double distribution = DistributionGgx(nDotH, alphaRoughness);
        // Evaluate Smith visibility
        double visibility = VisibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        // Interpolate base reflectance
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        // Evaluate Schlick fresnel
        Vec3 fresnel = FresnelSchlick(vDotH, f0);
        // Combine specular BRDF terms
        Vec3 specular = fresnel * (distribution * visibility);
        // Compute diffuse BRDF term
        Vec3 diffuse = (new Vec3(1.0, 1.0, 1.0) - fresnel).Multiply(surfaceColor) * ((1.0 - metallic) / Pi);
        // Scale light by strength
        Vec3 radiance = lightColor * (strength * 0.18);
        // Modulate BRDF by radiance
        return (diffuse + specular).Multiply(radiance) * nDotL;
    // End LightContribution
    }

    /// <summary>Computes diffuse and specular environment lighting for a surface.</summary>
    // Compute IBL for surface
    private static Vec3 EnvironmentLighting(Vec3 baseColor, Vec3 normal, Vec3 view, double metallic, double roughness)
    // Begin EnvironmentLighting
    {
        // Clamp normal view alignment
        double nDotV = Math.Max(0.0001, normal.Dot(view));
        // Interpolate base reflectance
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), baseColor, metallic);
        // Compute grazing angle factor
        double grazing = Math.Pow(1.0 - nDotV, 5.0);
        // Begin roughness reflectance cap
        Vec3 maximum = new(
            // Cap red reflectance channel
            Math.Max(1.0 - roughness, f0.X),
            // Cap green reflectance channel
            Math.Max(1.0 - roughness, f0.Y),
            // Cap blue reflectance channel
            Math.Max(1.0 - roughness, f0.Z));
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
        // Scale combined IBL output
        return (diffuse + specular) * 0.72;
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
    private static Vec3 Background(Ray ray)
    // Begin Background
    {
        // Map direction to blend
        double t = Math.Clamp(ray.Direction.Y * 0.5 + 0.5, 0.0, 1.0);
        // Blend horizon gradient
        return Vec3.Lerp(new Vec3(0.01, 0.012, 0.016), new Vec3(0.055, 0.06, 0.072), t);
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
