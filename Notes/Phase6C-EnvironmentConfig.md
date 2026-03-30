# Phase 6-C：環境設定（開發 / 測試 / 正式）

## 概念說明

一個系統通常有三套環境，設定各自不同：

| 環境 | ASPNETCORE_ENVIRONMENT | 特徵 |
|------|----------------------|------|
| Development | `Development` | 本機開發，User Secrets，Swagger 開啟，詳細錯誤 |
| Staging | `Staging` | 測試伺服器，像正式但用測試資料 |
| Production | `Production` | 正式上線，Docker Compose，Swagger 關閉，環境變數注入 secrets |

### .NET 的環境設定載入機制

```
appsettings.json                    ← 所有環境都載入（基底）
appsettings.{Environment}.json      ← 只有對應環境載入，同 Key 覆蓋基底
環境變數                             ← 優先權最高，覆蓋所有設定檔
User Secrets                        ← 只在 Development 有效
```

範例：`ASPNETCORE_ENVIRONMENT=Production` 時

```
載入 appsettings.json
載入 appsettings.Production.json（覆蓋 appsettings.json 裡相同的 Key）
套用環境變數（覆蓋上面所有）
```

### 各環境的 ASPNETCORE_ENVIRONMENT 值從哪來？

| 環境 | 來源 |
|------|------|
| 本機 dotnet run | `launchSettings.json` 的 `ASPNETCORE_ENVIRONMENT` |
| Docker Container | `.NET 8 預設 Production`（除非 docker-compose.yml 有設定） |
| 手動設定 | `export ASPNETCORE_ENVIRONMENT=Staging` |

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
