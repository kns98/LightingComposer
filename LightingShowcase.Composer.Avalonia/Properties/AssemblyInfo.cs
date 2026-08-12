/*
 * Assembly-level attributes live here because they describe the compiled application rather than any one type.
 * They affect framework/runtime behavior and metadata for the whole Composer assembly before a window or scene is
 * created.
 */
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LightingShowcase.Composer.Tests")]
