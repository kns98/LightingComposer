
#version 450

// Declare vertex position input
layout(location = 0) in vec3 Position;
// Declare vertex normal input
layout(location = 1) in vec3 Normal;
// Declare vertex UV input
layout(location = 2) in vec2 Uv;
// Declare material index input
layout(location = 3) in float MaterialIndex;

// Output world position to fragment
layout(location = 0) out vec3 fsWorldPos;
// Output world normal to fragment
layout(location = 1) out vec3 fsNormal;
// Output UV to fragment stage
layout(location = 2) out vec2 fsUv;
// Output flat material index
layout(location = 3) flat out int fsMaterialIndex;

// Declare camera uniform block
layout(std140, set = 0, binding = 0) uniform CameraConstants
// Begin camera uniform block
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
// End camera uniform block
} Camera;

// Declare transform uniform block
layout(std140, set = 0, binding = 1) uniform TransformConstants
// Begin transform uniform block
{
    // Store pivot and enable flag
    vec4 PivotEnabled;
    // Store translation offset
    vec4 Translation;
    // Store Euler rotation angles
    vec4 Rotation;
    // Store scale factors
    vec4 Scale;
// End transform uniform block
} Transform;

// Declare scale sanitizer function
float sanitizeScale(float value)
// Begin scale sanitizer block
{
    // Replace near zero scale
    return abs(value) < 0.00000001 ? 1.0 : value;
// End scale sanitizer block
}

// Declare Euler rotation helper
vec3 rotateEuler(vec3 value, vec3 rotation)
// Begin Euler rotation block
{
    // Compute X axis rotation factors
    float cx = cos(rotation.x); float sx = sin(rotation.x);
    // Apply rotation about X
    value = vec3(value.x, value.y * cx - value.z * sx, value.y * sx + value.z * cx);
    // Compute Y axis rotation factors
    float cy = cos(rotation.y); float sy = sin(rotation.y);
    // Apply rotation about Y
    value = vec3(value.x * cy + value.z * sy, value.y, -value.x * sy + value.z * cy);
    // Compute Z axis rotation factors
    float cz = cos(rotation.z); float sz = sin(rotation.z);
    // Apply rotation about Z
    return vec3(value.x * cz - value.y * sz, value.x * sz + value.y * cz, value.z);
// End Euler rotation block
}

// Declare vertex shader entry
void main()
// Begin vertex main block
{
    // Initialize world position from input
    vec3 worldPosition = Position;
    // Initialize world normal from input
    vec3 worldNormal = Normal;
    // Apply pivot transform when enabled
    if (Transform.PivotEnabled.w > 0.5)
    // Begin pivot transform block
    {
        // Start sanitized scale vector
        vec3 safeScale = vec3(
            // Sanitize X scale component
            sanitizeScale(Transform.Scale.x),
            // Sanitize Y scale component
            sanitizeScale(Transform.Scale.y),
            // Sanitize Z scale component
            sanitizeScale(Transform.Scale.z));
        // Scale position around pivot point
        vec3 local = (Position - Transform.PivotEnabled.xyz) * safeScale;
        // Rotate and translate pivot position
        worldPosition = Transform.PivotEnabled.xyz + Transform.Translation.xyz + rotateEuler(local, Transform.Rotation.xyz);
        // Rotate normal with inverse scale
        worldNormal = normalize(rotateEuler(Normal / safeScale, Transform.Rotation.xyz));
    // End pivot transform block
    }

    // Compute position relative to camera
    vec3 rel = worldPosition - Camera.CameraPosition.xyz;
    // Project onto camera right axis
    float vx = dot(rel, Camera.CameraRight.xyz);
    // Project onto camera up axis
    float vy = dot(rel, Camera.CameraUp.xyz);
    // Project onto camera forward axis
    float vz = dot(rel, Camera.CameraForward.xyz);

    // Clamp aspect ratio minimum
    float aspect = max(Camera.Projection.x, 0.0001);
    // Clamp field of view factor
    float tanHalfFov = max(Camera.Projection.y, 0.0001);
    // Clamp near plane minimum
    float nearPlane = max(Camera.Projection.z, 0.0001);
    // Ensure far plane beyond near
    float farPlane = max(Camera.Projection.w, nearPlane + 1.0);
    // Use forward depth as w
    float z = vz;
    // Standard perspective depth scale mapping near to 0 and far to 1
    float depthScale = farPlane / (farPlane - nearPlane);
    // Standard perspective depth offset for Vulkan zero to one range
    float depthOffset = -depthScale * nearPlane;

    // Start clip position constructor
    gl_Position = vec4(
        // Compute horizontal clip coordinate
        -vx / (aspect * tanHalfFov),
         // Compute vertical clip coordinate
         vy / tanHalfFov,
         // Scale depth by view distance
         depthScale * z + depthOffset,
         // Store view depth as w
         z);
    // Flip Y for Vulkan coordinates
    gl_Position.y = -gl_Position.y;

    // Pass world position onward
    fsWorldPos = worldPosition;
    // Pass world normal onward
    fsNormal = worldNormal;
    // Pass texture coordinates onward
    fsUv = Uv;
    // Pass rounded material index onward
    fsMaterialIndex = int(MaterialIndex + 0.5);
// End vertex main block
}
// End embedded vertex shader string
