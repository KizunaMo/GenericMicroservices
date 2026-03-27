using Demo.GrpcService.Data;
using Demo.GrpcService.Services;
using GenericMicroservices;
using Microsoft.EntityFrameworkCore;

// 允許對內部服務使用明文（非 TLS）HTTP/2
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 注入 DataService 的 gRPC Client，由框架管理 Channel 生命週期
builder.Services.AddGrpcClient<DataItemService.DataItemServiceClient>(o =>
{
    o.Address = new Uri("http://localhost:5129");
});

builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<GrpcItemService>();
app.MapGrpcService<DataBridgeGrpcService>();
app.MapGet("/", () => "gRPC service is running.");

app.Run();
