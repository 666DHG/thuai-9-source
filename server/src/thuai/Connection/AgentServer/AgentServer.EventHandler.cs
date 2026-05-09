namespace Thuai.Connection;

using Thuai.Protocol.Messages;

public partial class AgentServer
{
    // Called by GameController to link socket to player token
    public void HandleAfterPlayerConnectEvent(object? sender, AfterPlayerConnectEventArgs e)
    {
        _socketTokens[e.SocketId] = e.Token;
        _socketRoles[e.SocketId] = SocketRole.Player;
    }
}
