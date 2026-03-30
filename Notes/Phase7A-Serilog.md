# Phase 7-A：結構化日誌（Serilog）

## 概念說明

### 為什麼需要結構化日誌？

**純文字 log（.NET 預設）**：
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
info: Microsoft.AspNetCore.Hosting.Diagnostics[1]
      Request starting HTTP/1.1 POST http://localhost:5100/auth/login
```

人可以讀，但程式無法解析。出問題要在幾千行裡用眼睛找。

**結構化 log（Serilog）**：
```
[10:23:01 INF] Now listening on: http://[::]:8080
[10:23:02 INF] Request starting HTTP/1.1 POST http://localhost:5100/auth/login
[10:23:02 WRN] Login failed for user: admin
```

每行格式一致：`[時間 等級] 訊息`。可以直接餵給 Elasticsearch、Seq、Datadog 等工具做查詢和告警。

**結構化的真正意義**：不只是格式漂亮，而是 log 裡的欄位可以被程式解析：

```csharp
// 這樣寫
_logger.LogInformation("User {Username} logged in from {Ip}", username, ip);

// Serilog 輸出（JSON 模式）
{ "Username": "admin", "Ip": "192.168.1.10", "Message": "User admin logged in from 192.168.1.10" }
```

`{Username}` 不只是字串替換，而是獨立的欄位，可以用 `Username == "admin"` 查詢。

---

## 套件說明

| 套件 | 用途 |
|------|------|
| `Serilog.AspNetCore` | 主套件，整合 .NET 的 ILogger 介面 |
| `Serilog.Sinks.Console` | 輸出到 Console（Terminal / Docker logs）|
| `Serilog.Sinks.File` | 輸出到檔案（Phase 7 進階，目前未用）|

`Serilog.AspNetCore` 會自動帶入 `Serilog.Sinks.Console` 和 `Serilog.Sinks.File`，不需要另外安裝。

**Sink** 是「輸出目的地」的意思，一個 logger 可以同時輸出到多個 Sink。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.*/Program.cs` | 修改 | 加入 `UseSerilog` 接管所有 log 輸出 |
| `Demo.*/appsettings.json` | 修改 | 加入 Serilog 設定區塊（等級、格式、輸出目的地）|

---

## 實作步驟

### 步驟 1：安裝套件

```bash
# 在 Solution 根目錄執行，對每個服務安裝
dotnet add Demo.AuthService package Serilog.AspNetCore
dotnet add Demo.DataService package Serilog.AspNetCore
dotnet add Demo.Gateway package Serilog.AspNetCore
dotnet add Demo.RealTime package Serilog.AspNetCore
dotnet add Demo.GrpcService package Serilog.AspNetCore
dotnet add Demo.Worker package Serilog.AspNetCore
```

---

### 步驟 2：Program.cs 加入 UseSerilog

**適用於 WebApplication（Gateway、AuthService、DataService、RealTime、GrpcService）**

```csharp
using Serilog;   // 加在最上面的 using

var builder = WebApplication.CreateBuilder(args);

// 加在 CreateBuilder 之後，其他 builder.Services 之前
builder.Host.UseSerilog((context, config) =>
    config.ReadFrom.Configuration(context.Configuration));
```

`UseSerilog` 做了兩件事：
1. 接管 .NET 內建的 `ILogger`，之後所有服務注入 `ILogger<T>` 都會走 Serilog
2. 從 `context.Configuration`（即 appsettings.json）讀取 Serilog 設定

**Worker 服務用法不同**（Worker 用 `Host.CreateApplicationBuilder`，不是 `WebApplication`）：

```csharp
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// Worker 用 AddSerilog，不是 UseSerilog
builder.Services.AddSerilog((services, config) =>
    config.ReadFrom.Configuration(builder.Configuration));
```

**為什麼 Worker 不同**：Worker 是純背景服務，沒有 HTTP，用的是 `Host` 而非 `WebApplication`，API 稍有差異。

---

### 步驟 3：appsettings.json 加入 Serilog 區塊

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext" ]
  }
}
```

**逐段說明**：

`MinimumLevel.Default`：預設最低記錄等級，低於這個等級的 log 不輸出。

`MinimumLevel.Override`：針對特定命名空間覆蓋等級。
- `Microsoft.AspNetCore: Warning`：框架本身的 log 太多，只顯示警告以上
- `Microsoft.EntityFrameworkCore: Warning`：EF Core 每次 SQL 查詢都會 log，正常情況不需要看

`WriteTo`：輸出目的地清單（可以同時多個）。

`outputTemplate`：Console 輸出的格式：
```
[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}
 └─時間 HH:mm:ss  └─等級縮3字    └─訊息          └─換行  └─例外
```

`{Level:u3}` = 等級縮寫，大寫 3 個字：`INF`、`WRN`、`ERR`、`DBG`

`Enrich: ["FromLogContext"]`：允許在程式碼裡用 `LogContext.PushProperty` 動態加欄位到 log。

---

## Log 等級說明

| 等級 | 縮寫 | 用途 |
|------|------|------|
| Verbose | VRB | 最詳細，開發除錯用（通常不開）|
| Debug | DBG | 開發時的除錯資訊 |
| Information | INF | 正常運作的重要事件（服務啟動、請求進來）|
| Warning | WRN | 不預期但不影響運作（retry 成功、設定缺少）|
| Error | ERR | 發生錯誤，需要處理 |
| Fatal | FTL | 嚴重錯誤，服務無法繼續運作 |

**實際設定建議**：
- Development：`Default: Debug`，看到更多細節
- Production：`Default: Information`，減少噪音

---

## 在程式碼裡寫 Log

Serilog 接管 `ILogger` 之後，程式碼寫法不變：

```csharp
// 注入（和原本一樣，不需要改）
app.MapPost("/auth/login", async (LoginRequest req, AuthDbContext db, ILogger<Program> logger) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username);

    if (user is null)
    {
        // 結構化參數：{Username} 會成為可查詢的欄位
        logger.LogWarning("Login failed: user {Username} not found", req.Username);
        return Results.Unauthorized();
    }

    logger.LogInformation("User {Username} logged in successfully", req.Username);
    return Results.Ok(...);
});
```

**`{}` 佔位符的重要性**：
```csharp
// ❌ 字串串接：Username 只是文字，無法查詢
logger.LogInformation("User " + username + " logged in");

// ✅ 結構化參數：Username 是獨立欄位，可以查詢、過濾
logger.LogInformation("User {Username} logged in", username);
```

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| **Sink** | 日誌輸出目的地，例如 Console、File、Elasticsearch |
| **Enrich** | 為每條 log 自動附加額外欄位（例如機器名稱、環境名稱）|
| **outputTemplate** | Console Sink 的輸出格式模板 |
| **MinimumLevel.Override** | 針對特定命名空間設定不同的最低等級，避免框架 log 太多 |
| **結構化參數** | `{Username}` 這類佔位符，讓 log 欄位可以獨立查詢，不只是字串 |

---

## 驗證方式

```bash
docker compose up --build auth-service
```

預期看到格式從：
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
```

變成：
```
[06:46:58 INF] Now listening on: http://[::]:8080
[06:46:58 INF] Application started. Press Ctrl+C to shut down.
[06:46:58 WRN] Storing keys in a directory '/root/.aspnet/DataProtection-Keys'...
```

時間和等級在同一行，格式一致。
