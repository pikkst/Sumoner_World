using Godot;
using SumonerWorld.Core.Diagnostics;
using SumonerWorld.Game.Net;
using SumonerWorld.Net.Hosting;
using SumonerWorld.Net.Messages;
using SumonerWorld.Net.Sessions;
using SumonerWorld.Net.Transport;

namespace SumonerWorld.Game.Hosting;

/// <summary>
/// Connects to a server and opens a session with a dev ticket. With <see cref="HostOptions.ExitOnWelcome"/> it is a smoke test:
/// exit code 0 when welcomed, 2 when rejected, 1 after <see cref="TimeoutSeconds"/> without an answer.
/// </summary>
public sealed partial class ClientHost : Node
{
    /// <summary>Seconds to wait for the server before giving up (smoke tests).</summary>
    public const double TimeoutSeconds = 20;

    private const int ExitWelcomed = 0;
    private const int ExitTimeout = 1;
    private const int ExitRejected = 2;

    private readonly HostOptions _options;
    private readonly ILog _log;
    private GodotENetTransport? _transport;
    private ClientEndpoint? _endpoint;
    private double _elapsed;

    /// <summary>Creates the host.</summary>
    public ClientHost(HostOptions options, ILog log)
    {
        _options = options;
        _log = log;
    }

    /// <inheritdoc />
    public override void _Ready()
    {
        _transport = GodotENetTransport.Connect(_options.Address, _options.Port, out ConnectionId _);
        string ticket = DevTokenAuthenticator.Ticket(_options.Account, _options.DevSecret);
        _endpoint = new ClientEndpoint(_transport, _log, new Hello(0, ticket));
        _log.Write(LogLevel.Info, "client.connecting", $"{_options.Address}:{_options.Port} as account {_options.Account}");
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        _transport!.Poll(_endpoint!);
        _transport.Flush();
        _elapsed += delta;
        if (!_options.ExitOnWelcome)
        {
            return;
        }

        switch (_endpoint!.State)
        {
            case ClientSessionState.InSession:
                GetTree().Quit(ExitWelcomed);
                break;
            case ClientSessionState.Rejected:
                GetTree().Quit(ExitRejected);
                break;
            default:
                if (_elapsed > TimeoutSeconds)
                {
                    _log.Write(LogLevel.Error, "client.timeout", $"no answer from {_options.Address}:{_options.Port} within {TimeoutSeconds} s");
                    GetTree().Quit(ExitTimeout);
                }

                break;
        }
    }

    /// <inheritdoc />
    public override void _ExitTree() => _transport?.Dispose();
}
