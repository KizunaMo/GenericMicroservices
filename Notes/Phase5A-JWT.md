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
  ├── POST /auth/login ──────────────────────────► Demo.AuthService（:5100）
  │                                                  查 auth_db + bcrypt 驗證
  │                                                  → 簽發 Access Token + Refresh Token
  │
  ├── GET /api/** （帶 Access Token）
  │        │
  │        ▼
  │   Demo.Gateway（:5000）
  │   驗證 Token（用密鑰比對 Signature）
  │        │ 通過
  │        ▼
  │   Demo.DataService（:5128）
  │
  └── POST /auth/refresh （帶 Refresh Token）─────► Demo.AuthService
                                                     查 DB 確認 Refresh Token 有效
                                                     → 簽發新的 Access Token
```

> Gateway 只驗 Token（無狀態），AuthService 才簽發 Token（有狀態，需要 DB）。

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
