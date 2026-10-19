using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SumonerWorld.Net.Messages;

namespace SumonerWorld.Net.Sessions;

/// <summary>Checks a client's auth ticket and names the account it belongs to (TDD §3.3).</summary>
public interface IAuthenticator
{
    /// <summary>True and the account id if the ticket is valid; otherwise false and why.</summary>
    bool TryAuthenticate(string ticket, out ulong accountId, out RejectReason reason);
}

/// <summary>
/// Development authentication for the Docker dev server and tests (TDD §3.1a): tickets look like <c>dev:&lt;accountId&gt;:&lt;secret&gt;</c>
/// and the secret is compared in constant time. Never enabled on public servers; Steam tickets replace it (M1 spike).
/// </summary>
public sealed class DevTokenAuthenticator : IAuthenticator
{
    private const string Prefix = "dev:";

    private readonly byte[] _secret;

    /// <summary>Creates the authenticator with the server's dev secret (from configuration, e.g. <c>SW_DEV_SECRET</c>).</summary>
    public DevTokenAuthenticator(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        _secret = Encoding.UTF8.GetBytes(secret);
    }

    /// <summary>Builds a ticket for <paramref name="accountId"/> (clients and tests).</summary>
    public static string Ticket(ulong accountId, string secret) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{accountId}:{secret}");

    /// <inheritdoc />
    public bool TryAuthenticate(string ticket, out ulong accountId, out RejectReason reason)
    {
        accountId = 0;
        reason = RejectReason.NotAuthenticated;
        if (string.IsNullOrEmpty(ticket) || !ticket.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int separator = ticket.IndexOf(':', Prefix.Length);
        if (separator < 0
            || !ulong.TryParse(ticket.AsSpan(Prefix.Length, separator - Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out ulong id)
            || id == 0)
        {
            return false;
        }

        byte[] offered = Encoding.UTF8.GetBytes(ticket[(separator + 1)..]);
        if (!CryptographicOperations.FixedTimeEquals(offered, _secret))
        {
            return false;
        }

        accountId = id;
        return true;
    }
}
