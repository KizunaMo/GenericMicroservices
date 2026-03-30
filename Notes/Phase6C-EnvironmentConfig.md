# Phase 6-C：環境設定（開發 / 測試 / 正式）

## 概念說明

一個系統通常有三套環境，設定各自不同：

| 環境 | ASPNETCORE_ENVIRONMENT | 特徵 |
|------|----------------------|------|
| Development | `Development` | 本機開發，User Secrets，Swagger 開啟，詳細錯誤 |
| Staging | `Staging` | 測試伺服器，像正式但用測試資料 |
| Production | `Production` | 正式上線，Docker Compose，Swagger 關閉，環境變數注入 secrets |

---

## ASPNETCORE_ENVIRONMENT 是什麼？怎麼設定？

`ASPNETCORE_ENVIRONMENT` 是一個**作業系統環境變數**，.NET 啟動時會去讀取它，
用這個值決定「現在是哪個環境」。

### 它的值從哪裡來？

**情況 1：本機用 `dotnet run` 啟動**

值來自 `launchSettings.json`，這個檔案放在專案的 `Properties/` 目錄下：

```json
// Properties/launchSettings.json
{
  "profiles": {
    "Demo.DataService": {
      "commandName": "Project",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"   // ← 這裡設定
      }
    }
  }
}
```

Rider 或 VS 的「Run」按鈕也是讀這個檔案。所以本機跑永遠是 Development。

**情況 2：Docker Container**

.NET 8 容器的內建預設值是 `Production`。
也可以在 docker-compose.yml 手動指定：

```yaml
data-service:
  environment:
    - ASPNETCORE_ENVIRONMENT=Production   # 明確指定（其實不寫也一樣）
    # 如果要跑 Staging：
    # - ASPNETCORE_ENVIRONMENT=Staging
```

**情況 3：Terminal 手動設定（臨時）**

```bash
# macOS / Linux（只對目前這個 Terminal 視窗有效）
export ASPNETCORE_ENVIRONMENT=Staging
dotnet run

# Windows
set ASPNETCORE_ENVIRONMENT=Staging
dotnet run
```

---

## .NET 怎麼知道要讀哪個設定檔？

`WebApplication.CreateBuilder(args)` 這行程式碼呼叫時，.NET 在背後自動做了這些事：

```
步驟 1：讀取 ASPNETCORE_ENVIRONMENT 的值
        → 例如讀到 "Production"

步驟 2：載入 appsettings.json（基底，永遠載入）

步驟 3：根據環境名稱，載入 appsettings.{環境}.json
        → ASPNETCORE_ENVIRONMENT=Production
        → 自動載入 appsettings.Production.json
        → 同名的 Key 覆蓋掉 appsettings.json 的值

步驟 4：載入環境變數（ASPNETCORE_ 開頭或 __ 分隔的）
        → 優先權最高，覆蓋上面所有

步驟 5：載入 User Secrets（只有 Development 環境）
        → 只在開發機有效
```

這些全部由 `CreateBuilder` 自動完成，**不需要寫任何程式碼**。

### 覆蓋規則：同名 Key 後者蓋前者

```
appsettings.json 有：
  "RabbitMq": { "Host": "localhost" }

appsettings.Production.json 有：
  "RabbitMq": { "Host": "rabbitmq" }

最終結果（Production 環境）：
  "RabbitMq": { "Host": "rabbitmq" }   ← Production.json 贏了

appsettings.Production.json 沒有的 Key：
  "Logging": { ... }                   ← 繼續用 appsettings.json 的值
```

---

## 完整流程圖

```
啟動 dotnet run / docker 容器
         ↓
讀取 ASPNETCORE_ENVIRONMENT
         ↓
    ┌────┴────────────────────────┐
    │  Development                │  Production
    │  (本機 launchSettings.json) │  (Docker 預設)
    └────────────┬────────────────┘
                 ↓
         載入 appsettings.json
                 ↓
         載入 appsettings.{環境}.json
         （Development / Production / Staging）
                 ↓
         套用環境變數（最高優先）
                 ↓
    ┌────┴────────────────────────┐
    │  Development only           │
    │  載入 User Secrets          │
    └─────────────────────────────┘
                 ↓
         程式開始執行
```

---

## 實際對應本專案

### 本機啟動 DataService（Development）

```
ASPNETCORE_ENVIRONMENT = "Development"（來自 launchSettings.json）

載入 appsettings.json：
  ConnectionStrings.DefaultConnection = "Host=localhost;..."
  RabbitMq.Host = "localhost"

載入 appsettings.Development.json：
  只有 Logging，沒有新增/覆蓋任何設定

載入 User Secrets：
  （DataService 目前沒有 User Secrets，跳過）

最終結果：
  DB → localhost
  RabbitMQ → localhost
  Swagger → 開啟（IsDevelopment() = true）
```

### Docker 啟動 DataService（Production）

```
ASPNETCORE_ENVIRONMENT = "Production"（.NET 8 容器預設）

載入 appsettings.json：
  ConnectionStrings.DefaultConnection = "Host=localhost;..."  ← 先載入
  RabbitMq.Host = "localhost"                                ← 先載入

appsettings.Production.json：
  DataService 沒有這個檔案，跳過

載入環境變數（來自 docker-compose.yml）：
  ConnectionStrings__DefaultConnection = "Host=postgres;..."  ← 覆蓋
  RabbitMq__Host = "rabbitmq"                                ← 覆蓋

最終結果：
  DB → postgres（Docker 服務名稱）
  RabbitMQ → rabbitmq（Docker 服務名稱）
  Swagger → 關閉（IsDevelopment() = false）
```

### Docker 啟動 Gateway（Production）

```
載入 appsettings.json：
  YARP Clusters → localhost:5100、localhost:5128...  ← 先載入

載入 appsettings.Production.json：
  YARP Clusters → auth-service:8080、data-service:5128...  ← 覆蓋

最終結果：
  路由目標 → Docker 服務名稱
```

Gateway 用 Production.json 而不是環境變數的原因：
YARP 路由設定是巢狀 JSON，環境變數要這樣寫才能覆蓋：
```
ReverseProxy__Clusters__data-service-cluster__Destinations__destination1__Address=http://...
```
太長太醜，用 Production.json 直接覆蓋整個區塊清楚得多。

---

## 本專案的環境設定架構

```
各服務/
├── appsettings.json              ← 開發預設值（本機 localhost、DB 連線字串）
├── appsettings.Development.json  ← 開發專屬覆蓋（目前只有 Logging，大多為空）
└── appsettings.Production.json   ← 正式環境覆蓋（Docker 服務名稱）
```

**Gateway 特別需要 Production.json** 因為 YARP 路由設定巢狀較深，
用環境變數一個一個覆蓋很繁瑣，直接用 Production.json 覆蓋整個 Clusters 區塊較清楚。

**其他服務的正式環境值** 透過 docker-compose.yml 的 `environment:` 注入，
不需要 Production.json：

```yaml
auth-service:
  environment:
    - ConnectionStrings__AuthDb=Host=postgres;...   # 覆蓋 appsettings.json
    - Jwt__SecretKey=...                            # 覆蓋 appsettings.json
```

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.DataService/Program.cs` | 修改 | Swagger 改為只在 Development 開啟 |
| `Demo.DataService/appsettings.json` | 修改 | 補上 RabbitMq:Host 開發預設值 |
| `Demo.Worker/appsettings.json` | 修改 | 補上 RabbitMq:Host 開發預設值 |

---

## 實作步驟

### 步驟 1：Swagger 只在 Development 開啟

**目的**：正式環境不暴露 API 文件，避免讓外人知道 API 端點結構

**修改檔案**：`Demo.DataService/Program.cs`

```csharp
// 修改前：無條件開啟
app.UseSwagger();
app.UseSwaggerUI();

// 修改後：只在 Development 開啟
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

`app.Environment.IsDevelopment()` 等同於判斷 `ASPNETCORE_ENVIRONMENT == "Development"`。

**為什麼正式環境要關閉 Swagger**：
- Swagger UI 會列出所有 API 端點、參數格式、回應結構
- 攻擊者可以用這份文件快速了解系統，找到攻擊點
- 正式環境的 API 文件應該存在內部 Wiki，而非對外暴露

---

### 步驟 2：把設定值從程式碼移到 appsettings.json

**目的**：設定值應該在設定檔，不應該靠程式碼的 fallback 來補

**修改檔案**：`Demo.DataService/appsettings.json`

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=demo_db;Username=yuweilyutcit;Password="
  },
  "RabbitMq": {
    "Host": "localhost"
  },
  "Logging": { ... }
}
```

**修改檔案**：`Demo.Worker/appsettings.json`

```json
{
  "RabbitMq": {
    "Host": "localhost"
  },
  "Logging": { ... }
}
```

**為什麼要這樣改**：

程式碼裡寫 `?? "localhost"` 是「最後防線」，不是正確的設定來源。
設定值應該集中在 appsettings.json，讓人一眼就知道這個服務的預設行為，
不用去翻 Program.cs 才知道「喔原來預設連 localhost」。

---

## 各環境設定對照表（本專案）

| 設定項目 | Development（本機）| Production（Docker）|
|----------|-------------------|-------------------|
| Gateway YARP 路由 | appsettings.json（localhost:5100 等）| appsettings.Production.json（服務名稱）|
| DB 連線字串 | appsettings.json（localhost）| docker-compose.yml 環境變數（postgres）|
| JWT SecretKey | User Secrets | docker-compose.yml 環境變數 |
| RabbitMQ Host | appsettings.json（localhost）| docker-compose.yml 環境變數（rabbitmq）|
| Swagger | 開啟（IsDevelopment）| 關閉 |
| 詳細錯誤訊息 | 開啟（.NET 預設）| 關閉（.NET 預設）|

---

## 驗證方式

### 驗證 Swagger 在 Development 開啟

```bash
# 本機執行（Development 環境）
cd Demo.DataService
dotnet run

# 瀏覽器開啟
http://localhost:5128/swagger    # 應該看到 Swagger UI
```

### 驗證 Swagger 在 Production 關閉

```bash
docker compose up --build

# 瀏覽器開啟（Docker 是 Production 環境）
http://localhost:5128/swagger    # DataService 沒有對外 Port，無法直接連
# 透過 Gateway 也不行，因為 Gateway 沒有 /swagger 路由
```

Docker 環境下 Swagger 被關閉，就算嘗試連也會 404。

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| `IsDevelopment()` | 判斷目前是否為 Development 環境，等同於判斷 `ASPNETCORE_ENVIRONMENT == "Development"` |
| `IsProduction()` | 判斷目前是否為 Production 環境 |
| `IsStaging()` | 判斷目前是否為 Staging 環境 |
| `app.Environment` | 取得目前執行環境資訊的物件，在 Minimal API 的 `app.Build()` 之後可使用 |
