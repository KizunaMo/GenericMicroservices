var builder = WebApplication.CreateBuilder(args);

// 從 appsettings.json 讀取 YARP 路由設定
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.MapReverseProxy();

app.Run();
