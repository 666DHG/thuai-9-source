namespace Thuai.Connection;

using System.Text.Json;
using Serilog;
using Thuai.Protocol.Messages;

public partial class AgentServer
{
    private async Task ParseMessageLoop(Guid socketId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_socketRawTextReceivingQueue.TryGetValue(socketId, out var queue))
                break;

            if (queue.TryDequeue(out var rawText))
            {
                try
                {
                    ParseMessage(socketId, rawText);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error parsing message from {SocketId}", socketId);
                }
            }
            else
            {
                await Task.Delay(MessageProcessingInterval, ct);
            }
        }
    }

    private void ParseMessage(Guid socketId, string rawText)
    {
        using var doc = JsonDocument.Parse(rawText);
        var root = doc.RootElement;

        if (!root.TryGetProperty("messageType", out var msgTypeElement))
        {
            Log.Warning("Message from {SocketId} missing messageType", socketId);
            return;
        }

        string? messageType = msgTypeElement.GetString();

        if (messageType == "HELLO")
        {
            var hello = JsonSerializer.Deserialize<HelloMessage>(rawText);
            if (hello != null)
                HandleHello(socketId, hello);
            return;
        }

        PerformMessage? message = messageType switch
        {
            "LIMIT_BUY" => JsonSerializer.Deserialize<LimitBuyMessage>(rawText),
            "LIMIT_SELL" => JsonSerializer.Deserialize<LimitSellMessage>(rawText),
            "CANCEL_ORDER" => JsonSerializer.Deserialize<CancelOrderMessage>(rawText),
            "SUBMIT_REPORT" => JsonSerializer.Deserialize<SubmitReportMessage>(rawText),
            "SELECT_STRATEGY" => JsonSerializer.Deserialize<SelectStrategyMessage>(rawText),
            "ACTIVATE_SKILL" => JsonSerializer.Deserialize<ActivateSkillMessage>(rawText),
            _ => null
        };

        if (message == null)
        {
            Log.Warning("Unknown or invalid messageType: {MessageType} from {SocketId}", messageType, socketId);
            return;
        }

        // Implicit player binding via the first token-bearing action message,
        // kept for backwards compatibility with SDK clients that don't send HELLO.
        // Only pre-registered tokens are accepted.
        if (!string.IsNullOrEmpty(message.Token) && !_socketTokens.ContainsKey(socketId))
        {
            if (!_validTokens.ContainsKey(message.Token))
            {
                Log.Warning("Rejecting unknown token {Token} on socket {SocketId}", message.Token, socketId);
                return;
            }

            _socketTokens[socketId] = message.Token;
            _socketRoles[socketId] = SocketRole.Player;
            AfterPlayerConnectEvent?.Invoke(this, new AfterPlayerConnectEventArgs
            {
                SocketId = socketId,
                Token = message.Token
            });
            Log.Information("Player identified: {Token} on socket {SocketId}", message.Token, socketId);
        }

        // Action messages are only honoured from sockets registered as a player.
        // Observers and admins must use dedicated debug commands.
        if (!_socketRoles.TryGetValue(socketId, out var role) || role != SocketRole.Player)
        {
            Log.Warning("Rejecting action {MessageType} from non-player socket {SocketId}", messageType, socketId);
            return;
        }

        AfterMessageReceiveEvent?.Invoke(this, new AfterMessageReceiveEventArgs
        {
            SocketId = socketId,
            Message = message
        });
    }

    private void HandleHello(Guid socketId, HelloMessage hello)
    {
        // Once a role has been assigned, ignore subsequent HELLOs to prevent
        // role escalation on a single socket.
        if (_socketRoles.ContainsKey(socketId))
        {
            Log.Warning("Ignoring duplicate HELLO from socket {SocketId}", socketId);
            return;
        }

        switch (hello.Role?.ToLowerInvariant())
        {
            case "player":
                if (string.IsNullOrEmpty(hello.Token) || !_validTokens.ContainsKey(hello.Token))
                {
                    Log.Warning("Rejecting HELLO with invalid player token {Token} from {SocketId}",
                        hello.Token, socketId);
                    return;
                }
                _socketTokens[socketId] = hello.Token;
                _socketRoles[socketId] = SocketRole.Player;
                AfterPlayerConnectEvent?.Invoke(this, new AfterPlayerConnectEventArgs
                {
                    SocketId = socketId,
                    Token = hello.Token
                });
                Log.Information("Player connected via HELLO: {Token} on socket {SocketId}",
                    hello.Token, socketId);
                break;

            case "observer":
                _socketRoles[socketId] = SocketRole.Observer;
                Log.Information("Observer registered on socket {SocketId}", socketId);
                break;

            case "admin":
                if (string.IsNullOrEmpty(AdminSecret))
                {
                    Log.Warning("Rejecting HELLO admin from {SocketId}: admin mode disabled (set THUAI_ADMIN_SECRET to enable)", socketId);
                    return;
                }
                if (hello.AdminSecret != AdminSecret)
                {
                    Log.Warning("Rejecting HELLO admin from {SocketId}: bad secret", socketId);
                    return;
                }
                _socketRoles[socketId] = SocketRole.Admin;
                Log.Information("Admin registered on socket {SocketId}", socketId);
                break;

            default:
                Log.Warning("Rejecting HELLO with unknown role {Role} from {SocketId}",
                    hello.Role, socketId);
                break;
        }
    }
}
