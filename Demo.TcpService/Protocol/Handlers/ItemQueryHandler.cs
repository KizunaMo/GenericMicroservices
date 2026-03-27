namespace Demo.TcpService.Protocol.Handlers;

public class ItemQueryHandler : IMessageHandler
{
    public Task<string> HandleAsync(string data)
    {
        // 模擬查詢：回傳假資料
        var response = $"[Item.Query] Result for '{data}': Sword, Shield, Potion";
        return Task.FromResult(response);
    }
}
