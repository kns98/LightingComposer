// File: Rendering/Shadow/PreparedLight.cs
// Purpose: Enabled light with shadow map and precomputed cone data.

using LightingShowcase.Lighting;
using LightingShowcase.Math3D;

namespace LightingShowcase.Rendering;

internal readonly record struct PreparedLight(
    SceneLight Light,
    ShadowMap? ShadowMap,
    Vec3 DirectionalLightDirection,
    Vec3 SpotForward,
    double CosOuter,
    double CosInner);
