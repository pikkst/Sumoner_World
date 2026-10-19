namespace SumonerWorld.Core.Intents;

/// <summary>Per-player cooldowns of intent kinds (TDD §3.2 "authorize ... cooldown"), checked in a transaction's validation.</summary>
public sealed class CooldownTracker
{
    private readonly Dictionary<(ulong Account, ushort Kind), ulong> _readyAt = [];

    /// <summary>True when <paramref name="account"/> may use <paramref name="kind"/> at <paramref name="now"/>.</summary>
    public bool IsReady(ulong account, ushort kind, ulong now) =>
        !_readyAt.TryGetValue((account, kind), out ulong readyAt) || now >= readyAt;

    /// <summary>Starts a cooldown of <paramref name="ticks"/>.</summary>
    public void Start(ulong account, ushort kind, ulong now, ulong ticks) => _readyAt[(account, kind)] = now + ticks;

    /// <summary>Forgets a player's cooldowns (they left).</summary>
    public void Forget(ulong account)
    {
        foreach ((ulong Account, ushort Kind) key in _readyAt.Keys.Where(key => key.Account == account).ToList())
        {
            _readyAt.Remove(key);
        }
    }
}
