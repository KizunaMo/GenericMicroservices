using Demo.RealTime.Hubs;
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

var app = builder.Build();

app.UseStaticFiles();
app.UseHttpMetrics();   // 追蹤每個 HTTP 請求的 method、status、duration

// CORS 由 Gateway 統一處理，這裡不需要設定
app.MapHub<ChatHub>("/hub/chat");
app.MapHealthChecks("/health");
app.UseMetricServer();   // 暴露 /metrics，讓 Prometheus 來抓

app.Run();
