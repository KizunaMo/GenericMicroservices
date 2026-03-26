var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// CORS 統一在 Gateway 處理，後端服務不需要各自設定
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(
                "http://localhost:5200",  // 開發時 HTML 測試頁
                "http://localhost:3000"   // 之後 Web 前端預留
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();        // SignalR WebSocket 必須
    });
});

var app = builder.Build();

// UseCors 必須在 MapReverseProxy 之前
app.UseCors();
app.MapReverseProxy();

app.Run();
