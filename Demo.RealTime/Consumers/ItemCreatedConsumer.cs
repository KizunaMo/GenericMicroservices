using Demo.Contracts;
using Demo.RealTime.Hubs;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Consumers;

// 這個 Consumer 扮演「橋接者」角色：
// 訂閱 RabbitMQ 的 ItemCreated 事件 → 透過 SignalR 推送給所有 Unity 客戶端
public class ItemCreatedConsumer : IConsumer<ItemCreated>
{
    // IHubContext<T>：在 Hub 外部操作 SignalR 的介面
    // 不同於 Hub 內部的 Clients，這個介面讓任何注入點都能推送訊息
    private readonly IHubContext<ItemHub> _hubContext;
    private readonly ILogger<ItemCreatedConsumer> _logger;

    public ItemCreatedConsumer(IHubContext<ItemHub> hubContext, ILogger<ItemCreatedConsumer> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        var item = context.Message;

        _logger.LogInformation(
            "[RealTime] 收到 ItemCreated 事件 → Id: {Id}, Name: {Name}，推送 SignalR",
            item.Id, item.Name);

        // 推送給所有連線到 /hub/items 的 Unity 客戶端
        // "OnItemCreated"：Unity 端要監聽的事件名稱
        await _hubContext.Clients.All.SendAsync("OnItemCreated", new
        {
            item.Id,
            item.Name,
            item.CreatedAt
        });
    }
}
