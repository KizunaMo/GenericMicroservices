using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── JWT 驗證設定（只驗 Token，不簽發）────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SecretKey"]!));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = signingKey,
        };
    });

builder.Services.AddAuthorization();

// ── YARP ──────────────────────────────────────────────────────
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// ── CORS ──────────────────────────────────────────────────────
const string corsPolicy = "GatewayPolicy";

builder.Services.AddCors(options =>
{
    options.AddPolicy(corsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://localhost:5200",
                "http://localhost:3000"
              )
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors(corsPolicy);
app.UseAuthentication();
app.UseAuthorization();

// ── YARP 路由 ─────────────────────────────────────────────────
// /auth/** → AuthService（不需 Token，讓使用者能登入）
// /api/**  → DataService（需要 Token）
// /hub/**  → RealTime（需要 Token）
// 授權規則在 appsettings.json 的 ReverseProxy Routes 設定
app.MapReverseProxy().RequireCors(corsPolicy);

app.Run();
