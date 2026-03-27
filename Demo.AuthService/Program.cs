using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Demo.AuthService.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ── DB（auth_db）────────────────────────────────────────────
builder.Services.AddDbContext<AuthDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("AuthDb")));

// ── JWT 驗證（保護 /auth/users/** 端點）──────────────────────
var jwtCfg = builder.Configuration.GetSection("Jwt");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtCfg["Issuer"],
            ValidAudience            = jwtCfg["Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes(jwtCfg["SecretKey"]!)),
        };
    });
builder.Services.AddAuthorization();

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

app.UseAuthentication();
app.UseAuthorization();

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

// ── POST /auth/users（建立新使用者，需要 admin role）────────
app.MapPost("/auth/users", async (RegisterRequest req, AuthDbContext db) =>
{
    if (await db.Users.AnyAsync(u => u.Username == req.Username))
        return Results.Conflict(new { message = $"Username '{req.Username}' already exists." });

    db.Users.Add(new User
    {
        Username     = req.Username,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password),
        Role         = req.Role,
    });
    await db.SaveChangesAsync();

    return Results.Ok(new { message = $"User '{req.Username}' created." });
}).RequireAuthorization();

// ── GET /auth/users（列出所有使用者，需要 admin role）────────
app.MapGet("/auth/users", async (AuthDbContext db) =>
{
    var users = await db.Users
        .Select(u => new { u.Id, u.Username, u.Role })
        .ToListAsync();

    return Results.Ok(users);
}).RequireAuthorization();

// ── DELETE /auth/users/{id}（刪除使用者，需要 admin role）────
app.MapDelete("/auth/users/{id:int}", async (int id, AuthDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();

    // 同時撤銷該使用者所有 Refresh Token
    var tokens = db.RefreshTokens.Where(r => r.UserId == id);
    foreach (var t in tokens) t.IsRevoked = true;

    db.Users.Remove(user);
    await db.SaveChangesAsync();

    return Results.Ok(new { message = $"User '{user.Username}' deleted." });
}).RequireAuthorization();

// ── PUT /auth/users/{id}/password（修改密碼，需要登入）───────
app.MapPut("/auth/users/{id:int}/password", async (int id, ChangePasswordRequest req, AuthDbContext db) =>
{
    var user = await db.Users.FindAsync(id);
    if (user is null) return Results.NotFound();

    if (!BCrypt.Net.BCrypt.Verify(req.OldPassword, user.PasswordHash))
        return Results.BadRequest(new { message = "Current password is incorrect." });

    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword);

    // 密碼修改後撤銷所有 Refresh Token，強制重新登入
    var tokens = db.RefreshTokens.Where(r => r.UserId == id);
    foreach (var t in tokens) t.IsRevoked = true;

    await db.SaveChangesAsync();

    return Results.Ok(new { message = "Password changed. Please login again." });
}).RequireAuthorization();

app.Run();

record LoginRequest        (string Username, string Password);
record RefreshRequest      (string RefreshToken);
record RegisterRequest     (string Username, string Password, string Role);
record ChangePasswordRequest(string OldPassword, string NewPassword);
