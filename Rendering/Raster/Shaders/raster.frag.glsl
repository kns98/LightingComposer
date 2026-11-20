
#version 450

// Define PI constant for BRDF
const float PI = 3.14159265358979323846;

// Declare raster light structure
struct RasterLight
// Begin light structure block
{
    // Store light position and kind
    vec4 PositionKind;
    // Store light direction and range
    vec4 DirectionRange;
    // Store light color and intensity
    vec4 ColorIntensity;
    // Store cone and enable flags
    vec4 ConeEnabled;
// End light structure block
};

// Declare raster material structure
struct RasterMaterial
// Begin material structure block
{
    // Store base color and alpha
    vec4 BaseColorAlpha;
    // Store emissive factor
    vec4 EmissiveFactor;
    // Store metallic roughness factors
    vec4 PbrFactors;
    // Store alpha test flags
    vec4 AlphaFlags;
    // Store clearcoat and optical factors
    vec4 OpticalFactors;
    // Store attenuation color and distance
    vec4 AttenuationColorDistance;
    // Store primary texture presence flags
    vec4 TextureFlags;
    // Store extra texture presence flags
    vec4 TextureFlags2;

    // Store base atlas transform
    vec4 BaseAtlasTransform;
    // Store base texture addressing
    vec4 BaseTextureAddress;
    // Store base UV transform
    vec4 BaseTextureTransform;

    // Store metallic roughness atlas transform
    vec4 MrAtlasTransform;
    // Store metallic roughness addressing
    vec4 MrTextureAddress;
    // Store metallic roughness UV transform
    vec4 MrTextureTransform;

    // Store normal atlas transform
    vec4 NormalAtlasTransform;
    // Store normal texture addressing
    vec4 NormalTextureAddress;
    // Store normal UV transform
    vec4 NormalTextureTransform;

    // Store emissive atlas transform
    vec4 EmissiveAtlasTransform;
    // Store emissive texture addressing
    vec4 EmissiveTextureAddress;
    // Store emissive UV transform
    vec4 EmissiveTextureTransform;

    // Store occlusion atlas transform
    vec4 OcclusionAtlasTransform;
    // Store occlusion texture addressing
    vec4 OcclusionTextureAddress;
    // Store occlusion UV transform
    vec4 OcclusionTextureTransform;

    // Store transmission atlas transform
    vec4 TransmissionAtlasTransform;
    // Store transmission texture addressing
    vec4 TransmissionTextureAddress;
    // Store transmission UV transform
    vec4 TransmissionTextureTransform;
// End material structure block
};

// Receive interpolated world position
layout(location = 0) in vec3 fsWorldPos;
// Receive interpolated world normal
layout(location = 1) in vec3 fsNormal;
// Receive interpolated texture coordinates
layout(location = 2) in vec2 fsUv;
// Receive flat material index
layout(location = 3) flat in int fsMaterialIndex;

// Declare fragment color output
layout(location = 0) out vec4 outColor;

// Declare fragment camera block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin fragment camera block
{
    // Store camera world position
    vec4 CameraPosition;
    // Store camera forward vector
    vec4 CameraForward;
    // Store camera right vector
    vec4 CameraRight;
    // Store camera up vector
    vec4 CameraUp;
    // Store projection parameters
    vec4 Projection;
    // Store light and material counts
    vec4 Counts;
// End fragment camera block
} Camera;

// Declare light storage buffer
layout(std430, set = 0, binding = 2) readonly buffer LightBuffer
// Begin light buffer block
{
    // Store variable light array
    RasterLight Lights[];
// End light buffer block
};

// Declare material storage buffer
layout(std430, set = 0, binding = 3) readonly buffer MaterialBuffer
// Begin material buffer block
{
    // Store variable material array
    RasterMaterial Materials[];
// End material buffer block
};

// Declare atlas texture array
layout(set = 0, binding = 4) uniform texture2DArray AtlasTexture;
// Declare atlas texture sampler
layout(set = 0, binding = 5) uniform sampler AtlasSampler;

// Declare texture addressing helper
float addressTexture(float v, float mode)
// Begin addressing helper block
{
    // Detect clamp addressing mode
    if (mode > 0.5 && mode < 1.5)
        // Clamp coordinate into range
        return clamp(v, 0.0, 1.0);
    // Detect mirrored repeat mode
    if (mode > 1.5)
    // Begin mirror addressing block
    {
        // Find integer repeat count
        float floorValue = floor(v);
        // Find fractional local coordinate
        float local = fract(v);
        // Mirror alternate repeats
        return mod(floorValue, 2.0) == 0.0 ? local : 1.0 - local;
    // End mirror addressing block
    }
    // Pass through in range coordinates
    if (v >= 0.0 && v <= 1.0)
        // Return already valid coordinate
        return v;
    // Wrap coordinate with fraction
    return fract(v);
// End addressing helper block
}

// Declare UV transform helper
vec2 addressUv(vec2 uv, vec4 textureAddress, vec4 textureTransform)
// Begin UV transform block
{
    // Scale UV by transform factor
    vec2 transformed = uv * textureTransform.zw;
    // Read texture rotation angle
    float rotation = textureAddress.z;
    // Skip rotation when nearly zero
    if (abs(rotation) > 0.000001)
    // Begin UV rotation block
    {
        // Compute rotation cosine factor
        float c = cos(rotation);
        // Compute rotation sine factor
        float s = sin(rotation);
        // Rotate scaled UV coordinates
        transformed = vec2(transformed.x * c - transformed.y * s, transformed.x * s + transformed.y * c);
    // End UV rotation block
    }
    // Add UV offset translation
    transformed += textureTransform.xy;
    // Address both transformed axes
    return vec2(addressTexture(transformed.x, textureAddress.x), addressTexture(transformed.y, textureAddress.y));
// End UV transform block
}

// Declare atlas sampling helper
vec4 sampleAtlasTexture(vec2 uv, vec4 atlasTransform, vec4 textureAddress, vec4 textureTransform)
// Begin atlas sampling block
{
    // Resolve addressed texture coordinates
    vec2 addressed = addressUv(uv, textureAddress, textureTransform);
    // Map coordinates into atlas region
    vec2 atlasUv = atlasTransform.xy + addressed * atlasTransform.zw;
    // Sample atlas array layer
    return texture(sampler2DArray(AtlasTexture, AtlasSampler), vec3(atlasUv, textureAddress.w));
// End atlas sampling block
}

// Declare sRGB to linear helper
float srgbChannelToLinear(float value)
// Begin sRGB conversion block
{
    // Convert one channel to linear
    return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
// End sRGB conversion block
}

// Declare vector sRGB converter
vec3 srgbToLinear(vec3 value)
// Begin vector sRGB block
{
    // Start linear color constructor
    return vec3(
        // Convert red channel value
        srgbChannelToLinear(value.r),
        // Convert green channel value
        srgbChannelToLinear(value.g),
        // Convert blue channel value
        srgbChannelToLinear(value.b));
// End vector sRGB block
}

// Declare linear to sRGB helper
float linearChannelToSrgb(float value)
// Begin linear conversion block
{
    // Clamp negative linear values
    value = max(value, 0.0);
    // Convert one channel to sRGB
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
// End linear conversion block
}

// Declare vector linear converter
vec3 linearToSrgb(vec3 value)
// Begin vector linear block
{
    // Start sRGB color constructor
    return vec3(
        // Convert red channel value
        linearChannelToSrgb(value.r),
        // Convert green channel value
        linearChannelToSrgb(value.g),
        // Convert blue channel value
        linearChannelToSrgb(value.b));
// End vector linear block
}

// Declare neutral tone mapping helper
vec3 pbrNeutralToneMap(vec3 color)
// Begin tone mapping block
{
    // Set highlight compression start
    const float startCompression = 0.76;
    // Set highlight desaturation amount
    const float desaturation = 0.15;
    // Find minimum color channel
    float x = min(color.r, min(color.g, color.b));
    // Compute shadow toe offset
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    // Subtract toe offset value
    color -= offset;

    // Find peak color channel
    float peak = max(color.r, max(color.g, color.b));
    // Skip compression below threshold
    if (peak < startCompression)
        // Return dark colors directly
        return max(color, vec3(0.0));

    // Compute remaining headroom range
    float d = 1.0 - startCompression;
    // Compress peak toward white
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    // Scale color by compressed peak
    color *= newPeak / max(peak, 0.000001);
    // Compute desaturation blend factor
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    // Return desaturated safe color
    return max(mix(color, newPeak * vec3(1.0), g), vec3(0.0));
// End tone mapping block
}

// Declare GGX distribution helper
float distributionGgx(float nDotH, float alphaRoughness)
// Begin GGX distribution block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute denominator shape term
    float f = nDotH * nDotH * (a2 - 1.0) + 1.0;
    // Return normalized facet distribution
    return a2 / max(PI * f * f, 0.000001);
// End GGX distribution block
}

// Declare Smith visibility helper
float visibilitySmithGgxCorrelated(float nDotV, float nDotL, float alphaRoughness)
// Begin Smith visibility block
{
    // Square roughness to alpha
    float a2 = alphaRoughness * alphaRoughness;
    // Compute view masking term
    float gv = nDotL * sqrt(max(nDotV * nDotV * (1.0 - a2) + a2, 0.0));
    // Compute light shadowing term
    float gl = nDotV * sqrt(max(nDotL * nDotL * (1.0 - a2) + a2, 0.0));
    // Return correlated visibility factor
    return 0.5 / max(gv + gl, 0.000001);
// End Smith visibility block
}

// Declare Schlick fresnel helper
vec3 fresnelSchlick(float vDotH, vec3 f0)
// Begin fresnel helper block
{
    // Compute fifth power falloff
    float f = pow(1.0 - clamp(vDotH, 0.0, 1.0), 5.0);
    // Blend base reflectance upward
    return f0 + (1.0 - f0) * f;
// End fresnel helper block
}

// Declare cotangent frame helper
mat3 cotangentFrame(vec3 normal, vec3 position, vec2 uv)
// Begin cotangent frame block
{
    // Compute X position derivative
    vec3 dp1 = dFdx(position);
    // Compute Y position derivative
    vec3 dp2 = dFdy(position);
    // Compute X UV derivative
    vec2 duv1 = dFdx(uv);
    // Compute Y UV derivative
    vec2 duv2 = dFdy(uv);
    // Build first perpendicular vector
    vec3 dp2Perp = cross(dp2, normal);
    // Build second perpendicular vector
    vec3 dp1Perp = cross(normal, dp1);
    // Accumulate tangent direction
    vec3 tangent = dp2Perp * duv1.x + dp1Perp * duv2.x;
    // Accumulate bitangent direction
    vec3 bitangent = dp2Perp * duv1.y + dp1Perp * duv2.y;
    // Measure worst frame length
    float maxLength = max(dot(tangent, tangent), dot(bitangent, bitangent));
    // Handle degenerate UV mapping
    if (maxLength < 0.00000001)
    // Begin degenerate fallback block
    {
        // Pick axis away from normal
        vec3 axis = abs(normal.z) < 0.999 ? vec3(0.0, 0.0, 1.0) : vec3(0.0, 1.0, 0.0);
        // Build orthonormal tangent fallback
        tangent = normalize(cross(axis, normal));
        // Build orthonormal bitangent fallback
        bitangent = cross(normal, tangent);
        // Return fallback tangent frame
        return mat3(tangent, bitangent, normal);
    // End degenerate fallback block
    }
    // Compute inverse frame length
    float inverseLength = inversesqrt(maxLength);
    // Return normalized tangent frame
    return mat3(tangent * inverseLength, bitangent * inverseLength, normal);
// End cotangent frame block
}

// Declare studio environment helper
vec3 studioEnvironment(vec3 direction)
// Begin environment lighting block
{
    // Normalize sample direction vector
    direction = normalize(direction);
    // Blend ground to sky factor
    float skyAmount = smoothstep(-0.25, 0.65, direction.y);
    // Define ground bounce color
    vec3 ground = vec3(0.035, 0.040, 0.050);
    // Define sky ambient color
    vec3 sky = vec3(0.32, 0.39, 0.49);
    // Mix ground and sky radiance
    vec3 radiance = mix(ground, sky, skyAmount);

    // Define key light direction
    vec3 keyDirection = normalize(vec3(-0.55, 0.62, -0.56));
    // Define rim light direction
    vec3 rimDirection = normalize(vec3(0.78, 0.28, 0.56));
    // Compute key highlight lobe
    float key = pow(max(dot(direction, keyDirection), 0.0), 96.0);
    // Compute rim highlight lobe
    float rim = pow(max(dot(direction, rimDirection), 0.0), 180.0);
    // Add key light contribution
    radiance += vec3(5.2, 4.8, 4.2) * key;
    // Add blue rim highlight to environment radiance
    radiance += vec3(2.0, 2.7, 3.8) * rim;
    // Return studio environment radiance
    return radiance;
// End studioEnvironment function
}

// Declare diffuse environment lookup function
vec3 diffuseEnvironment(vec3 normal)
// Begin diffuseEnvironment function
{
    // Blend ground to sky by normal height
    float skyAmount = clamp(normal.y * 0.5 + 0.5, 0.0, 1.0);
    // Return ground sky gradient color
    return mix(vec3(0.055, 0.060, 0.070), vec3(0.42, 0.47, 0.54), skyAmount);
// End diffuseEnvironment function
}

// Declare fragment shader entry point
void main()
// Begin main function
{
    // Mark solid wireframe override block
#ifdef WIREFRAME_SOLID
    // Output flat wireframe line color
    outColor = vec4(0.92, 0.96, 1.0, 1.0);
    // Exit after wireframe output
    return;
#endif
    // Mark dashed wireframe override block
#ifdef WIREFRAME_DASHED
    // Discard dashed gap fragments
    if (mod(gl_FragCoord.x + gl_FragCoord.y * 2.0, 8.0) >= 4.0) discard;
    // Output flat hidden line color
    outColor = vec4(0.42, 0.52, 0.66, 1.0);
    // Exit after dashed output
    return;
#endif
    // Read debug visualization mode selector
    int debugMode = int(Camera.Counts.z + 0.5);
    // Fetch fragment material record
    RasterMaterial material = Materials[fsMaterialIndex];

    // Flag base color texture presence
    bool hasBase = material.TextureFlags.x > 0.5;
    // Flag metallic roughness texture presence
    bool hasMr = material.TextureFlags.y > 0.5;
    // Flag normal map presence
    bool hasNormal = material.TextureFlags.z > 0.5;
    // Flag emissive texture presence
    bool hasEmissive = material.TextureFlags.w > 0.5;
    // Flag occlusion texture presence
    bool hasOcclusion = material.TextureFlags2.x > 0.5;
    // Flag transmission texture presence
    bool hasTransmissionTexture = material.TextureFlags2.y > 0.5;

    // Sample base color texel when present
    vec4 baseTexel = hasBase
        // Fetch base texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.BaseAtlasTransform, material.BaseTextureAddress, material.BaseTextureTransform)
        // Default base texel to white
        : vec4(1.0);
    // Compute linear base color
    vec3 baseColor = material.BaseColorAlpha.rgb * (hasBase ? srgbToLinear(baseTexel.rgb) : vec3(1.0));
    // Combine material and texture alpha
    float sourceAlpha = material.BaseColorAlpha.a * baseTexel.a;
    // Initialize coverage alpha
    float alpha = sourceAlpha;
    // Default sampled transmission to opaque
    float sampledTransmission = 1.0;
    // Clamp material transmission factor
    float transmission = clamp(material.AlphaFlags.w, 0.0, 1.0);
    // Test for transmission texture
    if (hasTransmissionTexture)
    // Begin transmission sampling block
    {
        // Sample transmission mask red channel
        sampledTransmission = sampleAtlasTexture(
            // Pass fragment UVs to sampler
            fsUv,
            // Pass transmission atlas transform
            material.TransmissionAtlasTransform,
            // Pass transmission address mode
            material.TransmissionTextureAddress,
            // Read mask value from transform result
            material.TransmissionTextureTransform).r;
        // Modulate transmission by sampled mask
        transmission *= sampledTransmission;
    // End transmission sampling block
    }

    // Clamp index of refraction
    float ior = clamp(material.OpticalFactors.x, 1.0, 2.333);
    // Clamp volume thickness nonnegative
    float thickness = max(material.OpticalFactors.y, 0.0);
    // Clamp clearcoat layer weight
    float clearcoat = clamp(material.OpticalFactors.z, 0.0, 1.0);
    // Test for shared clearcoat mask
    if (material.TextureFlags2.z > 0.5)
        // Reuse transmission mask for clearcoat
        clearcoat *= sampledTransmission;
    // Clamp clearcoat roughness
    float clearcoatRoughness = clamp(material.OpticalFactors.w, 0.045, 1.0);
    // Decode alpha blend mode
    int alphaMode = int(material.AlphaFlags.x + 0.5);
    // Test for opaque alpha mode
    if (alphaMode == 0)
        // Force full opacity
        alpha = 1.0;
    // Test mask cutoff threshold
    else if (alphaMode == 1 && alpha < material.AlphaFlags.y)
        // Discard fragment below cutoff
        discard;

    // The shared rasterizer disables fixed-function culling so double-sided
    // materials remain possible. Respect glTF single-sided semantics here for
    // transparent surfaces; otherwise every back face is alpha blended too and
    // thin stained-glass meshes quickly accumulate toward white.
    // Flag blended or transmissive surface
    bool transparentSurface = alphaMode == 2 || transmission > 0.001;
    // Test single sided back face
    if (transparentSurface && material.AlphaFlags.z < 0.5 && !gl_FrontFacing)
        // Cull single sided back face
        discard;

    // Sample metallic roughness texel when present
    vec4 mrTexel = hasMr
        // Fetch MR texel from texture atlas
        ? sampleAtlasTexture(fsUv, material.MrAtlasTransform, material.MrTextureAddress, material.MrTextureTransform)
        // Default MR texel to white
        : vec4(1.0);
    // Compute roughness from green channel
    float roughness = clamp(material.PbrFactors.y * (hasMr ? mrTexel.g : 1.0), 0.045, 1.0);
    // Compute metallic from blue channel
    float metallic = clamp(material.PbrFactors.x * (hasMr ? mrTexel.b : 1.0), 0.0, 1.0);

    // Default occlusion to unshadowed
    float occlusion = 1.0;
    // Test for occlusion texture
    if (hasOcclusion)
    // Begin occlusion sampling block
    {
        // Sample occlusion red channel
        float sampledOcclusion = sampleAtlasTexture(
            // Fetch occlusion texel from atlas
            fsUv, material.OcclusionAtlasTransform, material.OcclusionTextureAddress, material.OcclusionTextureTransform).r;
        // Blend occlusion by material strength
        occlusion = mix(1.0, sampledOcclusion, clamp(material.PbrFactors.w, 0.0, 1.0));
    // End occlusion sampling block
    }

    // Compute view direction to camera
    vec3 viewDir = normalize(Camera.CameraPosition.xyz - fsWorldPos);
    // Normalize interpolated surface normal
    vec3 normal = normalize(fsNormal);
    // Test double sided material flag
    if (material.AlphaFlags.z > 0.5)
    // Begin double sided normal block
    {
        // Test back facing normal
        if (dot(normal, viewDir) < 0.0)
            // Flip normal toward viewer
            normal = -normal;
    // End double sided normal block
    }
    // Test single sided back face
    else if (dot(normal, viewDir) < 0.0)
    // Begin normal flip block
    {
        // Flip normal toward viewer
        normal = -normal;
    // End normal flip block
    }

    // Test for normal map
    if (hasNormal)
    // Begin normal mapping block
    {
        // Compute addressed normal map UVs
        vec2 normalUv = addressUv(fsUv, material.NormalTextureAddress, material.NormalTextureTransform);
        // Sample tangent space normal
        vec3 tangentNormal = sampleAtlasTexture(
            // Decode normal from unsigned range
            fsUv, material.NormalAtlasTransform, material.NormalTextureAddress, material.NormalTextureTransform).xyz * 2.0 - 1.0;
        // Scale normal map strength
        tangentNormal.xy *= material.PbrFactors.z;
        // Normalize perturbed tangent normal
        tangentNormal = normalize(tangentNormal);
        // Transform normal to world space
        normal = normalize(cotangentFrame(normal, fsWorldPos, normalUv) * tangentNormal);
    // End normal mapping block
    }

    // Default emissive to black
    vec3 emissive = vec3(0.0);
    // Test for emissive contribution
    if (material.EmissiveFactor.a > 0.0)
    // Begin emissive block
    {
        // Select emissive source color
        vec3 emissionSource = hasEmissive
            // Sample emissive texel from atlas
            ? srgbToLinear(sampleAtlasTexture(
                // Convert emissive texel to linear
                fsUv, material.EmissiveAtlasTransform, material.EmissiveTextureAddress, material.EmissiveTextureTransform).rgb)
            // Fall back to base color
            : baseColor;
        // Scale emissive by factor and strength
        emissive = emissionSource * material.EmissiveFactor.rgb * material.EmissiveFactor.a;
    // End emissive block
    }

    // Test UV debug view
    if (debugMode == 1)
    // Begin UV debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Output UV gradient debug color
        outColor = vec4(uv.x, uv.y, 0.0, 1.0);
        // Exit after UV debug output
        return;
    // End UV debug block
    }
    // Test atlas debug view
    if (debugMode == 2)
    // Begin atlas debug block
    {
        // Compute addressed base UVs
        vec2 uv = addressUv(fsUv, material.BaseTextureAddress, material.BaseTextureTransform);
        // Map UVs into atlas region
        vec2 atlasUv = material.BaseAtlasTransform.xy + uv * material.BaseAtlasTransform.zw;
        // Output atlas grid debug color
        outColor = vec4(fract(atlasUv.x * 8.0), fract(atlasUv.y * 8.0), hasBase ? 1.0 : 0.0, 1.0);
        // Exit after atlas debug output
        return;
    // End atlas debug block
    }
    // Test base texture debug view
    if (debugMode == 3)
    // Begin base texture debug block
    {
        // Output base texel debug color
        outColor = vec4(hasBase ? baseTexel.rgb : linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after base debug output
        return;
    // End base texture debug block
    }
    // Test base color debug view
    if (debugMode == 4)
    // Begin base color debug block
    {
        // Output base color debug swatch
        outColor = vec4(linearToSrgb(material.BaseColorAlpha.rgb), 1.0);
        // Exit after color debug output
        return;
    // End base color debug block
    }
    // Test normal debug view
    if (debugMode == 5)
    // Begin normal debug block
    {
        // Output normal vector debug color
        outColor = vec4(normal * 0.5 + 0.5, 1.0);
        // Exit after normal debug output
        return;
    // End normal debug block
    }
    // Test metallic debug view
    if (debugMode == 6)
    // Begin metallic debug block
    {
        // Output metallic debug grayscale
        outColor = vec4(vec3(metallic), 1.0);
        // Exit after metallic debug output
        return;
    // End metallic debug block
    }
    // Test roughness debug view
    if (debugMode == 7)
    // Begin roughness debug block
    {
        // Output roughness debug grayscale
        outColor = vec4(vec3(roughness), 1.0);
        // Exit after roughness debug output
        return;
    // End roughness debug block
    }
    // Test occlusion debug view
    if (debugMode == 8)
    // Begin occlusion debug block
    {
        // Output occlusion debug grayscale
        outColor = vec4(vec3(occlusion), 1.0);
        // Exit after occlusion debug output
        return;
    // End occlusion debug block
    }
    // Test emissive debug view
    if (debugMode == 9)
    // Begin emissive debug block
    {
        // Output tone mapped emissive debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(emissive)), 1.0);
        // Exit after emissive debug output
        return;
    // End emissive debug block
    }

    // Clamp normal view cosine
    float nDotV = max(dot(normal, viewDir), 0.0001);
    // Compute dielectric Fresnel reflectance
    float dielectricF0 = pow((ior - 1.0) / max(ior + 1.0, 0.0001), 2.0);
    // Blend Fresnel base by metallic
    vec3 f0 = mix(vec3(dielectricF0), baseColor, metallic);
    // Initialize direct light accumulator
    vec3 directLighting = vec3(0.0);
    // Initialize clearcoat light accumulator
    vec3 clearcoatDirect = vec3(0.0);
    // Clamp active light count
    int lightCount = min(32, int(Camera.Counts.x + 0.5));
    // Loop over active lights
    for (int i = 0; i < lightCount; i++)
    // Begin per light loop
    {
        // Fetch current light record
        RasterLight light = Lights[i];
        // Test disabled light flag
        if (light.ConeEnabled.w < 0.5)
            // Skip disabled light
            continue;

        // Read light type selector
        float kind = light.PositionKind.w;
        // Declare light direction variable
        vec3 lightDirection;
        // Default attenuation to none
        float attenuation = 1.0;
        // Default spotlight cone to full
        float cone = 1.0;

        // Test directional light type
        if (kind > 0.5 && kind < 1.5)
        // Begin directional light block
        {
            // Aim along directional light vector
            lightDirection = normalize(-light.DirectionRange.xyz);
        // End directional light block
        }
        // Handle positional light types
        else
        // Begin positional light block
        {
            // Compute vector to light
            vec3 toLight = light.PositionKind.xyz - fsWorldPos;
            // Measure distance to light
            float distanceToLight = length(toLight);
            // Test degenerate light distance
            if (distanceToLight < 0.0001)
                // Skip coincident light
                continue;

            // Normalize direction to light
            lightDirection = toLight / distanceToLight;
            // Read light range cutoff
            float range = light.DirectionRange.w;
            // Apply inverse square falloff
            attenuation = 1.0 / (1.0 + 0.11 * distanceToLight * distanceToLight);
            // Test finite range cutoff
            if (range > 0.0001)
            // Begin range fade block
            {
                // Normalize distance by range
                float normalizedDistance = clamp(distanceToLight / range, 0.0, 1.0);
                // Compute range edge fade
                float edgeFade = 1.0 - normalizedDistance * normalizedDistance;
                // Fade attenuation at range edge
                attenuation *= edgeFade * edgeFade;
            // End range fade block
            }

            // Test spotlight type
            if (kind >= 1.5)
            // Begin spotlight cone block
            {
                // Normalize spotlight forward vector
                vec3 spotForward = normalize(light.DirectionRange.xyz);
                // Measure spot alignment cosine
                float coneDot = dot(normalize(fsWorldPos - light.PositionKind.xyz), spotForward);
                // Read inner cone angle
                float inner = light.ConeEnabled.x;
                // Read outer cone angle
                float outer = light.ConeEnabled.y;
                // Interpolate cone falloff factor
                cone = clamp((coneDot - outer) / max(0.0001, inner - outer), 0.0, 1.0);
                // Soften cone falloff curve
                cone *= cone;
            // End spotlight cone block
            }
        // End positional light block
        }

        // Clamp normal light cosine
        float nDotL = max(dot(normal, lightDirection), 0.0);
        // Test back facing light
        if (nDotL <= 0.0)
            // Skip unlit contribution
            continue;

        // Compute Blinn Phong half vector
        vec3 halfVector = normalize(lightDirection + viewDir);
        // Clamp normal half cosine
        float nDotH = max(dot(normal, halfVector), 0.0);
        // Clamp view half cosine
        float vDotH = max(dot(viewDir, halfVector), 0.0);
        // Square perceptual roughness value
        float alphaRoughness = roughness * roughness;
        // Evaluate GGX normal distribution
        float distribution = distributionGgx(nDotH, alphaRoughness);
        // Evaluate Smith geometry term
        float visibility = visibilitySmithGgxCorrelated(nDotV, nDotL, alphaRoughness);
        // Evaluate Schlick Fresnel term
        vec3 fresnel = fresnelSchlick(vDotH, f0);
        // Combine specular BRDF terms
        vec3 specular = distribution * visibility * fresnel;
        // Compute energy conserving diffuse
        vec3 diffuse = (vec3(1.0) - fresnel) * (1.0 - metallic) * baseColor / PI;
        // Scale light color by falloff
        vec3 radiance = light.ColorIntensity.rgb * light.ColorIntensity.w * attenuation * cone * 0.18;
        // Accumulate diffuse plus specular lighting
        directLighting += (diffuse + specular) * radiance * nDotL;

        // KHR_materials_clearcoat adds a dielectric layer. Reuse the already
        // sampled transmission mask when the asset shares that texture, so the
        // StainedGlassLamp path adds ALU only and no extra texture fetch.
        // Test clearcoat layer presence
        if (clearcoat > 0.001)
        // Begin clearcoat direct block
        {
            // Square clearcoat roughness value
            float clearcoatAlpha = clearcoatRoughness * clearcoatRoughness;
            // Evaluate clearcoat GGX distribution
            float clearcoatDistribution = distributionGgx(nDotH, clearcoatAlpha);
            // Evaluate clearcoat geometry term
            float clearcoatVisibility = visibilitySmithGgxCorrelated(nDotV, nDotL, clearcoatAlpha);
            // Evaluate clearcoat Fresnel term
            vec3 clearcoatFresnel = fresnelSchlick(vDotH, vec3(0.04));
            // Accumulate clearcoat specular direct light
            clearcoatDirect += clearcoat * clearcoatDistribution * clearcoatVisibility *
                // Scale clearcoat by radiance and cosine
                clearcoatFresnel * radiance * nDotL;
        // End clearcoat direct block
        }
    // End per light loop
    }

    // Compute mirror reflection vector
    vec3 reflection = reflect(-viewDir, normal);
    // Approximate image based Fresnel
    vec3 fresnelIbl = f0 + (max(vec3(1.0 - roughness), f0) - f0) * pow(1.0 - nDotV, 5.0);
    // Compute diffuse image based lighting
    vec3 diffuseIbl = diffuseEnvironment(normal) * baseColor * (vec3(1.0) - fresnelIbl) * (1.0 - metallic);
    // Sample sharp environment reflection
    vec3 sharpSpecularIbl = studioEnvironment(reflection);
    // Sample broad environment reflection
    vec3 broadSpecularIbl = diffuseEnvironment(normal);
    // Blend reflections by roughness
    vec3 specularIbl = mix(sharpSpecularIbl, broadSpecularIbl, roughness * roughness) * fresnelIbl;
    // Combine indirect light with occlusion
    vec3 indirectLighting = (diffuseIbl + specularIbl) * occlusion * 0.72;

    // Test direct light debug view
    if (debugMode == 10)
    // Begin direct debug block
    {
        // Output tone mapped direct debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(directLighting)), 1.0);
        // Exit after direct debug output
        return;
    // End direct debug block
    }
    // Test indirect light debug view
    if (debugMode == 11)
    // Begin indirect debug block
    {
        // Output tone mapped indirect debug
        outColor = vec4(linearToSrgb(pbrNeutralToneMap(indirectLighting)), 1.0);
        // Exit after indirect debug output
        return;
    // End indirect debug block
    }

    // Sum direct and indirect lighting
    vec3 surfaceLighting = directLighting + indirectLighting;

    // Keep the fast single-pass raster path: transmission changes the shaded
    // light, never surface coverage. This follows glTF's separation of optical
    // transmission from alpha while avoiding a framebuffer resolve, mip chain,
    // additional render pass, or any extra texture sample for StainedGlassLamp.
    // Test transmissive material
    if (transmission > 0.001)
    // Begin transmission block
    {
        // Compute Snell refraction vector
        vec3 refractedDirection = refract(-viewDir, normal, 1.0 / ior);
        // Test total internal reflection
        if (dot(refractedDirection, refractedDirection) < 0.000001)
            // Fall back to mirror reflection
            refractedDirection = reflection;

        // Square roughness for blur
        float transmissionBlur = roughness * roughness;
        // Blend transmitted environment samples
        vec3 transmittedEnvironment = mix(
            // Sample sharp refracted environment
            studioEnvironment(refractedDirection),
            // Sample boosted backside diffuse
            diffuseEnvironment(-normal) * 1.35,
            // Blur by roughness factor
            transmissionBlur);

        // Tint glass by base color
        vec3 glassTint = mix(vec3(1.0), max(baseColor, vec3(0.001)), 0.72);
        // Default volume attenuation to none
        vec3 volumeAttenuation = vec3(1.0);
        // Read attenuation distance parameter
        float attenuationDistance = material.AttenuationColorDistance.w;
        // Test volumetric absorption enabled
        if (thickness > 0.0 && attenuationDistance > 0.0)
        // Begin absorption block
        {
            // Compute view dependent path length
            float pathLength = thickness / max(abs(dot(normal, viewDir)), 0.15);
            // Apply Beer Lambert absorption
            volumeAttenuation = pow(
                // Clamp attenuation color positive
                max(material.AttenuationColorDistance.rgb, vec3(0.0001)),
                // Scale absorption by normalized path
                vec3(pathLength / attenuationDistance));
        // End absorption block
        }

        // Tint transmitted environment light
        vec3 transmittedLight = transmittedEnvironment * glassTint * volumeAttenuation;
        // Evaluate transmission Fresnel term
        vec3 transmissionFresnel = fresnelSchlick(nDotV, vec3(dielectricF0));
        // Blend transmitted and surface light
        vec3 opticalResult = mix(transmittedLight, surfaceLighting, transmissionFresnel);
        // Mix surface toward optical result
        surfaceLighting = mix(surfaceLighting, opticalResult, transmission);
    // End transmission block
    }

    // Add emissive to surface lighting
    vec3 linearColor = surfaceLighting + emissive;
    // Test clearcoat layer presence
    if (clearcoat > 0.001)
    // Begin clearcoat indirect block
    {
        // Compute view Fresnel for clearcoat
        float clearcoatFresnelView = 0.04 + 0.96 * pow(1.0 - nDotV, 5.0);
        // Blend clearcoat environment reflections
        vec3 clearcoatIbl = mix(
            // Use sharp reflection sample
            sharpSpecularIbl,
            // Use broad reflection sample
            broadSpecularIbl,
            // Weight clearcoat IBL by Fresnel
            clearcoatRoughness * clearcoatRoughness) * clearcoatFresnelView * clearcoat;
        // Attenuate base by clearcoat layer
        linearColor *= 1.0 - clearcoat * clearcoatFresnelView;
        // Add clearcoat direct and indirect
        linearColor += clearcoatDirect + clearcoatIbl;
    // End clearcoat indirect block
    }

    // Alpha remains geometric coverage. OPAQUE and MASK transmission materials
    // therefore stay visibly present instead of becoming ordinary transparency.
    // Select output alpha by mode
    float outputAlpha = alphaMode == 2 ? alpha : 1.0;

    // Tone map and encode output
    vec3 outputColor = linearToSrgb(pbrNeutralToneMap(linearColor));
    // Write final fragment color
    outColor = vec4(outputColor, outputAlpha);
// End main function
}
// End fragment shader source string
