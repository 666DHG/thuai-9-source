namespace Thuai.Connection;

using System.Collections.Concurrent;
using Fleck;
using Serilog;
using Thuai.Protocol.Messages;

public partial class AgentServer
{
    private void AddSocket(IWebSocketConnection socket)
    {
        Guid id = socket.ConnectionInfo.Id;
        _sockets[id] = socket;
        _socketRawTextReceivingQueue[id] = new ConcurrentQueue<string>();
        _socketMessageSendingQueue[id] = new ConcurrentQueue<Message>();

        var cts = new CancellationTokenSource();
        _cancellationTokenSources[id] = cts;

        _tasksForParsingMessage[id] = Task.Run(() => ParseMessageLoop(id, cts.Token));
        _tasksForSendingMessage[id] = Task.Run(() => SendMessageLoop(id, cts.Token));

        Log.Information("Socket connected: {SocketId}", id);
    }

    private void RemoveSocket(Guid socketId)
    {
        if (_cancellationTokenSources.TryRemove(socketId, out var cts))
            cts.Cancel();

        _sockets.TryRemove(socketId, out _);
        _socketRawTextReceivingQueue.TryRemove(socketId, out _);
        _socketMessageSendingQueue.TryRemove(socketId, out _);
        _tasksForParsingMessage.TryRemove(socketId, out _);
        _tasksForSendingMessage.TryRemove(socketId, out _);
        _socketRoles.TryRemove(socketId, out _);

        if (_socketTokens.TryRemove(socketId, out var token))
        {
            AfterPlayerDisconnectEvent?.Invoke(this, new AfterPlayerDisconnectEventArgs
            {
                SocketId = socketId,
                Token = token
            });
            Log.Information("Player disconnected: {Token}", token);
        }
        else
        {
            Log.Information("Socket disconnected: {SocketId}", socketId);
        }
    }
}
