
#version 450

// Declare shadow depth output
layout(location = 0) out vec4 outColor;

// Declare shadow fragment entry
void main()
// Begin shadow fragment block
{
    // Write fragment depth value
    outColor = vec4(gl_FragCoord.z, 0.0, 0.0, 1.0);
// End shadow fragment block
}
