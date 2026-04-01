using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Hubs;

// Unity 客戶端連接到這個 Hub，訂閱後端主動推送的事件
// 這個 Hub 是單向的：Server → Client（沒有 Client → Server 的方法）
// 推送由 ItemCreatedConsumer 透過 IHubContext<ItemHub> 觸發
public class ItemHub : Hub
{
}
