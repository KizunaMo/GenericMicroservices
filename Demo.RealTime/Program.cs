using Demo.RealTime.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();

var app = builder.Build();

// /hub/chat 是這個 Hub 的連線端點
app.MapHub<ChatHub>("/hub/chat");

app.Run();
