namespace Thuai.Connection;

using Serilog;
using Thuai.Protocol.Messages;

public partial class AgentServer
{
    private async Task SendMessageLoop(Guid socketId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (!_socketMessageSendingQueue.TryGetValue(socketId, out var queue))
                break;

            if (queue.TryDequeue(out var message))
            {
                try
                {
                    if (_sockets.TryGetValue(socketId, out var socket))
                    {
                        await socket.Send(message.Json);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error sending message to {SocketId}", socketId);
                }
            }
            else
            {
                await Task.Delay(MessageProcessingInterval, ct);
            }
        }
    }

    // Publish a private message addressed to a specific player by token.
    // Delivered to the player's bound socket plus every admin socket
    // (admins observe everything for debugging).
    public void Publish(Message message, string token)
    {
        foreach (var (socketId, socketToken) in _socketTokens)
        {
            if (socketToken == token && _socketMessageSendingQueue.TryGetValue(socketId, out var queue))
            {
                queue.Enqueue(message);
            }
        }
        foreach (var (socketId, role) in _socketRoles)
        {
            if (role == SocketRole.Admin && _socketMessageSendingQueue.TryGetValue(socketId, out var queue))
            {
                queue.Enqueue(message);
            }
        }
    }

    // Publish a public message to every registered socket — players, observers,
    // and admins. Sockets that haven't sent HELLO and never bound a token are
    // skipped, so unidentified clients can't passively scrape state.
    public void PublishToAll(Message message)
    {
        foreach (var (socketId, role) in _socketRoles)
        {
            if (role == SocketRole.Unidentified)
                continue;
            if (_socketMessageSendingQueue.TryGetValue(socketId, out var queue))
            {
                queue.Enqueue(message);
            }
        }
    }

    // Publish to observer sockets only. Use for broadcasts whose per-player view
    // is dispatched separately via Publish(token); admins already see the
    // per-player fan-out so we exclude them here to avoid double-delivery.
    public void PublishToObservers(Message message)
    {
        foreach (var (socketId, role) in _socketRoles)
        {
            if (role != SocketRole.Observer)
                continue;
            if (_socketMessageSendingQueue.TryGetValue(socketId, out var queue))
            {
                queue.Enqueue(message);
            }
        }
    }
}
