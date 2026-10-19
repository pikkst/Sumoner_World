namespace SumonerWorld.Core.Ecs;

/// <summary>
/// Gives each component type a small process-wide index so a world finds its store with an array lookup instead of a
/// dictionary. The index is only an in-memory slot number: it is never saved, sent or used to order anything, so it cannot
/// affect determinism (R11).
/// </summary>
internal static class ComponentTypeIndex
{
    private static int _count;

    /// <summary>Number of component types seen so far.</summary>
    public static int Count => Volatile.Read(ref _count);

    /// <summary>Returns a new index.</summary>
    public static int Next() => Interlocked.Increment(ref _count) - 1;
}

/// <summary>The index of component type <typeparamref name="T"/>.</summary>
/// <typeparam name="T">Component type.</typeparam>
internal static class ComponentTypeIndex<T>
    where T : struct
{
    /// <summary>The index, assigned the first time the type is used.</summary>
    public static readonly int Value = ComponentTypeIndex.Next();
}
