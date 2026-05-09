namespace Thuai.Connection;

using System.Collections.Concurrent;
using Fleck;
using Serilog;
using Thuai.Protocol.Messages;

public partial class AgentServer
{
    public int Port { get; init; } = 14514;

    private WebSocketServer? _wsServer;
    private readonly ConcurrentDictionary<Guid, IWebSocketConnection> _sockets = new();
    private readonly ConcurrentDictionary<Guid, string> _socketTokens = new();
    private readonly ConcurrentDictionary<string, byte> _validTokens = new();

    public void RegisterValidToken(string token)
    {
        if (!string.IsNullOrEmpty(token))
            _validTokens.TryAdd(token, 0);
    }
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<string>> _socketRawTextReceivingQueue = new();
    private readonly ConcurrentDictionary<Guid, ConcurrentQueue<Message>> _socketMessageSendingQueue = new();
    private readonly ConcurrentDictionary<Guid, Task> _tasksForParsingMessage = new();
    private readonly ConcurrentDictionary<Guid, Task> _tasksForSendingMessage = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _cancellationTokenSources = new();

    private const int MaxMessageQueueSize = 11;
    private const int MessageProcessingInterval = 10; // ms

    public void Start()
    {
        _wsServer = new WebSocketServer($"ws://0.0.0.0:{Port}");
        _wsServer.Start(socket =>
        {
            socket.OnOpen = () => AddSocket(socket);
            socket.OnClose = () => RemoveSocket(socket.ConnectionInfo.Id);
            socket.OnMessage = message => OnMessageReceived(socket.ConnectionInfo.Id, message);
            socket.OnError = ex => Log.Error(ex, "WebSocket error for {SocketId}", socket.ConnectionInfo.Id);
        });

        Log.Information("AgentServer started on port {Port}", Port);
    }

    public void Stop()
    {
        foreach (var cts in _cancellationTokenSources.Values)
            cts.Cancel();

        _wsServer?.Dispose();
        Log.Information("AgentServer stopped");
    }

    private void OnMessageReceived(Guid socketId, string rawMessage)
    {
        if (!_socketRawTextReceivingQueue.TryGetValue(socketId, out var queue)) return;

        if (queue.Count >= MaxMessageQueueSize)
        {
            Log.Warning("Message queue overflow for socket {SocketId}, dropping message", socketId);
            return;
        }

        queue.Enqueue(rawMessage);
    }
}
