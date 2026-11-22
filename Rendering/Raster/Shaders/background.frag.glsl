#version 450

layout(std140, set = 0, binding = 0) uniform BackgroundConstants
{
    vec4 Top;
    vec4 Bottom;
} Background;

layout(location = 0) in vec2 vUv;
layout(location = 0) out vec4 outColor;

float linearChannelToSrgb(float value)
{
    value = max(value, 0.0);
    return value <= 0.0031308 ? value * 12.92 : 1.055 * pow(value, 1.0 / 2.4) - 0.055;
}

vec3 linearToSrgb(vec3 value)
{
    return vec3(
        linearChannelToSrgb(value.r),
        linearChannelToSrgb(value.g),
        linearChannelToSrgb(value.b));
}

// Exact copy of the main shader's output transform so background pixels match
// shaded pixels for the same linear color.
vec3 pbrNeutralToneMap(vec3 color)
{
    const float startCompression = 0.76;
    const float desaturation = 0.15;
    float x = min(color.r, min(color.g, color.b));
    float offset = x < 0.08 ? x - 6.25 * x * x : 0.04;
    color -= offset;

    float peak = max(color.r, max(color.g, color.b));
    if (peak < startCompression)
        return max(color, vec3(0.0));

    float d = 1.0 - startCompression;
    float newPeak = 1.0 - d * d / (peak + d - startCompression);
    color *= newPeak / max(peak, 0.000001);
    float g = 1.0 - 1.0 / (desaturation * (peak - newPeak) + 1.0);
    return max(mix(color, newPeak * vec3(1.0), g), vec3(0.0));
}

void main()
{
    // NDC y=-1 lands on the image top with this backend's clip convention,
    // so uv.y 0 is the top color and uv.y 1 (clamped) is the bottom color.
    vec3 linear = mix(Background.Top.rgb, Background.Bottom.rgb, clamp(vUv.y, 0.0, 1.0));
    outColor = vec4(linearToSrgb(pbrNeutralToneMap(linear)), 1.0);
}
