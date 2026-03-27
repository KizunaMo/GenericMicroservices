using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Demo.AuthService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── DB（auth_db）────────────────────────────────────────────
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuthDb")));

var app = builder.Build();

// ── 啟動時自動建立資料表並植入初始使用者 ─────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
    db.Database.EnsureCreated();

    if (!db.Users.Any())
    {
        db.Users.Add(new User
        {
            Username     = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("password"),
            Role         = "admin",
        });
        db.SaveChanges();
    }
}

// ── JWT 設定（簽發用）────────────────────────────────────────
var jwtSection = builder.Configuration.GetSection("Jwt");
var secretKey  = jwtSection["SecretKey"]!;
var issuer     = jwtSection["Issuer"]!;
var audience   = jwtSection["Audience"]!;
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));

// ── 登入端點（查 DB + bcrypt 驗證 → 簽發 Token）─────────────
app.MapPost("/auth/login", async (LoginRequest req, AuthDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

    // 帳號不存在，或密碼雜湊比對失敗 → 一律回 401（不透露是哪個錯）
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    var claims = new[]
    {
        new Claim(ClaimTypes.Name,           user.Username),
        new Claim(ClaimTypes.Role,           user.Role),
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
    };

    var token = new JwtSecurityToken(
        issuer:             issuer,
        audience:           audience,
        claims:             claims,
        expires:            DateTime.UtcNow.AddHours(1),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
    );

    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
});

app.Run();

record LoginRequest(string Username, string Password);
