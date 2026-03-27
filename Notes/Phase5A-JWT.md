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
Client
  │  Authorization: Bearer <token>
  ↓
Demo.Gateway（5000）
  ├── POST /auth/login  → 不需要 Token，負責簽發 Token
  ├── GET  /api/**      → 需要 Token（Gateway 驗證後轉發給 DataService）
  └── /hub/**           → 需要 Token（可選）

DataService（5128）← 內部服務，不再重複驗證
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

## 本專案 /auth/login 設計（學習用，帳密寫死）

```csharp
// 學習用：帳密寫死，真實專案應查 DB
app.MapPost("/auth/login", (LoginRequest req, IConfiguration config) =>
{
    if (req.Username != "admin" || req.Password != "password")
        return Results.Unauthorized();

    // 產生 Token...
    return Results.Ok(new { token = tokenString });
});

record LoginRequest(string Username, string Password);
```

> 學習重點是 JWT 的機制，不是帳密管理。
> 真實專案的帳密驗證：查 DB + bcrypt 密碼雜湊。

---

## 驗證流程總覽

```
1. Client → POST /auth/login { username, password }
2. Gateway 驗證帳密 → 產生 JWT Token → 回傳給 Client
3. Client 儲存 Token（記憶體 / localStorage / Cookie）

4. Client → GET /api/items
           Authorization: Bearer <token>
5. Gateway 的 JwtBearer Middleware 解析並驗證 Token
   ├── 驗簽（Signature 對不對）
   ├── 驗過期（exp 有沒有超過）
   └── 驗 Issuer / Audience
6. 驗證通過 → YARP 轉發到 DataService
   驗證失敗 → 回 401 Unauthorized，不轉發
```
