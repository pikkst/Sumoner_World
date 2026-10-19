using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using SumonerWorld.Core.Cognition;
using SumonerWorld.Core.Cognition.Opinions;
using SumonerWorld.Core.Ecs;
using SumonerWorld.Core.World;

namespace SumonerWorld.Tests.Core.Diagnostics;

/// <summary>
/// Regression guard (R44) for the M1-07 determinism bug: hashing a struct's raw bytes includes its padding, whose content is
/// undefined, so identical states could hash differently. Every type hashed as raw bytes must have no padding.
/// </summary>
public class StateHashPaddingTests
{
    public static TheoryData<Type> RawHashedTypes => new()
    {
        typeof(Entity), typeof(StableId), typeof(Position), typeof(Facing), typeof(MoveTarget), typeof(AiTier), typeof(FocusRef),
        typeof(CurrentEmote), typeof(CurrentGoal), typeof(GroupRef),
        typeof(Opinion), typeof(Personality),
    };

    [Theory]
    [MemberData(nameof(RawHashedTypes))]
    public void Types_hashed_as_raw_bytes_have_no_padding(Type type)
    {
        Assert.True(
            FieldBytes(type) == SizeOf(type),
            $"{type.Name} is {SizeOf(type)} bytes but its fields are {FieldBytes(type)}: hash it field by field (see StateHash).");
    }

    private static int FieldBytes(Type type)
    {
        if (type.IsPrimitive || type.IsEnum)
        {
            return SizeOf(type);
        }

        if (type == typeof(Vector3))
        {
            return 3 * sizeof(float);
        }

        return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Sum(field => FieldBytes(field.FieldType));
    }

    private static int SizeOf(Type type) =>
        (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;
}
