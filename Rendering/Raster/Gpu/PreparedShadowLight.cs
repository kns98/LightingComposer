// File: Rendering/Raster/Gpu/PreparedShadowLight.cs
// Purpose: Shadow-casting scene light with its shadow map layer.

using LightingShowcase.Lighting;

namespace LightingShowcase.Rendering;

/// <summary>Shadow-casting scene light with its assigned shadow map layer.</summary>
internal sealed record PreparedShadowLight(SceneLight Light, int Layer);
