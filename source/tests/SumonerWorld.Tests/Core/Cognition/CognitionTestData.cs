using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Memory;
using SumonerWorld.Core.Data;
using SumonerWorld.Core.Data.Definitions;

namespace SumonerWorld.Tests.Core.Cognition;

/// <summary>Builds cognition objects from the shipped game data, so tests exercise the real tuning values.</summary>
internal static class CognitionTestData
{
    public const int TicksPerDay = 86_400;
    public const ushort FedBy = 1;
    public const ushort HealedBy = 2;
    public const ushort SavedLifeOf = 3;
    public const ushort AttackedBy = 5;
    public const ushort KilledKin = 7;
    public const ushort BondedDied = 14;

    public static readonly MemorySubject Alice = new(SubjectKind.Player, 1001);
    public static readonly MemorySubject Bob = new(SubjectKind.Player, 1002);
    public static readonly NeedRates Needs = new() { Hunger = 60, Rest = 40, Safety = 50, Social = 70, Territory = 10 };

    private static readonly Lazy<DataRegistry> Shipped = new(LoadShipped);

    public static MemoryRules ShippedRules() =>
        new(Shipped.Value.MemoryEventTypes, Shipped.Value.OpinionWeights, TicksPerDay);

    public static (CreatureMindStore Store, MindHandle Mind) StoreWithMind(Personality personality)
    {
        var store = new CreatureMindStore(ShippedRules(), capacity: 8);
        return (store, store.Allocate(personality, Needs));
    }

    private static DataRegistry LoadShipped()
    {
        const string relative = "source/project-sumoner-worl/data";
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string root = Path.Combine(directory.FullName, relative);
            if (Directory.Exists(root))
            {
                return DataRegistryLoader.Load(Directory.EnumerateFiles(root, "*.json", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(path => new DataFile(Path.GetRelativePath(root, path), File.ReadAllText(path))));
            }
        }

        throw new DirectoryNotFoundException($"Could not find {relative} above {AppContext.BaseDirectory}.");
    }
}
