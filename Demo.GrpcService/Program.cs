using Demo.GrpcService.Data;
using Demo.GrpcService.Services;
using Demo.DataService;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;
using Serilog;

// 允許對內部服務使用明文（非 TLS）HTTP/2
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 注入 DataService 的 gRPC Client，由框架管理 Channel 生命週期
builder.Services.AddGrpcClient<DataItemService.DataItemServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5129");
});

builder.Services.AddGrpc();
builder.Services.AddHealthChecks();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.GrpcService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤進來的 gRPC 請求
        .AddHttpClientInstrumentation()   // 追蹤對 DataService gRPC 的呼叫
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));

var app = builder.Build();

app.UseHttpMetrics();   // 追蹤每個 HTTP 請求的 method、status、duration
app.MapGrpcService<GrpcItemService>();
app.MapGrpcService<DataBridgeGrpcService>();
app.MapGet("/", () => "gRPC service is running.");
app.MapHealthChecks("/health");
app.UseMetricServer();   // 暴露 /metrics，讓 Prometheus 來抓

app.Run();
