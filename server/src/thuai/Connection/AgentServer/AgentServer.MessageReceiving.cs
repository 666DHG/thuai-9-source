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
        // First, determine the message type
        using var doc = JsonDocument.Parse(rawText);
        var root = doc.RootElement;

        if (!root.TryGetProperty("messageType", out var msgTypeElement))
        {
            Log.Warning("Message from {SocketId} missing messageType", socketId);
            return;
        }

        string? messageType = msgTypeElement.GetString();
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

        // Handle player identification via token. Only bind sockets to tokens that
        // were pre-registered as valid players, so observers (or arbitrary clients)
        // sending a token-bearing message cannot impersonate a player and receive
        // private state.
        if (!string.IsNullOrEmpty(message.Token) && !_socketTokens.ContainsKey(socketId))
        {
            if (!_validTokens.ContainsKey(message.Token))
            {
                Log.Warning("Rejecting unknown token {Token} on socket {SocketId}", message.Token, socketId);
                return;
            }

            _socketTokens[socketId] = message.Token;
            AfterPlayerConnectEvent?.Invoke(this, new AfterPlayerConnectEventArgs
            {
                SocketId = socketId,
                Token = message.Token
            });
            Log.Information("Player identified: {Token} on socket {SocketId}", message.Token, socketId);
        }

        AfterMessageReceiveEvent?.Invoke(this, new AfterMessageReceiveEventArgs
        {
            SocketId = socketId,
            Message = message
        });
    }
}
