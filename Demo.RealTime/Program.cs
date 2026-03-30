using Demo.RealTime.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseStaticFiles();

// CORS 由 Gateway 統一處理，這裡不需要設定
app.MapHub<ChatHub>("/hub/chat");
app.MapHealthChecks("/health");

app.Run();
