# Phase 5-A：JWT 身份驗證

## JWT 是什麼？

JWT（JSON Web Token）是一種**無狀態的身份驗證機制**。

傳統 Session 做法：
```
Client 登入 → Server 產生 Session ID → 存在 Server 記憶體
Client 每次請求帶 Session ID → Server 查記憶體確認
```

JWT 做法：
```
Client 登入 → Server 產生 Token（含使用者資訊）→ 不存在 Server
Client 每次請求帶 Token → Server 用密鑰驗簽，不用查 DB
```

**優點**：Server 不需要儲存狀態，適合微服務（每個服務都能獨立驗證）。

---

## JWT Token 結構

```
eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9
.
eyJzdWIiOiJ1c2VyMSIsInJvbGUiOiJhZG1pbiIsImV4cCI6MTcxMjAwMDAwMH0
.
SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c
```

用 `.` 分成三段，每段都是 Base64Url 編碼：

| 段落      | 原始內容（JSON）                            | 說明                     |
|-----------|---------------------------------------------|--------------------------|
| Header    | `{"alg":"HS256","typ":"JWT"}`               | 使用的簽名算法            |
| Payload   | `{"sub":"user1","role":"admin","exp":...}`  | Claims（使用者資訊）      |
| Signature | `HMAC-SHA256(header.payload, 密鑰)`        | 防止竄改，只有 Server 能簽 |

> Base64Url 不是加密，任何人都能解碼 Payload 看到內容。
> 所以 **不要把密碼或敏感資訊放進 Payload**。

---

## Claims 是什麼？

Claims 是放在 Payload 裡的鍵值對，代表「關於這個使用者的聲明」：

| Claim 名稱 | 說明                              |
|------------|-----------------------------------|
| `sub`      | Subject，通常放 userId            |
| `name`     | 使用者名稱                        |
| `role`     | 角色（admin、user...）            |
| `exp`      | 過期時間（Unix timestamp）        |
| `iss`      | Issuer，誰簽發的                  |
| `aud`      | Audience，給誰用的                |

自訂 Claims 也完全可以：
```csharp
new Claim("tenant_id", "company-abc")
```

---

## Token 如何產生（C#）

```csharp
// 1. 定義 Claims（放進 Token 的資訊）
var claims = new[]
{
    new Claim(ClaimTypes.Name, "user1"),
    new Claim(ClaimTypes.Role, "admin"),
};

// 2. 密鑰（只有 Server 知道）
var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("your-secret-key-32chars-min"));
var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

// 3. 建立 Token
var token = new JwtSecurityToken(
    issuer:             "demo-app",
    audience:           "demo-app",
    claims:             claims,
    expires:            DateTime.UtcNow.AddHours(1),
    signingCredentials: creds
);

// 4. 序列化成字串
var tokenString = new JwtSecurityTokenHandler().WriteToken(token);
```

---

## Token 如何驗證（Gateway）

```csharp
// Program.cs
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,   // 驗證過期時間
            ValidateIssuerSigningKey = true,
            ValidIssuer              = "demo-app",
            ValidAudience            = "demo-app",
            IssuerSigningKey         = new SymmetricSecurityKey(
                                           Encoding.UTF8.GetBytes("your-secret-key-32chars-min"))
        };
    });

app.UseAuthentication();  // 解析 Token
app.UseAuthorization();   // 套用授權規則
```

Client 請求時帶上：
```
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

---

## 本專案架構：Gateway 集中驗證

```
                          Client（瀏覽器 / Postman）
                                    |
              ┌─────────────────────┼─────────────────────┐
              |                     |                     |
     POST /auth/login        GET /api/**           GET /hub/**
     （不帶 Token）          （帶 Access Token）   （帶 Access Token）
              |                     |                     |
              v                     v                     v
        ┌─────────────────────────────────────────────────────┐
        │              Demo.Gateway  :5000 (HTTP)             │
        │                           :5001 (HTTPS)             │
        │                                                     │
        │  /auth/**  → 不驗 Token，直接轉發給 AuthService        │
        │  /api/**   → 驗 Token（JwtBearer Middleware）        │
        │              通過 → 轉發 / 失敗 → 401                 │
        └───────────────┬─────────────────┬───────────────────┘
                        |                 |
                        v                 v
          ┌─────────────────┐   ┌──────────────────┐
          │ Demo.AuthService│   │ Demo.DataService  │
          │    :5100        │   │    :5128          │
          │                 │   │                   │
          │ 查 auth_db      │   │ 內部服務，          │
          │ 驗密碼，簽發      │   │ 不重複驗 Token     │
          │ Access Token +  │   │                   │
          │ Refresh Token   │   └──────────────────┘
          └─────────────────┘
```

**為什麼集中在 Gateway 驗證？**

- 內部服務不需要各自實作驗證邏輯
- 統一的安全策略，一個地方修改全部生效
- 內部服務假設「能到達我這裡的請求都已經通過驗證」

---

## 套件

```
Microsoft.AspNetCore.Authentication.JwtBearer  版本需對應 .NET 版本
```

安裝：
```bash
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
```

> 必須指定 `--version 8.0.0`，否則 NuGet 會裝最新版（v10）導致與 .NET 8 不相容。
> 規則：套件版本主號對應 .NET 主號（.NET 8 → 套件 8.x.x）。

---

## bcrypt 密碼雜湊

### 雜湊 vs 加密

```
加密（Encryption）：可以用密鑰解密還原原始內容
雜湊（Hashing）：   單向，不可逆，無法還原原始密碼

"password" ──bcrypt──► "$2a$11$abc123xyz..."
                               │
                               └── 無法反推回 "password"
```

後端永遠不知道使用者的原始密碼，**包括開發者自己**。這是設計目的。

### 驗證方式

不是解密，而是重新雜湊再比對：

```csharp
// 儲存時
PasswordHash = BCrypt.Net.BCrypt.HashPassword("password");
// → "$2a$11$randomSaltEmbedded...hashedResult"

// 驗證時
BCrypt.Net.BCrypt.Verify("password", storedHash);
// 對輸入的密碼重新走一次雜湊流程，比對結果是否一致
// → true / false
```

bcrypt 每次雜湊結果都不同（內含隨機 salt），但 Verify 仍能正確比對。

### 忘記密碼怎麼辦？

因為無法還原，「忘記密碼」的標準做法是「重設密碼」而不是「找回密碼」：

```
使用者點「忘記密碼」→ 輸入 Email
  → Server 產生一次性重設連結（有時效，存 DB）
  → 寄到使用者 Email
  → 使用者點連結 → 輸入新密碼
  → Server 用新密碼重新雜湊存入 DB，舊雜湊覆蓋
```

> 正確的網站永遠不會「寄你的密碼給你」，只會讓你設新的。
> 如果收到含有明文密碼的 Email，代表該網站密碼沒有雜湊，有資安風險。

---

## 密鑰管理

開發環境：放在 `appsettings.json`（不可 commit 到公開 repo）
```json
{
  "Jwt": {
    "SecretKey": "dev-secret-key-must-be-at-least-32-chars",
    "Issuer":    "demo-app",
    "Audience":  "demo-app"
  }
}
```

正式環境：用環境變數注入，不寫死在設定檔：
```bash
export Jwt__SecretKey="production-secret-key"
```

> HS256 密鑰最少 32 bytes（256 bits），否則會拋出例外。

---

## 本專案架構：Gateway 驗證，AuthService 簽發

```
Client
  │
  │ 流程 1：登入取得 Token
  ├── POST /auth/login { username, password }
  │              │
  │              ▼
  │        Demo.Gateway :5000
  │        （/auth/** 不驗 Token，直接轉發）
  │              │
  │              ▼
  │        Demo.AuthService :5100
  │        查 auth_db（:5432）→ bcrypt 驗密碼
  │        → 產生 Access Token（JWT，1小時）
  │        → 產生 Refresh Token（隨機字串，存 auth_db，7天）
  │              │
  │        回傳 { accessToken, refreshToken }
  │              │
  │    ◄─────────┘
  │
  │ 流程 2：帶 Token 呼叫受保護 API
  ├── GET /api/items
  │   Header: Authorization: Bearer <accessToken>
  │              │
  │              ▼
  │        Demo.Gateway :5000
  │        JwtBearer Middleware 驗 Token：
  │          ├── 驗 Signature（密鑰比對）
  │          ├── 驗 exp（過期時間）
  │          └── 驗 Issuer / Audience
  │              │ 通過             │ 失敗
  │              ▼                  ▼
  │        轉發給              回 401 Unauthorized
  │   Demo.DataService :5128       （不轉發）
  │
  │ 流程 3：Access Token 過期，自動換新
  └── POST /auth/refresh { refreshToken: "..." }
                 │
                 ▼
           Demo.Gateway :5000
           （/auth/** 不驗 Token）
                 │
                 ▼
           Demo.AuthService :5100
           查 auth_db 確認 Refresh Token 有效且未撤銷
           舊 Refresh Token 標記 IsRevoked = true（Rotation）
           產生新的 Access Token + 新的 Refresh Token
                 │
           回傳 { accessToken, refreshToken }
```

> Gateway 只驗 Token（無狀態，不查 DB），AuthService 才簽發 Token（有狀態，需要 auth_db）。

---

## Token 有效期設定

在 AuthService Program.cs：
```csharp
expires: DateTime.UtcNow.AddHours(1),  // Access Token 有效期
```

常見設定參考：

| 場景           | Access Token    | Refresh Token   |
|----------------|-----------------|-----------------|
| 一般 Web App   | 15 分鐘 ~ 1 小時 | 7 ~ 30 天       |
| 銀行 / 高安全性 | 5 ~ 15 分鐘     | 無（每次重新登入）|
| 手機 App       | 1 小時          | 90 天 ~ 1 年    |

原則：**安全需求越高 → Token 越短**。沒有絕對標準，依業務需求決定。

---

## Refresh Token 機制

### 為什麼需要？

只有 Access Token 的問題：
```
Access Token 過期（1小時）
  → Client 必須重新輸入帳密
  → 使用者體驗很差
```

Refresh Token 的解法：登入時同時發兩個 Token：

| Token         | 用途                   | 有效期  | 儲存位置      |
|---------------|------------------------|---------|---------------|
| Access Token  | 呼叫 API（帶在 Header）| 短      | Client 記憶體 |
| Refresh Token | 換新 Access Token 用   | 長      | Client + DB   |

### 完整流程

```
① 使用者登入
     → 取得 Access Token（1小時）+ Refresh Token（7天）
     → 每次登入都會產生全新的 Token

② 正常使用（Access Token 有效期內）
     → 每個請求帶 Access Token
     → Gateway 驗證通過 → 轉發

③ Access Token 過期 → 收到 401
     → Client 自動在背景呼叫 POST /auth/refresh
     → Server 查 DB 確認 Refresh Token 有效
     → 回傳新的 Access Token
     → 使用者完全無感，繼續使用

④ Refresh Token 也過期（7天沒開 App）
     → 才真正需要使用者重新登入
```

### 為什麼 Refresh Token 必須存 DB？

```
Access Token  → 無狀態，Gateway 用密鑰驗就好，不需查 DB
Refresh Token → 必須存 DB，原因：

  使用者登出 → 刪除 DB 裡的 Refresh Token → 立即失效
  帳號被封鎖 → 刪除 DB 裡的 Refresh Token → 立即失效
  密碼被修改 → 刪除所有 Refresh Token     → 所有裝置登出

  如果不存 DB → 7天內還是能換新 Token，無法主動撤銷
```

### 什麼時候真的需要重新登入？

1. Refresh Token 到期（長時間沒使用）
2. 主動登出（刪除 Client 的 Token + Server 刪除 DB 的 Refresh Token）
3. 管理員封鎖帳號
4. 密碼被修改（通常讓所有 Refresh Token 一起失效）

---

## 驗證流程總覽

```
1. Client → POST /auth/login { username, password }
2. AuthService 查 DB，bcrypt 驗證密碼
   → 產生 Access Token + Refresh Token
   → Refresh Token 存入 auth_db
   → 回傳兩個 Token 給 Client

3. Client → GET /api/items
            Authorization: Bearer <access_token>
4. Gateway 的 JwtBearer Middleware 解析並驗證 Access Token
   ├── 驗簽（Signature 對不對）
   ├── 驗過期（exp 有沒有超過）
   └── 驗 Issuer / Audience
5. 驗證通過 → YARP 轉發到 DataService
   驗證失敗 → 回 401 Unauthorized，不轉發

6. Access Token 過期 → Client 自動呼叫：
   POST /auth/refresh { refreshToken: "..." }
   → AuthService 查 DB 確認 Refresh Token 有效且未過期
   → 回傳新的 Access Token
```

---

## 實作步驟（從零開始重現）

### 涉及的文件

**Demo.AuthService（新服務）**：

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.AuthService.csproj` | 新增 | 安裝 JWT、bcrypt、EF Core 套件 |
| `Data/User.cs` | 新增 | 使用者資料模型 |
| `Data/RefreshToken.cs` | 新增 | Refresh Token 資料模型 |
| `Data/AuthDbContext.cs` | 新增 | auth_db 的 EF Core 橋接器 |
| `Migrations/` | 新增（自動產生）| DB Schema 版本記錄 |
| `Program.cs` | 新增 | 所有端點（login、refresh、logout、users CRUD）|
| `appsettings.json` | 新增 | JWT 設定（Issuer、Audience）、DB 連線字串留空 |
| `Properties/launchSettings.json` | 新增 | Port 5100 |

**Demo.Gateway（修改既有）**：

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.Gateway.csproj` | 修改 | 安裝 JWT 驗證套件 |
| `Program.cs` | 修改 | 加入 JWT 驗證設定 |
| `appsettings.json` | 修改 | 加入 JWT 設定區段、auth 路由、各路由授權 Policy |

---

### 步驟 1：建立 Demo.AuthService 專案

```bash
dotnet new web -n Demo.AuthService
dotnet sln add Demo.AuthService/Demo.AuthService.csproj
```

安裝套件：
```bash
cd Demo.AuthService
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
dotnet add package BCrypt.Net-Next
dotnet add package Microsoft.EntityFrameworkCore --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.0.0
```

---

### 步驟 2：建立資料模型

**新增檔案**：`Demo.AuthService/Data/User.cs`

```csharp
namespace Demo.AuthService.Data;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;  // bcrypt 雜湊，不存明文
    public string Role { get; set; } = "user";
}
```

**新增檔案**：`Demo.AuthService/Data/RefreshToken.cs`

```csharp
namespace Demo.AuthService.Data;

public class RefreshToken
{
    public int Id { get; set; }
    public string Token { get; set; } = string.Empty;        // 隨機字串（非 JWT）
    public int UserId { get; set; }                           // 外鍵，指向哪個 User
    public User User { get; set; } = null!;                  // Navigation Property，讓 EF Core 能 JOIN
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; } = false;             // 撤銷後設為 true
}
```

**為什麼 RefreshToken 要存 DB**：Access Token 是無狀態的（JWT），但 Refresh Token 需要能主動撤銷（登出、刪帳號、改密碼），所以必須存 DB 才能查詢和標記撤銷。

---

### 步驟 3：建立 DbContext 並執行 Migration

**新增檔案**：`Demo.AuthService/Data/AuthDbContext.cs`

```csharp
using Microsoft.EntityFrameworkCore;

namespace Demo.AuthService.Data;

public class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }
}
```

建立 Migration（在 Demo.AuthService 目錄執行）：

```bash
dotnet ef migrations add InitialCreate
dotnet ef database update
```

---

### 步驟 4：實作 AuthService Program.cs

**修改檔案**：`Demo.AuthService/Program.cs`

**4-A：輔助方法（產生 Token）**

```csharp
// 產生 Access Token（JWT）
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
        expires:            DateTime.UtcNow.AddHours(1),     // Access Token 有效期 1 小時
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256)
    );

    return new JwtSecurityTokenHandler().WriteToken(token);  // 序列化成字串
}

// 產生 Refresh Token（隨機字串，不是 JWT）
string GenerateRefreshToken() =>
    Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));  // 64 bytes 的隨機數
```

**4-B：POST /auth/login**

```csharp
app.MapPost("/auth/login", async (LoginRequest req, AuthDbContext db) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

    // BCrypt.Verify：重新雜湊輸入的密碼，比對是否和存的 Hash 一致
    if (user is null || !BCrypt.Net.BCrypt.Verify(req.Password, user.PasswordHash))
        return Results.Unauthorized();  // 使用者不存在或密碼錯誤，統一回 401（不說哪個錯）

    var refreshToken = new RefreshToken
    {
        Token     = GenerateRefreshToken(),
        UserId    = user.Id,
        ExpiresAt = DateTime.UtcNow.AddDays(7),  // Refresh Token 有效期 7 天
    };
    db.RefreshTokens.Add(refreshToken);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken  = GenerateAccessToken(user),
        refreshToken = refreshToken.Token,
    });
});
```

**4-C：POST /auth/refresh（Refresh Token Rotation）**

```csharp
app.MapPost("/auth/refresh", async (RefreshRequest req, AuthDbContext db) =>
{
    var stored = await db.RefreshTokens
        .Include(r => r.User)                                  // JOIN User 資料
        .FirstOrDefaultAsync(r => r.Token == req.RefreshToken);

    if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
        return Results.Unauthorized();

    // Rotation：舊 Token 撤銷，同時發新 Token
    // 若舊 Token 被偷，攻擊者使用時 Server 發現已撤銷（被正常使用者用過了），可以告警
    stored.IsRevoked = true;

    var newRefreshToken = new RefreshToken
    {
        Token     = GenerateRefreshToken(),
        UserId    = stored.UserId,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
    };
    db.RefreshTokens.Add(newRefreshToken);
    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        accessToken  = GenerateAccessToken(stored.User),
        refreshToken = newRefreshToken.Token,
    });
});
```

---

### 步驟 5：Gateway 加入 JWT 驗證

**目的**：Gateway 負責驗 Token（不簽發），通過才轉發請求

**安裝套件**：
```bash
cd Demo.Gateway
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
```

**修改檔案**：`Demo.Gateway/Program.cs`

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtSection = builder.Configuration.GetSection("Jwt");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSection["SecretKey"]!));

// 加入 JWT 驗證（只驗，不簽發）
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,             // 驗過期時間
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtSection["Issuer"],
            ValidAudience            = jwtSection["Audience"],
            IssuerSigningKey         = signingKey,       // 和 AuthService 使用相同密鑰
        };
    });
builder.Services.AddAuthorization();

// ...

app.UseAuthentication();   // 解析並驗證 Token，成功後填充 HttpContext.User
app.UseAuthorization();    // 根據 Policy 決定是否允許通過
```

---

### 步驟 6：在 YARP 路由設定授權

**目的**：指定哪些路由需要 Token，哪些不需要

**修改檔案**：`Demo.Gateway/appsettings.json`

```json
"Routes": {
  "auth-service-route": {
    "ClusterId": "auth-service-cluster",
    "Match": { "Path": "/auth/{**remainder}" }
    // 不加 AuthorizationPolicy → 不需要 Token（讓使用者能登入）
  },
  "data-service-route": {
    "ClusterId": "data-service-cluster",
    "AuthorizationPolicy": "Default",    // 需要有效 Token，否則 401
    "Match": { "Path": "/api/{**remainder}" }
  }
},
"Clusters": {
  "auth-service-cluster": {
    "Destinations": {
      "destination1": { "Address": "http://localhost:5100" }
    }
  }
  // ...
}
```

---

### 步驟 7：AuthService 自己也加 JWT 驗證

**目的**：保護使用者管理端點（/auth/users），不讓未登入的人操作

這是**兩層防線**：Gateway 擋外部未驗證請求，AuthService 自己再驗一次確保管理端點安全。

在 AuthService 的 `Program.cs`，加入和 Gateway 一樣的 JWT 驗證設定，然後在端點加：

```csharp
app.MapPost("/auth/users", async (...) =>
{
    // 建立使用者的邏輯
}).RequireAuthorization();  // ← 需要有效 Token 才能呼叫
```

---

### 驗證方式

```bash
# 1. 登入取得 Token
POST http://localhost:5000/auth/login
Body: { "username": "admin", "password": "password" }
→ 預期回傳 accessToken 和 refreshToken

# 2. 帶 Token 呼叫受保護的 API
GET http://localhost:5000/api/items
Header: Authorization: Bearer <accessToken>
→ 預期正常回傳資料

# 3. 不帶 Token 呼叫受保護的 API
GET http://localhost:5000/api/items
→ 預期回傳 401 Unauthorized

# 4. Access Token 過期後，用 Refresh Token 換新
POST http://localhost:5000/auth/refresh
Body: { "refreshToken": "<refreshToken>" }
→ 預期回傳新的 accessToken
```
