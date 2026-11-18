// -----------------------------------------------------------------------------
// File: Rendering/ShadowRasterRenderer.cs
// Purpose: Independent fast raster preview with depth buffering and shadow maps.
//
// This renderer is independent of the UI and the CPU/Vulkan
// ray tracers. It owns a small AMD-style preview pipeline: build reusable shadow
// maps from scene lights, project triangles, rasterize a depth buffer, and shade
// visible pixels with direct lighting. It is not a path tracer and it does not
// call Scene.Intersect for visibility or shadows.
// -----------------------------------------------------------------------------

// Import timing utilities for render profiling
using System.Diagnostics;
// Import cancellation token support
using System.Threading;
// Import camera basis types
using LightingShowcase.CameraSystem;
// Import lighting and shadow types
using LightingShowcase.Lighting;
// Import vector math types
using LightingShowcase.Math3D;
// Import scene graph types
using LightingShowcase.SceneGraph;

// Declare rendering namespace scope
namespace LightingShowcase.Rendering;

/// <summary>Independent realtime-style raster preview renderer with z-buffered triangle drawing and shadow-map shadows.</summary>
// Define static shadow raster renderer
public static class ShadowRasterRenderer
// Begin renderer class block
{
    // Minimum camera projection distance
    private const double CameraNear = 0.035;
    // Preview camera field of view
    private const double CameraFovDegrees = 72.0;
    // Shadow map texel resolution
    private const int ShadowMapSize = 512;
    // Limit shadow casting lights
    private const int MaxShadowCastingLights = 4;
    // Limit interactive preview lights
    private const int MaxInteractiveLights = 2;


    /// <summary>
    /// Reusable raster cache. Shadow maps are view-independent, so they should be
    /// rebuilt only when the scene or lights change, not on every camera orbit.
    /// </summary>
    // Cache reusable shadow map state
    public sealed class PreviewCache
    // Begin preview cache block
    {
        /// <summary>Captures the scene stamp, shadow maps, and build statistics.</summary>
        // Construct cache from shadow maps
        internal PreviewCache(Scene scene, List<ShadowMap> shadowMaps, long buildMilliseconds)
        // Begin cache constructor block
        {
            // Snapshot scene revision stamp
            CacheStamp = SceneCacheStamp.Capture(scene);
            // Store cached scene reference
            Scene = scene;
            // Store built shadow maps
            ShadowMaps = shadowMaps;
            // Record cache build time
            BuildMilliseconds = buildMilliseconds;
            // Record cached triangle count
            TriangleCount = scene.Triangles.Count;
            // Record cached light count
            LightCount = scene.Lights.Count;
            // Count enabled shadow maps
            EnabledShadowMapCount = shadowMaps.Count(m => m.Enabled);
        // End cache constructor block
        }

        // Expose captured scene stamp
        public SceneCacheStamp CacheStamp { get; }
        // Expose cached scene
        public Scene Scene { get; }
        /// <summary>Returns true when the cache stamp still matches the scene.</summary>
        // Check cache against scene
        public bool IsCurrent(Scene scene) => CacheStamp.Matches(scene);
        // Expose cached triangle count
        public int TriangleCount { get; }
        // Expose cached light count
        public int LightCount { get; }
        // Expose enabled shadow count
        public int EnabledShadowMapCount { get; }
        // Expose cache build time
        public long BuildMilliseconds { get; }
        // Expose internal shadow maps
        internal IReadOnlyList<ShadowMap> ShadowMaps { get; }
    // End preview cache block
    }

    /// <summary>Builds reusable shadow-map state for a scene snapshot.</summary>
    // Build shadow cache for scene
    public static PreviewCache BuildCache(Scene scene, CancellationToken cancellationToken)
    // Begin BuildCache block
    {
        // Reject null scene argument
        if (scene == null) throw new ArgumentNullException(nameof(scene));
        // Start cache build timer
        Stopwatch stopwatch = Stopwatch.StartNew();
        // Build light shadow maps
        List<ShadowMap> shadowMaps = BuildShadowMaps(scene, cancellationToken);
        // Stop cache build timer
        stopwatch.Stop();
        // Return populated preview cache
        return new PreviewCache(scene, shadowMaps, stopwatch.ElapsedMilliseconds);
    // End BuildCache block
    }

    /// <summary>Renders one shaded raster preview frame into a cross-platform RGBA image.</summary>
    // Render frame without cached shadows
    public static RenderImage Render(
        // Scene geometry to render
        Scene scene,
        // Camera world position input
        Vec3 cameraPosition,
        // Camera orientation basis input
        CameraBasis cameraBasis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Render statistics output text
        out string details)
    // Begin Render overload block
    {
        // Build fresh shadow cache
        PreviewCache cache = BuildCache(scene, cancellationToken);
        // Delegate to cached render
        return Render(cache, cameraPosition, cameraBasis, width, height, cancellationToken, out details);
    // End Render overload block
    }

    /// <summary>Renders one shaded raster preview frame using a reusable shadow-map cache.</summary>
    // Render frame from preview cache
    public static RenderImage Render(
        // Reusable shadow map cache
        PreviewCache cache,
        // Camera world position input
        Vec3 cameraPosition,
        // Camera orientation basis input
        CameraBasis cameraBasis,
        // Output image width
        int width,
        // Output image height
        int height,
        // Cancellation token for render
        CancellationToken cancellationToken,
        // Render statistics output text
        out string details,
        // Fast preview mode flag
        bool interactiveFast = false)
    // Begin cached Render block
    {
        // Reject null cache argument
        if (cache == null) throw new ArgumentNullException(nameof(cache));
        // Clamp width to minimum
        width = Math.Max(1, width);
        // Clamp height to minimum
        height = Math.Max(1, height);

        // Start frame render timer
        Stopwatch stopwatch = Stopwatch.StartNew();
        // Resolve scene from cache
        Scene scene = cache.Scene;
        // Prepare enabled render lights
        PreparedLight[] lights = PrepareLights(scene, cache.ShadowMaps);
        // Check interactive light limit
        if (interactiveFast && lights.Length > MaxInteractiveLights)
            // Trim lights for speed
            lights = lights.Take(MaxInteractiveLights).ToArray();

        // Allocate output pixel buffer
        uint[] pixels = new uint[checked(width * height)];
        // Allocate depth buffer
        double[] zBuffer = new double[pixels.Length];
        // Reset depth to infinity
        Array.Fill(zBuffer, double.PositiveInfinity);
        // Paint gradient background
        ClearBackground(pixels, width, height);

        // Loop over scene triangles
        foreach (Triangle tri in scene.Triangles)
        // Begin triangle loop
        {
            // Honor render cancellation request
            cancellationToken.ThrowIfCancellationRequested();
            // Rasterize one scene triangle
            RasterizeTriangle(pixels, zBuffer, width, height, tri, cameraPosition, cameraBasis, lights, interactiveFast);
        // End triangle loop
        }

        // Stop frame render timer
        stopwatch.Stop();
        // Format frame statistics string
        details = $"Shadow raster {(interactiveFast ? "20fps preview" : "OK")} - {width}x{height}, revision={cache.CacheStamp.Revision}, triangles={cache.TriangleCount}, lights={(interactiveFast ? lights.Length : cache.LightCount)}, shadowMaps={cache.EnabledShadowMapCount}, cache={cache.BuildMilliseconds}ms, frame={stopwatch.ElapsedMilliseconds}ms";
        // Return completed render image
        return new RenderImage(width, height, pixels);
    // End cached Render block
    }

    /// <summary>Prepares enabled lights with shadow maps and spot cone data.</summary>
    // Prepare lights with shadow data
    private static PreparedLight[] PrepareLights(Scene scene, IReadOnlyList<ShadowMap> shadowMaps)
    // Begin PrepareLights block
    {
        // Index shadow maps by light
        Dictionary<string, ShadowMap> shadowByLightId = new(StringComparer.OrdinalIgnoreCase);
        // Loop over built shadow maps
        foreach (ShadowMap map in shadowMaps)
        // Begin shadow map loop
        {
            // Skip maps without light id
            if (!string.IsNullOrWhiteSpace(map.LightId))
                // Store map under light id
                shadowByLightId[map.LightId] = map;
        // End shadow map loop
        }

        // Collect prepared render lights
        List<PreparedLight> prepared = new();
        // Loop over scene lights
        foreach (SceneLight light in scene.Lights)
        // Begin light loop
        {
            // Skip disabled scene lights
            if (!light.Enabled)
                // Continue to next light
                continue;

            // Look up light shadow map
            shadowByLightId.TryGetValue(light.Id, out ShadowMap? shadowMap);
            // Compute directional light vector
            Vec3 directionalLightDirection = light.Kind == SceneLightKind.Directional
                // Negate and normalize light direction
                ? (light.Direction * -1.0).Normalize()
                // Default non-directional vector zero
                : Vec3.Zero;
            // Compute spot forward vector
            Vec3 spotForward = light.Kind == SceneLightKind.Spot
                // Normalize spot light direction
                ? light.Direction.Normalize()
                // Default non-spot vector zero
                : Vec3.Zero;
            // Precompute spot outer cosine
            double cosOuter = light.Kind == SceneLightKind.Spot ? Math.Cos(light.OuterConeAngle) : 0.0;
            // Precompute spot inner cosine
            double cosInner = light.Kind == SceneLightKind.Spot ? Math.Cos(Math.Min(light.InnerConeAngle, light.OuterConeAngle)) : 1.0;
            // Append prepared light entry
            prepared.Add(new PreparedLight(light, shadowMap, directionalLightDirection, spotForward, cosOuter, cosInner));
        // End light loop
        }

        // Return prepared light array
        return prepared.ToArray();
    // End PrepareLights block
    }

    /// <summary>Clears the frame with a vertical gradient background.</summary>
    // Fill frame with vertical gradient
    private static void ClearBackground(uint[] pixels, int width, int height)
    // Begin ClearBackground block
    {
        // Loop over image rows
        for (int y = 0; y < height; y++)
        // Begin background row loop
        {
            // Compute vertical gradient factor
            double t = height <= 1 ? 0.0 : y / (double)(height - 1);
            // Interpolate background gradient color
            Vec3 color = Vec3.Lerp(new Vec3(0.055, 0.060, 0.074), new Vec3(0.010, 0.012, 0.016), t);
            // Pack gradient color to RGBA
            uint packed = PackRgba(
                // Convert red channel byte
                ToByte(Gamma(color.X)),
                // Convert green channel byte
                ToByte(Gamma(color.Y)),
                // Convert blue channel byte
                ToByte(Gamma(color.Z)),
                // Set opaque alpha channel
                255);
            // Compute row start offset
            int row = y * width;
            // Fill row with color
            Array.Fill(pixels, packed, row, width);
        // End background row loop
        }
    // End ClearBackground block
    }

    /// <summary>Projects and shades one triangle into the pixel and depth buffers.</summary>
    // Rasterize single shaded triangle
    private static void RasterizeTriangle(
        // Output pixel buffer target
        uint[] pixels,
        // Depth buffer for occlusion
        double[] zBuffer,
        // Target image width
        int width,
        // Target image height
        int height,
        // Triangle to rasterize
        Triangle tri,
        // Camera world position input
        Vec3 cameraPosition,
        // Camera orientation basis
        CameraBasis basis,
        // Prepared lights for shading
        IReadOnlyList<PreparedLight> lights,
        // Fast preview mode flag
        bool interactiveFast)
    // Begin RasterizeTriangle block
    {
        // Project triangle vertex A
        if (!TryProjectCamera(tri.A, cameraPosition, basis, width, height, out RasterVertex a) ||
            // Project triangle vertex B
            !TryProjectCamera(tri.B, cameraPosition, basis, width, height, out RasterVertex b) ||
            // Project triangle vertex C
            !TryProjectCamera(tri.C, cameraPosition, basis, width, height, out RasterVertex c))
            // Skip unprojectable triangle
            return;

        // Compute screen triangle area
        double area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        // Detect degenerate tiny triangle
        if (Math.Abs(area) < 1e-9)
            // Skip degenerate triangle
            return;

        // Clamp bounding box left
        int minX = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, width - 1);
        // Clamp bounding box right
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, width - 1);
        // Clamp bounding box top
        int minY = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, height - 1);
        // Clamp bounding box bottom
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, height - 1);
        // Detect empty bounding box
        if (maxX < minX || maxY < minY)
            // Skip offscreen triangle
            return;

        // Precompute vertex A depth reciprocal
        double invZa = 1.0 / Math.Max(CameraNear, a.Z);
        // Precompute vertex B depth reciprocal
        double invZb = 1.0 / Math.Max(CameraNear, b.Z);
        // Precompute vertex C depth reciprocal
        double invZc = 1.0 / Math.Max(CameraNear, c.Z);
        // Normalize triangle surface normal
        Vec3 normal = tri.Normal.Normalize();
        // Compute triangle center point
        Vec3 triangleCenter = (tri.A + tri.B + tri.C) / 3.0;
        // Test normal against camera
        if (normal.Dot(cameraPosition - triangleCenter) < 0.0)
            // Flip normal toward camera
            normal = normal * -1.0;

        // Average fast path U
        double fastU = (tri.UvA.U + tri.UvB.U + tri.UvC.U) / 3.0;
        // Average fast path V
        double fastV = (tri.UvA.V + tri.UvB.V + tri.UvC.V) / 3.0;
        // Sample flat preview albedo
        Vec3 fastAlbedo = interactiveFast ? tri.Material.Sample(fastU, fastV) : Vec3.Zero;
        // Sample flat preview alpha
        double fastAlpha = interactiveFast ? tri.Material.SampleAlpha(fastU, fastV) : 1.0;
        // Detect transparent fast triangle
        if (interactiveFast && fastAlpha < 0.04)
            // Skip transparent fast triangle
            return;

        // Loop over bounding rows
        for (int y = minY; y <= maxY; y++)
        // Begin pixel row loop
        {
            // Compute pixel center Y
            double py = y + 0.5;
            // Compute row start offset
            int row = y * width;
            // Loop over bounding columns
            for (int x = minX; x <= maxX; x++)
            // Begin pixel column loop
            {
                // Compute pixel center X
                double px = x + 0.5;
                // Compute barycentric weight A
                double wa = Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                // Compute barycentric weight B
                double wb = Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                // Derive barycentric weight C
                double wc = 1.0 - wa - wb;
                // Reject pixels outside triangle
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    // Continue to next pixel
                    continue;

                // Interpolate pixel depth
                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                // Compute pixel buffer index
                int pixel = y * width + x;
                // Depth test against buffer
                if (depth >= zBuffer[pixel])
                    // Skip occluded pixel
                    continue;

                // Interpolate world position
                Vec3 world = tri.A * wa + tri.B * wb + tri.C * wc;
                // Declare shaded pixel color
                Vec3 shaded;
                // Branch on preview mode
                if (interactiveFast)
                // Begin fast shade block
                {
                    // Shade pixel with fast path
                    shaded = ShadeFast(fastAlbedo, world, normal, lights);
                // End fast shade block
                }
                // Handle full quality branch
                else
                // Begin full shade block
                {
                    // Interpolate depth reciprocal
                    double invZ = wa * invZa + wb * invZb + wc * invZc;
                    // Perspective correct U coordinate
                    double u = ((wa * tri.UvA.U * invZa) + (wb * tri.UvB.U * invZb) + (wc * tri.UvC.U * invZc)) / Math.Max(1e-12, invZ);
                    // Perspective correct V coordinate
                    double v = ((wa * tri.UvA.V * invZa) + (wb * tri.UvB.V * invZb) + (wc * tri.UvC.V * invZc)) / Math.Max(1e-12, invZ);
                    // Sample material alpha
                    double alpha = tri.Material.SampleAlpha(u, v);
                    // Detect transparent shaded pixel
                    if (alpha < 0.04)
                        // Skip transparent pixel
                        continue;

                    // Compute view direction vector
                    Vec3 viewDirection = (cameraPosition - world).Normalize();
                    // Shade pixel fully
                    shaded = Shade(tri.Material, u, v, world, normal, viewDirection, lights);
                // End full shade block
                }
                // Record winning pixel depth
                zBuffer[pixel] = depth;

                // Pack shaded color to RGBA
                pixels[row + x] = PackRgba(
                    // Convert red channel byte
                    ToByte(Gamma(shaded.X)),
                    // Convert green channel byte
                    ToByte(Gamma(shaded.Y)),
                    // Convert blue channel byte
                    ToByte(Gamma(shaded.Z)),
                    // Set opaque alpha channel
                    255);
            // End pixel column loop
            }
        // End pixel row loop
        }
    // End RasterizeTriangle block
    }

    /// <summary>Shades one point with fast diffuse-only lighting.</summary>
    // Shade point with diffuse preview
    private static Vec3 ShadeFast(Vec3 albedo, Vec3 point, Vec3 normal, IReadOnlyList<PreparedLight> lights)
    // Begin ShadeFast block
    {
        // Seed ambient albedo term
        Vec3 color = albedo * 0.085;

        // Loop over prepared lights
        foreach (PreparedLight prepared in lights)
        // Begin fast light loop
        {
            // Resolve current scene light
            SceneLight light = prepared.Light;
            // Declare light direction vector
            Vec3 lightDir;
            // Read light intensity strength
            double strength = light.Intensity;
            // Default attenuation to one
            double attenuation = 1.0;
            // Default cone factor one
            double cone = 1.0;

            // Branch on directional light
            if (light.Kind == SceneLightKind.Directional)
            // Begin directional branch
            {
                // Use prepared directional vector
                lightDir = prepared.DirectionalLightDirection;
            // End directional branch
            }
            // Handle positional light branch
            else
            // Begin positional branch
            {
                // Compute vector to light
                Vec3 toLight = light.Position - point;
                // Compute squared light distance
                double distanceSquared = toLight.Dot(toLight);
                // Detect degenerate light distance
                if (distanceSquared < 1e-12)
                    // Skip degenerate light sample
                    continue;
                // Compute light distance
                double distance = Math.Sqrt(distanceSquared);
                // Test light range cutoff
                if (light.Range > 0.0 && distance > light.Range)
                    // Skip out of range light
                    continue;

                // Normalize light direction vector
                lightDir = toLight / distance;
                // Compute distance attenuation factor
                attenuation = DistanceAttenuation(distance, light.Range);
                // Branch on spotlight cone
                if (light.Kind == SceneLightKind.Spot)
                // Begin spot cone block
                {
                    // Evaluate spot cone factor
                    cone = SpotConeFactor(prepared, lightDir * -1.0);
                    // Detect light outside cone
                    if (cone <= 0.0)
                        // Skip light outside cone
                        continue;
                // End spot cone block
                }
            // End positional branch
            }

            // Compute diffuse cosine term
            double ndotl = Math.Max(0.0, normal.Dot(lightDir));
            // Detect backfacing light
            if (ndotl <= 0.0)
                // Skip backfacing light
                continue;

            // Sample fast shadow factor
            double shadow = light.CastsShadow ? FindShadowFactorFast(prepared.ShadowMap, point, normal) : 1.0;
            // Accumulate diffuse light contribution
            color += albedo.Multiply(light.Color) * (ndotl * strength * attenuation * cone * shadow * 0.18);
        // End fast light loop
        }

        // Return clamped preview color
        return ClampColor(color);
    // End ShadeFast block
    }

    /// <summary>Shades one point with diffuse, specular, shadow, and emission terms.</summary>
    // Shade point with full lighting
    private static Vec3 Shade(
        // Surface material for shading
        Material material,
        // Surface texture U coordinate
        double u,
        // Surface texture V coordinate
        double v,
        // World shade point
        Vec3 point,
        // Surface normal vector
        Vec3 normal,
        // Direction toward camera
        Vec3 viewDirection,
        // Prepared lights for shading
        IReadOnlyList<PreparedLight> lights)
    // Begin Shade block
    {
        // Sample surface albedo
        Vec3 albedo = material.Sample(u, v);
        // Sample metallic roughness pair
        (double metallic, double roughness) = material.SampleMetallicRoughness(u, v);
        // Seed ambient albedo term
        Vec3 color = albedo * (0.075 + 0.035 * (1.0 - metallic));

        // Loop over prepared lights
        foreach (PreparedLight prepared in lights)
        // Begin full light loop
        {
            // Resolve current scene light
            SceneLight light = prepared.Light;
            // Declare light direction vector
            Vec3 lightDir;
            // Read light intensity strength
            double strength = light.Intensity;
            // Default attenuation to one
            double attenuation = 1.0;
            // Default cone factor one
            double cone = 1.0;

            // Branch on directional light
            if (light.Kind == SceneLightKind.Directional)
            // Begin directional branch
            {
                // Use prepared directional vector
                lightDir = prepared.DirectionalLightDirection;
            // End directional branch
            }
            // Handle positional light branch
            else
            // Begin positional branch
            {
                // Compute vector to light
                Vec3 toLight = light.Position - point;
                // Compute light distance
                double distance = toLight.Length();
                // Test range and degenerate distance
                if (distance < 1e-8 || (light.Range > 0.0 && distance > light.Range))
                    // Skip invalid light sample
                    continue;

                // Normalize light direction vector
                lightDir = toLight / distance;
                // Compute distance attenuation factor
                attenuation = DistanceAttenuation(distance, light.Range);
                // Branch on spotlight cone
                if (light.Kind == SceneLightKind.Spot)
                // Begin spot cone block
                {
                    // Reverse light direction vector
                    Vec3 lightToSurface = lightDir * -1.0;
                    // Evaluate spot cone factor
                    cone = SpotConeFactor(prepared, lightToSurface);
                    // Detect light outside cone
                    if (cone <= 0.0)
                        // Skip light outside cone
                        continue;
                // End spot cone block
                }
            // End positional branch
            }

            // Sample soft shadow factor
            double shadow = light.CastsShadow ? FindShadowFactor(prepared.ShadowMap, point, normal) : 1.0;
            // Accumulate full light contribution
            color += LightContribution(albedo, normal, viewDirection, lightDir, light.Color, strength * attenuation * cone * shadow, metallic, roughness);
        // End full light loop
        }

        // Add material emission term
        color += material.SampleEmission(u, v);
        // Return clamped shaded color
        return ClampColor(color);
    // End Shade block
    }

    /// <summary>Computes diffuse plus specular contribution from one light.</summary>
    // Compute diffuse plus specular term
    private static Vec3 LightContribution(Vec3 surfaceColor, Vec3 normal, Vec3 viewDirection, Vec3 lightDir, Vec3 lightColor, double strength, double metallic, double roughness)
    // Begin LightContribution block
    {
        // Compute diffuse cosine term
        double ndotl = Math.Max(0.0, normal.Dot(lightDir));
        // Detect zero light contribution
        if (ndotl <= 0.0 || strength <= 0.0)
            // Return black for darkness
            return Vec3.Zero;

        // Preview exposure scale factor
        double previewScale = 0.18;
        // Compute metallic scaled diffuse
        Vec3 diffuse = surfaceColor.Multiply(lightColor) * ((1.0 - metallic) * ndotl * strength * previewScale);
        // Compute Blinn halfway vector
        Vec3 halfVector = (lightDir + viewDirection).Normalize();
        // Compute specular cosine term
        double ndoth = Math.Max(0.0, normal.Dot(halfVector));
        // Derive shininess from roughness
        double shininess = Math.Clamp(2.0 / (roughness * roughness) - 2.0, 2.0, 192.0);
        // Interpolate Fresnel base reflectance
        Vec3 f0 = Vec3.Lerp(new Vec3(0.04, 0.04, 0.04), surfaceColor, metallic);
        // Compute specular highlight strength
        double specularTerm = Math.Pow(ndoth, shininess) * ndotl * strength * previewScale * (1.0 - roughness * 0.65);
        // Tint specular by light
        Vec3 specular = f0.Multiply(lightColor) * (specularTerm * (1.0 + metallic));
        // Return diffuse plus specular
        return diffuse + specular;
    // End LightContribution block
    }

    /// <summary>Computes distance attenuation with smooth range falloff.</summary>
    // Compute range faded attenuation
    private static double DistanceAttenuation(double distance, double range)
    // Begin DistanceAttenuation block
    {
        // Apply inverse square falloff
        double attenuation = 1.0 / (1.0 + 0.11 * distance * distance);
        // Detect unlimited light range
        if (range <= 0.0)
            // Return unclipped attenuation
            return attenuation;

        // Normalize distance by range
        double normalized = Math.Clamp(distance / range, 0.0, 1.0);
        // Compute smooth range fade
        double edgeFade = 1.0 - normalized * normalized;
        // Return faded attenuation value
        return attenuation * edgeFade * edgeFade;
    // End DistanceAttenuation block
    }

    /// <summary>Computes the smooth spot cone falloff for one direction.</summary>
    // Compute smooth spotlight falloff
    private static double SpotConeFactor(PreparedLight prepared, Vec3 lightToSurface)
    // Begin SpotConeFactor block
    {
        // Compute cone alignment cosine
        double theta = prepared.SpotForward.Dot(lightToSurface.Normalize());
        // Reject rays beyond cone
        if (theta <= prepared.CosOuter) return 0.0;
        // Accept rays inside hotspot
        if (theta >= prepared.CosInner) return 1.0;
        // Interpolate penumbra falloff factor
        return (theta - prepared.CosOuter) / Math.Max(1e-8, prepared.CosInner - prepared.CosOuter);
    // End SpotConeFactor block
    }

    /// <summary>Builds one shadow map per shadow-casting light.</summary>
    // Build maps for shadow lights
    private static List<ShadowMap> BuildShadowMaps(Scene scene, CancellationToken cancellationToken)
    // Begin BuildShadowMaps block
    {
        // Collect generated shadow maps
        List<ShadowMap> maps = new();
        // Resolve scene bounds fallback
        Aabb bounds = scene.GetSceneBounds() ?? new Aabb(new Vec3(-2, -2, -2), new Vec3(2, 2, 2));
        // Compute scene center point
        Vec3 center = (bounds.Min + bounds.Max) * 0.5;
        // Compute scene bounding radius
        double radius = Math.Max(0.35, (bounds.Max - bounds.Min).Length() * 0.5);

        // Loop over shadow casting lights
        foreach (SceneLight light in scene.Lights.Where(l => l.Enabled && l.CastsShadow).Take(MaxShadowCastingLights))
        // Begin shadow light loop
        {
            // Honor cache build cancellation
            cancellationToken.ThrowIfCancellationRequested();
            // Create light shadow projection
            ShadowProjection projection = CreateShadowProjection(light, center, radius);
            // Allocate empty shadow map
            ShadowMap map = new(light.Id, ShadowMapSize, projection);
            // Loop over scene triangles
            foreach (Triangle tri in scene.Triangles)
                // Rasterize triangle into shadow map
                RasterizeShadowTriangle(map, tri);
            // Store completed shadow map
            maps.Add(map);
        // End shadow light loop
        }

        // Return built shadow maps
        return maps;
    // End BuildShadowMaps block
    }

    /// <summary>Creates the shadow projection matching the light kind.</summary>
    // Create projection for light kind
    private static ShadowProjection CreateShadowProjection(SceneLight light, Vec3 center, double radius)
    // Begin CreateShadowProjection block
    {
        // Branch on directional light
        if (light.Kind == SceneLightKind.Directional)
        // Begin directional shadow block
        {
            // Normalize directional light vector
            Vec3 directionalForward = light.Direction.Normalize();
            // Detect degenerate light direction
            if (directionalForward.Length() < 1e-8)
                // Substitute default sun direction
                directionalForward = new Vec3(0.45, -0.75, 0.55).Normalize();
            // Place orthographic shadow origin
            Vec3 origin = center - directionalForward * (radius * 2.5);
            // Return orthographic shadow projection
            return ShadowProjection.CreateOrthographic(origin, directionalForward, radius * 1.45, radius * 5.0, bias: radius * 0.0025 + 0.0015);
        // End directional shadow block
        }

        // Select positional shadow forward
        Vec3 forward = light.Kind == SceneLightKind.Spot
            // Use spot light direction
            ? light.Direction.Normalize()
            // Aim point light inward
            : (center - light.Position).Normalize();
        // Detect degenerate forward vector
        if (forward.Length() < 1e-8)
            // Substitute downward forward vector
            forward = new Vec3(0, -1, 0);

        // Select shadow frustum angle
        double fov = light.Kind == SceneLightKind.Spot
            // Clamp spotlight cone angle
            ? Math.Clamp(light.OuterConeAngle * 2.20, 18.0 * Math.PI / 180.0, 140.0 * Math.PI / 180.0)
            // Use wide point angle
            : Math.PI * 0.72;
        // Compute shadow far plane
        double far = light.Range > 0.0 ? Math.Max(light.Range, radius * 2.0) : radius * 5.0;
        // Return perspective shadow projection
        return ShadowProjection.CreatePerspective(light.Position, forward, fov, far, bias: radius * 0.0035 + 0.0025);
    // End CreateShadowProjection block
    }

    /// <summary>Rasterizes one triangle into a shadow map depth buffer.</summary>
    // Draw triangle into shadow map
    private static void RasterizeShadowTriangle(ShadowMap map, Triangle tri)
    // Begin RasterizeShadowTriangle block
    {
        // Project shadow vertex A
        if (!map.Projection.TryProject(tri.A, map.Size, map.Size, out RasterVertex a) ||
            // Project shadow vertex B
            !map.Projection.TryProject(tri.B, map.Size, map.Size, out RasterVertex b) ||
            // Project shadow vertex C
            !map.Projection.TryProject(tri.C, map.Size, map.Size, out RasterVertex c))
            // Skip unprojectable shadow triangle
            return;

        // Compute shadow triangle area
        double area = Edge(a.X, a.Y, b.X, b.Y, c.X, c.Y);
        // Detect degenerate shadow triangle
        if (Math.Abs(area) < 1e-9)
            // Skip degenerate shadow triangle
            return;

        // Clamp shadow box left
        int minX = Math.Clamp((int)Math.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), 0, map.Size - 1);
        // Clamp shadow box right
        int maxX = Math.Clamp((int)Math.Ceiling(Math.Max(a.X, Math.Max(b.X, c.X))), 0, map.Size - 1);
        // Clamp shadow box top
        int minY = Math.Clamp((int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))), 0, map.Size - 1);
        // Clamp shadow box bottom
        int maxY = Math.Clamp((int)Math.Ceiling(Math.Max(a.Y, Math.Max(b.Y, c.Y))), 0, map.Size - 1);

        // Loop over shadow rows
        for (int y = minY; y <= maxY; y++)
        // Begin shadow row loop
        {
            // Compute texel center Y
            double py = y + 0.5;
            // Loop over shadow columns
            for (int x = minX; x <= maxX; x++)
            // Begin shadow column loop
            {
                // Compute texel center X
                double px = x + 0.5;
                // Compute barycentric weight A
                double wa = Edge(b.X, b.Y, c.X, c.Y, px, py) / area;
                // Compute barycentric weight B
                double wb = Edge(c.X, c.Y, a.X, a.Y, px, py) / area;
                // Derive barycentric weight C
                double wc = 1.0 - wa - wb;
                // Reject texels outside triangle
                if (wa < -1e-7 || wb < -1e-7 || wc < -1e-7)
                    // Continue to next texel
                    continue;

                // Interpolate shadow depth
                double depth = wa * a.Z + wb * b.Z + wc * c.Z;
                // Compute depth buffer index
                int index = y * map.Size + x;
                // Test nearer shadow depth
                if (depth < map.Depth[index])
                // Begin depth update block
                {
                    // Record nearer shadow depth
                    map.Depth[index] = depth;
                    // Mark shadow map nonempty
                    map.HasDepth = true;
                // End depth update block
                }
            // End shadow column loop
            }
        // End shadow row loop
        }
    // End RasterizeShadowTriangle block
    }

    /// <summary>Samples one shadow texel for the fast preview path.</summary>
    // Sample single shadow texel
    private static double FindShadowFactorFast(ShadowMap? map, Vec3 point, Vec3 normal)
    // Begin FindShadowFactorFast block
    {
        // Detect missing disabled map
        if (map == null || !map.Enabled)
            // Return full light value
            return 1.0;

        // Offset point along normal
        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        // Project point into map
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out RasterVertex p))
            // Treat unprojectable point as fully lit
            return 1.0;

        // Round projected X to texel column
        int sx = (int)Math.Round(p.X);
        // Round projected Y to texel row
        int sy = (int)Math.Round(p.Y);
        // Skip texels outside shadow map
        if (sx < 0 || sx >= map.Size || sy < 0 || sy >= map.Size)
            // Treat out-of-range sample as lit
            return 1.0;

        // Fetch stored occluder depth here
        double storedDepth = map.Depth[sy * map.Size + sx];
        // Compare depth against bias for visibility
        double visibility = (!double.IsFinite(storedDepth) || p.Z <= storedDepth + map.Projection.Bias) ? 1.0 : 0.0;
        // Blend shadow floor with lit result
        return 0.30 + visibility * 0.70;
    // End fast shadow factor sampler
    }

    /// <summary>Samples a 3 by 3 shadow kernel for soft shadows.</summary>
    // Declare soft 3x3 shadow factor sampler
    private static double FindShadowFactor(ShadowMap? map, Vec3 point, Vec3 normal)
    // Begin soft shadow factor sampler
    {
        // Skip shadows when map unavailable
        if (map == null || !map.Enabled)
            // Return full light without shadows
            return 1.0;

        // Offset point along normal against acne
        Vec3 biasedPoint = point + normal * map.Projection.Bias;
        // Project biased point into shadow map
        if (!map.Projection.TryProject(biasedPoint, map.Size, map.Size, out RasterVertex p))
            // Treat unprojectable point as lit
            return 1.0;

        // Round projected X to kernel center
        int centerX = (int)Math.Round(p.X);
        // Round projected Y to kernel center
        int centerY = (int)Math.Round(p.Y);
        // Initialize lit sample counter
        double lit = 0.0;
        // Initialize total sample counter
        double samples = 0.0;
        // Loop over 3x3 kernel rows
        for (int oy = -1; oy <= 1; oy++)
        // Begin kernel row loop
        {
            // Compute kernel sample row
            int sy = centerY + oy;
            // Skip rows outside shadow map
            if (sy < 0 || sy >= map.Size)
                // Skip to next kernel row
                continue;
            // Loop over kernel columns
            for (int ox = -1; ox <= 1; ox++)
            // Begin kernel column loop
            {
                // Compute kernel sample column
                int sx = centerX + ox;
                // Skip columns outside shadow map
                if (sx < 0 || sx >= map.Size)
                    // Skip to next kernel column
                    continue;

                // Fetch stored depth for kernel texel
                double storedDepth = map.Depth[sy * map.Size + sx];
                // Test texel depth for lit sample
                if (!double.IsFinite(storedDepth) || p.Z <= storedDepth + map.Projection.Bias)
                    // Count this texel as lit
                    lit += 1.0;
                // Count this texel as sampled
                samples += 1.0;
            // End kernel column loop
            }
        // End kernel row loop
        }

        // Handle fully out-of-range kernel
        if (samples <= 0.0)
            // Treat unsampled kernel as lit
            return 1.0;

        // Average lit fraction across kernel
        double visibility = lit / samples;
        // Blend soft shadow floor with visibility
        return Math.Clamp(0.24 + visibility * 0.76, 0.24, 1.0);
    // End soft shadow factor sampler
    }

    /// <summary>Projects one world point to raster coordinates.</summary>
    // Declare camera world-to-raster projection
    private static bool TryProjectCamera(Vec3 p, Vec3 cameraPosition, CameraBasis basis, int width, int height, out RasterVertex projected)
    // Begin camera projection
    {
        // Compute point relative to camera
        Vec3 rel = p - cameraPosition;
        // Project relative vector onto forward
        double z = rel.Dot(basis.Forward);
        // Reject points behind near plane
        if (!double.IsFinite(z) || z <= CameraNear)
        // Begin near-plane rejection block
        {
            // Clear output for failed projection
            projected = default;
            // Report projection failure here
            return false;
        // End near-plane rejection block
        }

        // Compute viewport aspect ratio
        double aspect = width / (double)Math.Max(1, height);
        // Compute tangent of half field of view
        double fov = Math.Tan((CameraFovDegrees * Math.PI / 180.0) * 0.5);
        // Compute normalized horizontal camera coordinate
        double u = rel.Dot(basis.Right) / z;
        // Compute normalized vertical camera coordinate
        double v = rel.Dot(basis.Up) / z;
        // Map horizontal coordinate to pixels
        double x = (1.0 - u / (aspect * fov)) * 0.5 * width;
        // Map vertical coordinate to pixels
        double y = (1.0 - v / fov) * 0.5 * height;
        // Reject non-finite raster coordinates
        if (!double.IsFinite(x) || !double.IsFinite(y))
        // Begin invalid-coordinate rejection block
        {
            // Clear output for invalid coordinates
            projected = default;
            // Report invalid coordinate failure
            return false;
        // End invalid-coordinate rejection block
        }

        // Store projected raster vertex
        projected = new RasterVertex(x, y, z);
        // Report successful camera projection
        return true;
    // End camera projection
    }

    /// <summary>Evaluates the signed edge function for rasterization.</summary>
    // Declare signed edge function helper
    private static double Edge(double ax, double ay, double bx, double by, double px, double py) =>
        // Evaluate cross product edge test
        (px - ax) * (by - ay) - (py - ay) * (bx - ax);

    /// <summary>Clamps a color to the preview range.</summary>
    // Declare preview color clamping helper
    private static Vec3 ClampColor(Vec3 color) => new(
        // Clamp red channel to preview range
        Math.Clamp(color.X, 0.0, 8.0),
        // Clamp green channel to preview range
        Math.Clamp(color.Y, 0.0, 8.0),
        // Clamp blue channel and close constructor
        Math.Clamp(color.Z, 0.0, 8.0));

    /// <summary>Applies gamma correction to one channel.</summary>
    // Apply gamma correction to channel
    private static double Gamma(double value) => Math.Pow(Math.Clamp(value, 0.0, 1.0), 1.0 / 2.2);
    /// <summary>Packs four bytes into one RGBA pixel.</summary>
    // Declare RGBA byte packing helper
    private static uint PackRgba(byte r, byte g, byte b, byte a) =>
        // Pack bytes into RGBA uint
        (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>Converts one unit channel value to a byte.</summary>
    // Convert unit channel value to byte
    private static byte ToByte(double value) => (byte)Math.Clamp((int)Math.Round(value * 255.0), 0, 255);

    // Define raster-space vertex record struct
    internal readonly record struct RasterVertex(double X, double Y, double Z);

    // Declare prepared light record struct
    private readonly record struct PreparedLight(
        // Store source scene light reference
        SceneLight Light,
        // Store optional shadow map reference
        ShadowMap? ShadowMap,
        // Store cached directional light direction
        Vec3 DirectionalLightDirection,
        // Store cached spotlight forward vector
        Vec3 SpotForward,
        // Store spotlight outer cone cosine
        double CosOuter,
        // Store spotlight inner cone cosine
        double CosInner);

    // Declare shadow map depth container
    internal sealed class ShadowMap
    // Begin ShadowMap class
    {
        // Identify owning light for map
        public string LightId { get; }
        // Store shadow map resolution
        public int Size { get; }
        // Store light-space projection mapping
        public ShadowProjection Projection { get; }
        // Store per-texel depth buffer
        public double[] Depth { get; }
        // Report shadows ready when depth exists
        public bool Enabled => HasDepth;
        // Track whether any depth rasterized
        internal bool HasDepth { get; set; }

        /// <summary>Initializes an empty shadow map depth buffer.</summary>
        // Declare shadow map depth initializer
        public ShadowMap(string lightId, int size, ShadowProjection projection)
        // Begin shadow map constructor
        {
            // Assign owning light identifier
            LightId = lightId;
            // Assign shadow map resolution
            Size = size;
            // Assign light-space projection
            Projection = projection;
            // Allocate per-texel depth buffer
            Depth = new double[size * size];
            // Initialize depth buffer to infinity
            Array.Fill(Depth, double.PositiveInfinity);
        // End shadow map constructor
        }
    // End ShadowMap class
    }

    // Declare light-space projection struct
    internal readonly struct ShadowProjection
    // Begin ShadowProjection struct
    {
        // Store projection origin position
        private readonly Vec3 origin;
        // Store projection forward axis
        private readonly Vec3 forward;
        // Store projection right axis
        private readonly Vec3 right;
        // Store projection up axis
        private readonly Vec3 up;
        // Flag perspective versus orthographic mode
        private readonly bool perspective;
        // Store orthographic half extent
        private readonly double halfExtent;
        // Store perspective tangent half angle
        private readonly double tanHalfFov;
        // Store maximum shadow distance
        private readonly double farPlane;
        // Expose depth bias against acne
        public double Bias { get; }

        /// <summary>Initializes a shadow projection with its basis and clipping planes.</summary>
        // Declare projection field initializer
        private ShadowProjection(Vec3 origin, Vec3 forward, Vec3 right, Vec3 up, bool perspective, double halfExtent, double tanHalfFov, double farPlane, double bias)
        // Begin projection constructor
        {
            // Assign projection origin
            this.origin = origin;
            // Assign projection forward axis
            this.forward = forward;
            // Assign projection right axis
            this.right = right;
            // Assign projection up axis
            this.up = up;
            // Assign projection mode flag
            this.perspective = perspective;
            // Clamp orthographic half extent positive
            this.halfExtent = Math.Max(1e-5, halfExtent);
            // Clamp tangent half angle positive
            this.tanHalfFov = Math.Max(1e-5, tanHalfFov);
            // Clamp far plane distance positive
            this.farPlane = Math.Max(1e-5, farPlane);
            // Clamp depth bias to minimum
            Bias = Math.Max(0.0001, bias);
        // End projection constructor
        }

        /// <summary>Creates an orthographic shadow projection.</summary>
        // Declare orthographic projection factory
        public static ShadowProjection CreateOrthographic(Vec3 origin, Vec3 forward, double halfExtent, double farPlane, double bias)
        // Begin orthographic factory
        {
            // Build orthonormal basis from forward
            CreateBasis(forward, out Vec3 f, out Vec3 r, out Vec3 u);
            // Construct orthographic projection instance
            return new ShadowProjection(origin, f, r, u, false, halfExtent, 1.0, farPlane, bias);
        // End orthographic factory
        }

        /// <summary>Creates a perspective shadow projection.</summary>
        // Declare perspective projection factory
        public static ShadowProjection CreatePerspective(Vec3 origin, Vec3 forward, double fovRadians, double farPlane, double bias)
        // Begin perspective factory
        {
            // Build orthonormal basis from forward
            CreateBasis(forward, out Vec3 f, out Vec3 r, out Vec3 u);
            // Construct perspective projection instance
            return new ShadowProjection(origin, f, r, u, true, 1.0, Math.Tan(fovRadians * 0.5), farPlane, bias);
        // End perspective factory
        }

        /// <summary>Projects one world point into shadow map coordinates.</summary>
        // Declare world-to-shadow map projection
        public bool TryProject(Vec3 point, int width, int height, out RasterVertex projected)
        // Begin shadow projection
        {
            // Compute point relative to origin
            Vec3 rel = point - origin;
            // Project relative vector onto forward
            double z = rel.Dot(forward);
            // Reject points outside depth range
            if (!double.IsFinite(z) || z <= CameraNear || z > farPlane)
            // Begin depth rejection block
            {
                // Clear output for rejected depth
                projected = default;
                // Report depth rejection failure
                return false;
            // End depth rejection block
            }

            // Project relative vector onto right
            double xComponent = rel.Dot(right);
            // Project relative vector onto up
            double yComponent = rel.Dot(up);
            // Declare normalized horizontal coordinate
            double normalizedX;
            // Declare normalized vertical coordinate
            double normalizedY;
            // Branch on perspective projection mode
            if (perspective)
            // Begin perspective normalization block
            {
                // Normalize X by perspective depth
                normalizedX = xComponent / (z * tanHalfFov);
                // Normalize Y by perspective depth
                normalizedY = yComponent / (z * tanHalfFov);
                // Reject coordinates outside frustum margin
                if (normalizedX < -1.12 || normalizedX > 1.12 || normalizedY < -1.12 || normalizedY > 1.12)
                // Begin frustum rejection block
                {
                    // Clear output for frustum rejection
                    projected = default;
                    // Report frustum rejection failure
                    return false;
                // End frustum rejection block
                }
            // End perspective normalization block
            }
            // Handle orthographic projection branch
            else
            // Begin orthographic normalization block
            {
                // Normalize X by orthographic extent
                normalizedX = xComponent / halfExtent;
                // Normalize Y by orthographic extent
                normalizedY = yComponent / halfExtent;
                // Reject coordinates outside ortho margin
                if (normalizedX < -1.12 || normalizedX > 1.12 || normalizedY < -1.12 || normalizedY > 1.12)
                // Begin ortho rejection block
                {
                    // Clear output for ortho rejection
                    projected = default;
                    // Report ortho rejection failure
                    return false;
                // End ortho rejection block
                }
            // End orthographic normalization block
            }

            // Map normalized X to texel column
            double x = (normalizedX * 0.5 + 0.5) * (width - 1);
            // Map normalized Y to texel row
            double y = (0.5 - normalizedY * 0.5) * (height - 1);
            // Reject non-finite texel coordinates
            if (!double.IsFinite(x) || !double.IsFinite(y))
            // Begin invalid texel rejection block
            {
                // Clear output for invalid texel
                projected = default;
                // Report invalid texel failure
                return false;
            // End invalid texel rejection block
            }

            // Store projected shadow vertex
            projected = new RasterVertex(x, y, z);
            // Report successful shadow projection
            return true;
        // End shadow projection
        }

        /// <summary>Builds an orthonormal basis from one forward vector.</summary>
        // Declare orthonormal basis builder
        private static void CreateBasis(Vec3 forward, out Vec3 f, out Vec3 r, out Vec3 u)
        // Begin basis builder
        {
            // Normalize forward axis
            f = forward.Normalize();
            // Detect degenerate forward vector
            if (f.Length() < 1e-8)
                // Default forward to downward direction
                f = new Vec3(0, -1, 0);
            // Pick helper axis avoiding parallelism
            Vec3 helper = Math.Abs(f.Y) < 0.92 ? new Vec3(0, 1, 0) : new Vec3(1, 0, 0);
            // Compute normalized right axis
            r = helper.Cross(f).Normalize();
            // Detect degenerate right axis
            if (r.Length() < 1e-8)
                // Default right axis to X
                r = new Vec3(1, 0, 0);
            // Compute normalized up axis
            u = f.Cross(r).Normalize();
            // Detect degenerate up axis
            if (u.Length() < 1e-8)
                // Default up axis to Y
                u = new Vec3(0, 1, 0);
        // End basis builder
        }
    // End ShadowProjection struct
    }
// End renderer class
}
