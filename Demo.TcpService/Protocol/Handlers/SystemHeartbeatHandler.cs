namespace Demo.TcpService.Protocol.Handlers;

public class SystemHeartbeatHandler : IMessageHandler
{
    public Task<string> HandleAsync(string data)
    {
        return Task.FromResult("[System.Heartbeat] Pong");
    }
}
