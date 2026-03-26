using Microsoft.AspNetCore.SignalR;

namespace Demo.RealTime.Hubs;

public class ChatHub : Hub
{
    // Client 呼叫這個方法 → Server 廣播給所有人
    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }

    // Client 連線時觸發
    public override async Task OnConnectedAsync()
    {
        await Clients.All.SendAsync("ReceiveMessage", "System", $"{Context.ConnectionId} joined");
        await base.OnConnectedAsync();
    }

    // Client 離線時觸發
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Clients.All.SendAsync("ReceiveMessage", "System", $"{Context.ConnectionId} left");
        await base.OnDisconnectedAsync(exception);
    }
}
