
#version 450

// Declare vertex position input
layout(location = 0) in vec3 Position;

// Declare shadow camera uniform block
layout(std140, set = 0, binding = 0) uniform ShadowCameraConstants
// Begin shadow camera block
{
    // Store shadow camera position
    vec4 CamPos;
    // Store shadow camera forward
    vec4 CamForward;
    // Store shadow camera right
    vec4 CamRight;
    // Store shadow camera up
    vec4 CamUp;
    // Store projection parameters
    vec4 Projection;
// End shadow camera block
} Camera;

// Declare shadow vertex entry
void main()
// Begin shadow vertex block
{
    // Compute position relative to light
    vec3 rel = Position - Camera.CamPos.xyz;
    // Project onto shadow right axis
    float vx = dot(rel, Camera.CamRight.xyz);
    // Project onto shadow up axis
    float vy = dot(rel, Camera.CamUp.xyz);
    // Project onto shadow forward axis
    float vz = dot(rel, Camera.CamForward.xyz);

    // Clamp aspect ratio minimum
    float aspect = max(Camera.Projection.x, 0.0001);
    // Read field of view factor
    float tanHalfFov = Camera.Projection.y;
    // Clamp near plane minimum
    float nearPlane = max(Camera.Projection.z, 0.0);
    // Ensure far plane beyond near
    float farPlane = max(Camera.Projection.w, nearPlane + 1.0);
    // Use forward depth as z
    float z = vz;
    // Normalize depth between planes
    float ndcDepth = (z - nearPlane) / (farPlane - nearPlane);

    // Test orthographic projection mode
    if (tanHalfFov < 0.0)
    // Begin orthographic block
    {
        // Decode orthographic half height
        float halfH = max(-tanHalfFov, 0.0001);
        // Decode orthographic half width
        float halfW = max(aspect * halfH, 0.0001);
        // Start orthographic clip constructor
        gl_Position = vec4(
            // Compute horizontal clip coordinate
            -vx / halfW,
            // Compute vertical clip coordinate
            vy / halfH,
            // Store linear depth value
            ndcDepth,
            // Store unit w divisor
            1.0);
    // End orthographic block
    }
    else
    // Begin perspective block
    {
        // Clamp field of view factor
        float t = max(tanHalfFov, 0.0001);
        // Start perspective clip constructor
        gl_Position = vec4(
            // Compute horizontal clip coordinate
            -vx / (aspect * t),
            // Compute vertical clip coordinate
            vy / t,
            // Scale depth by view distance
            ndcDepth * z,
            // Store view depth as w
            z);
    // End perspective block
    }
    // Flip Y for Vulkan coordinates
    gl_Position.y = -gl_Position.y;
// End shadow vertex block
}
