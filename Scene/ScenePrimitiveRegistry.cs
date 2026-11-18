// -----------------------------------------------------------------------------
// File: Scene/ScenePrimitiveRegistry.cs
// Purpose: Discovers self-contained object definition classes.
// -----------------------------------------------------------------------------

using System.Reflection;

namespace LightingShowcase.SceneGraph;

/// <summary>Reflection-backed registry for insertable object definitions.</summary>
public static class ScenePrimitiveRegistry
{
    private static readonly object Gate = new();
    private static bool initialized;
    private static readonly List<ISceneObjectDefinition> primitives = new();

    public static IReadOnlyList<ISceneObjectDefinition> Primitives
    {
        get { EnsureInitialized(); return primitives; }
    }

    public static string[] DisplayNames => Primitives.Select(p => p.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Initializes the primitive registry exactly once in a thread-safe manner.</summary>
    public static void EnsureInitialized()
    {
        if (initialized) return;
        lock (Gate)
        {
            if (initialized) return;
            LoadPrimitiveAssemblies();
            DiscoverPrimitives();
            initialized = true;
        }
    }

    /// <summary>Returns true when a primitive matches the given name or kind.</summary>
    public static bool Contains(string? nameOrKind) => Find(nameOrKind) != null;

    /// <summary>Finds the primitive matching the given name or kind, or null when none matches.</summary>
    public static ISceneObjectDefinition? Find(string? nameOrKind)
    {
        if (string.IsNullOrWhiteSpace(nameOrKind)) return null;
        EnsureInitialized();
        string key = Normalize(nameOrKind);
        return primitives.FirstOrDefault(p => Normalize(p.Kind) == key || Normalize(p.DisplayName) == key);
    }

    /// <summary>Loads object-library assemblies from the application directory.</summary>
    private static void LoadPrimitiveAssemblies()
    {
        string baseDirectory = AppContext.BaseDirectory;
        if (!Directory.Exists(baseDirectory)) return;

        foreach (string dll in Directory.EnumerateFiles(baseDirectory, "LightingShowcase.ObjectLibrary.*.dll"))
        {
            try
            {
                string fullPath = Path.GetFullPath(dll);
                if (AppDomain.CurrentDomain.GetAssemblies().Any(a => string.Equals(a.Location, fullPath, StringComparison.OrdinalIgnoreCase)))
                    continue;
                Assembly.LoadFrom(fullPath);
            }
            catch { }
        }
    }

    /// <summary>Discovers parameterless object definitions across loaded assemblies.</summary>
    private static void DiscoverPrimitives()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (Type type in types)
            {
                if (type.IsAbstract || !typeof(ISceneObjectDefinition).IsAssignableFrom(type) || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    if (Activator.CreateInstance(type) is ISceneObjectDefinition primitive && !primitives.Any(p => SamePrimitive(p, primitive)))
                        primitives.Add(primitive);
                }
                catch { }
            }
        }

        primitives.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns true when two definitions share a kind or display name.</summary>
    private static bool SamePrimitive(ISceneObjectDefinition a, ISceneObjectDefinition b) =>
        string.Equals(a.Kind, b.Kind, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lowercases the alphanumeric characters of the given value.</summary>
    private static string Normalize(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
