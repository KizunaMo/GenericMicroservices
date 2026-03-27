namespace Demo.TcpService.Protocol.Handlers;

public class ItemCreateHandler : IMessageHandler
{
    public Task<string> HandleAsync(string data)
    {
        var response = $"[Item.Create] Created item: '{data}'";
        return Task.FromResult(response);
    }
}
