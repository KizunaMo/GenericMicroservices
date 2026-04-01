using Demo.RealTime.Consumers;
using Demo.RealTime.Hubs;
using MassTransit;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddSignalR();
builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.RealTime"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤 WebSocket upgrade 請求
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));

// 讓 RealTime 服務成為 RabbitMQ 的訂閱者
// 收到 ItemCreated 事件後，由 ItemCreatedConsumer 推送到 SignalR
builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ItemCreatedConsumer>();

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration["RabbitMq:Host"] ?? "localhost", "/", h =>
        {
            h.Username("guest");
            h.Password("guest");
        });

        // 自動為所有已註冊的 Consumer 建立對應的 Queue
        cfg.ConfigureEndpoints(context);
    });
});

var app = builder.Build();

app.UseStaticFiles();
app.UseHttpMetrics();   // 追蹤每個 HTTP 請求的 method、status、duration

// CORS 由 Gateway 統一處理，這裡不需要設定
app.MapHub<ChatHub>("/hub/chat");
app.MapHub<ItemHub>("/hub/items");   // Unity 連這裡，訂閱 Item 建立事件
app.MapHealthChecks("/health");
app.UseMetricServer();   // 暴露 /metrics，讓 Prometheus 來抓

app.Run();
