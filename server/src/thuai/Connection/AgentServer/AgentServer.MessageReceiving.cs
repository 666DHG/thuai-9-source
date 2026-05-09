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
            "DEBUG_QUERY" => JsonSerializer.Deserialize<DebugQueryMessage>(rawText),
            "DEBUG_GIVE_CARD" => JsonSerializer.Deserialize<DebugGiveCardMessage>(rawText),
            "DEBUG_INJECT_NEWS" => JsonSerializer.Deserialize<DebugInjectNewsMessage>(rawText),
            "DEBUG_ADVANCE_STAGE" => JsonSerializer.Deserialize<DebugAdvanceStageMessage>(rawText),
            "DEBUG_SET_PLAYER" => JsonSerializer.Deserialize<DebugSetPlayerMessage>(rawText),
            _ => null
        };

        if (message == null)
        {
            Log.Warning("Unknown or invalid messageType: {MessageType} from {SocketId}", messageType, socketId);
            return;
        }

        // Route DEBUG_* messages through the admin channel — only honoured from
        // sockets that authenticated as admin via HELLO.
        if (messageType?.StartsWith("DEBUG_") == true)
        {
            if (!_socketRoles.TryGetValue(socketId, out var debugRole) || debugRole != SocketRole.Admin)
            {
                Log.Warning("Rejecting {MessageType} from non-admin socket {SocketId}", messageType, socketId);
                return;
            }
            AfterAdminMessageEvent?.Invoke(this, new AfterAdminMessageEventArgs
            {
                SocketId = socketId,
                Message = message
            });
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

        // Action messages: players act only with their bound token; admins may
        // act on behalf of any registered token (used by the 服务器调试台 panel
        // to test rule scenarios); observers can't issue actions at all.
        if (!_socketRoles.TryGetValue(socketId, out var role))
        {
            Log.Warning("Rejecting action {MessageType} from unidentified socket {SocketId}", messageType, socketId);
            return;
        }

        switch (role)
        {
            case SocketRole.Player:
                if (!_socketTokens.TryGetValue(socketId, out var boundToken) || boundToken != message.Token)
                {
                    Log.Warning("Player socket {SocketId} (bound to {Bound}) attempted to act as {Attempted}",
                        socketId, _socketTokens.GetValueOrDefault(socketId), message.Token);
                    return;
                }
                break;
            case SocketRole.Admin:
                if (string.IsNullOrEmpty(message.Token) || !_validTokens.ContainsKey(message.Token))
                {
                    Log.Warning("Admin {SocketId} action targets unknown token {Token}", socketId, message.Token);
                    return;
                }
                break;
            default:
                Log.Warning("Rejecting action {MessageType} from {Role} socket {SocketId}",
                    messageType, role, socketId);
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
