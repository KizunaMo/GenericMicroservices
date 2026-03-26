var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

const string corsPolicy = "GatewayPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:5200",  // 開發時 HTML 測試頁
                "http://localhost:3000"   // 之後 Web 前端預留
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors(corsPolicy);

// RequireCors 讓 YARP 的 endpoint 也套用 CORS
// 才能正確回應瀏覽器的 OPTIONS preflight 請求
app.MapReverseProxy().RequireCors(corsPolicy);

app.Run();
