using Demo.RealTime.Hubs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
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

// CORS 由 Gateway 統一處理，這裡不需要設定
app.MapHub<ChatHub>("/hub/chat");
app.MapHealthChecks("/health");

app.Run();
