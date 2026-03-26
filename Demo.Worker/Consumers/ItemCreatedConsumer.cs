using Demo.Contracts;
using MassTransit;

namespace Demo.Worker.Consumers;

public class ItemCreatedConsumer : IConsumer<ItemCreated>
{
    private readonly ILogger<ItemCreatedConsumer> _logger;

    public ItemCreatedConsumer(ILogger<ItemCreatedConsumer> logger)
    {
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ItemCreated> context)
    {
        var item = context.Message;

        _logger.LogInformation(
            "[Worker] 收到 ItemCreated 事件 → Id: {Id}, Name: {Name}, 時間: {CreatedAt}",
            item.Id, item.Name, item.CreatedAt);

        // 這裡之後可以擴充：
        // - 寄通知信
        // - 推 SignalR 即時通知
        // - 更新快取
        // - 記錄稽核日誌

        await Task.CompletedTask;
    }
}
