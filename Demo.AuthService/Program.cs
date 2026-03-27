using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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
    db.Database.Migrate();  // 執行所有尚未套用的 Migration

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
var jwtSection      = builder.Configuration.GetSection("Jwt");
var secretKey       = jwtSection["SecretKey"]!;
var issuer          = jwtSection["Issuer"]!;
var audience        = jwtSection["Audience"]!;
var signingKey      = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
var refreshTokenTtl = TimeSpan.FromDays(7);

// ── 輔助方法：產生 Access Token ───────────────────────────────
string GenerateAccessToken(User user)
{
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

    return new JwtSecurityTokenHandler().WriteToken(token);
}

// ── 輔助方法：產生 Refresh Token（隨機字串，非 JWT）──────────
string GenerateRefreshToken() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

// ── POST /auth/login ─────────────────────────────────────────
app.MapPost("/auth/login", async (LoginRequest req, AuthDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();

    // 產生並儲存 Refresh Token
    var refreshToken = new RefreshToken
    {
        Token     = GenerateRefreshToken(),
        UserId    = user.Id,
        ExpiresAt = DateTime.UtcNow.Add(refreshTokenTtl),
    };
    db.RefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken  = GenerateAccessToken(user),
        refreshToken = refreshToken.Token,
    });
});

// ── POST /auth/refresh ───────────────────────────────────────
app.MapPost("/auth/refresh", async (RefreshRequest req, AuthDbContext db) =>
{
    var stored = await db.RefreshTokens
        .Include(r => r.User)
        .FirstOrDefaultAsync(r => r.Token == req.RefreshToken);

    // Token 不存在、已撤銷、或已過期 → 一律回 401
    if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
        return Results.Unauthorized();

    // 換發：舊 Token 撤銷，產生新 Token（Refresh Token Rotation）
    stored.IsRevoked = true;

    var newRefreshToken = new RefreshToken
    {
        Token     = GenerateRefreshToken(),
        UserId    = stored.UserId,
        ExpiresAt = DateTime.UtcNow.Add(refreshTokenTtl),
    };
    db.RefreshTokens.Add(newRefreshToken);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken  = GenerateAccessToken(stored.User),
        refreshToken = newRefreshToken.Token,
    });
});

// ── POST /auth/logout ────────────────────────────────────────
app.MapPost("/auth/logout", async (RefreshRequest req, AuthDbContext db) =>
{
    var stored = await db.RefreshTokens
        .FirstOrDefaultAsync(r => r.Token == req.RefreshToken);

    if (stored is not null)
    {
        stored.IsRevoked = true;
        await db.SaveChangesAsync();
    }

    return Results.Ok();
});

app.Run();

record LoginRequest  (string Username, string Password);
record RefreshRequest(string RefreshToken);
