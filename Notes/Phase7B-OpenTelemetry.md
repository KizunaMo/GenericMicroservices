# Phase 7-B：分散式追蹤（OpenTelemetry + Jaeger）

## 概念說明

### 問題：多服務的請求怎麼追蹤？

假設使用者呼叫 `POST /api/items`，實際流程是：

```
Client → Gateway → DataService → PostgreSQL
                               → RabbitMQ → Worker
```

如果這個請求花了 2 秒，是哪個環節慢？光看每個服務的 log，你只知道「各自花了多久」，但不知道「整條鏈哪裡卡」。

**分散式追蹤**讓你可以：
1. 用一個 **TraceId** 把整條請求鏈串起來
2. 看到每個服務處理了多久（Span）
3. 在視覺化介面（Jaeger）裡看到整條流程的瀑布圖

---

### 核心概念

```
TraceId：整條請求鏈的唯一識別（Client 到最後一個服務）
└── Span：某個服務處理這條請求的一段時間
    └── Span：該服務內的子操作（例如 SQL 查詢）
```

**例子**：
```
TraceId: abc123
├── Gateway（5ms）      ← 驗 Token + 轉發
│   └── forward to DataService
└── DataService（120ms）
    ├── HTTP handler（5ms）
    ├── EF Core SQL（100ms）  ← 這裡最慢！
    └── publish to RabbitMQ（15ms）
```

Jaeger 會用瀑布圖顯示這個結構，讓你一眼看出瓶頸在 SQL。

---

### OpenTelemetry 是什麼？

**OpenTelemetry（OTel）** 是業界標準的可觀測性框架，統一了三種資料：

| 資料種類 | 說明 | 工具 |
|----------|------|------|
| **Traces**（追蹤） | 請求在各服務的流程與耗時 | Jaeger、Zipkin |
| **Metrics**（指標） | 數值型監控（請求數、延遲）| Prometheus（Phase 7-C）|
| **Logs**（日誌） | 文字記錄 | Serilog（Phase 7-A）|

OTel 負責**收集**資料，Jaeger 負責**儲存與顯示**。

---

### OTLP 是什麼？

**OTLP（OpenTelemetry Protocol）** 是 OTel 定義的資料傳輸格式。

```
你的服務 → [OTLP gRPC, port 4317] → Jaeger
```

.NET 服務用 `AddOtlpExporter` 把 Trace 資料送到 Jaeger 的 4317 port。

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.Gateway/Program.cs` | 修改 | 加入 OpenTelemetry，追蹤進來的請求和 YARP 轉發 |
| `Demo.AuthService/Program.cs` | 修改 | 加入 OpenTelemetry，追蹤 auth 請求 |
| `Demo.DataService/Program.cs` | 修改 | 加入 OpenTelemetry，含 EF Core SQL 追蹤 |
| `Demo.RealTime/Program.cs` | 修改 | 加入 OpenTelemetry，追蹤 WebSocket upgrade 請求 |
| `Demo.GrpcService/Program.cs` | 修改 | 加入 OpenTelemetry，追蹤 gRPC 請求和對 DataService 的呼叫 |
| `Demo.Gateway/appsettings.json` | 修改 | 加入 OTLP endpoint（本機開發用）|
| `Demo.AuthService/appsettings.json` | 修改 | 加入 OTLP endpoint |
| `Demo.DataService/appsettings.json` | 修改 | 加入 OTLP endpoint |
| `Demo.RealTime/appsettings.json` | 修改 | 加入 OTLP endpoint |
| `Demo.GrpcService/appsettings.json` | 修改 | 加入 OTLP endpoint |
| `docker-compose.yml` | 修改 | 加入 Jaeger 容器 + 所有服務的 OTLP 環境變數 |

**哪些服務加 OTel、哪些不加**：

| 服務 | 加 OTel | 原因 |
|------|---------|------|
| Gateway | ✅ | 所有請求入口，追蹤最完整 |
| AuthService | ✅ | HTTP 服務 |
| DataService | ✅ | HTTP 服務 + EF Core SQL |
| RealTime | ✅ | HTTP 服務（WebSocket 也是從 HTTP upgrade）|
| GrpcService | ✅ | gRPC 底層也是 HTTP/2，AspNetCore instrumentation 可追蹤 |
| Worker | ❌ | 沒有 HTTP，純背景服務消費 RabbitMQ，需要 MassTransit OTel 整合（不在本 Phase）|
| TcpService | ❌ | Raw TCP，沒有標準 OTel instrumentation，需要手動埋點 |

---

## 套件說明

| 套件 | 用途 |
|------|------|
| `OpenTelemetry.Extensions.Hosting` | 整合 .NET DI 系統 |
| `OpenTelemetry.Instrumentation.AspNetCore` | 自動追蹤 HTTP 請求（進來的）|
| `OpenTelemetry.Instrumentation.Http` | 自動追蹤 HttpClient 呼叫（出去的）|
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | 用 OTLP 格式把資料送到 Jaeger |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | 追蹤 EF Core SQL 查詢（DataService 專用）|

安裝指令（在 Solution 根目錄執行）：

```bash
# 以下五個服務都需要這四個基本套件
for svc in Demo.Gateway Demo.AuthService Demo.DataService Demo.RealTime Demo.GrpcService; do
  dotnet add $svc package OpenTelemetry.Extensions.Hosting
  dotnet add $svc package OpenTelemetry.Instrumentation.AspNetCore
  dotnet add $svc package OpenTelemetry.Instrumentation.Http
  dotnet add $svc package OpenTelemetry.Exporter.OpenTelemetryProtocol
done

# DataService 額外加 EF Core（目前只有 prerelease 版本，需要加 --prerelease）
dotnet add Demo.DataService package OpenTelemetry.Instrumentation.EntityFrameworkCore --prerelease
```

---

## 實作步驟

### 步驟 1：docker-compose.yml 加入 Jaeger

**目的**：Jaeger 是接收和顯示 trace 的工具，需要在 Docker 中跑起來

**修改檔案**：`docker-compose.yml`

```yaml
jaeger:
  image: jaegertracing/all-in-one:latest
  ports:
    - "16686:16686"   # Jaeger UI（瀏覽器打開看瀑布圖）
    - "4317:4317"     # OTLP gRPC（.NET 服務送追蹤資料到這裡）
```

**為什麼用 `all-in-one`**：開發環境把 Jaeger 所有元件（收集、儲存、UI）打包在一個容器裡，方便使用。正式環境會拆分開來。

---

### 步驟 2：各服務加入 OTLP Endpoint 設定

**目的**：讓 OTLP 目標地址可以設定，本機和 Docker 用不同地址

**修改檔案**：`Demo.Gateway/appsettings.json`（AuthService、DataService 相同）

```json
{
  "Otlp": {
    "Endpoint": "http://localhost:4317"   // 本機直接連 Jaeger port 4317
  }
}
```

**修改檔案**：`docker-compose.yml` — 各服務的 environment 加入：

```yaml
environment:
  - Otlp__Endpoint=http://jaeger:4317   # Docker 內部用服務名稱 jaeger，不用 localhost
```

**為什麼不寫死 `jaeger:4317`**：
- 本機跑（沒有 Docker）：Jaeger 不存在，連線會失敗（不影響功能，只是 trace 送不出去）
- 本機跑（Jaeger 在 Docker 但服務不在）：用 `localhost:4317`，透過 Docker port mapping 連進去
- Docker 全跑：用 `jaeger:4317`，Docker 內部網路直連

透過設定檔分離，本機開發和 Docker 各自用正確的地址。

---

### 步驟 3：Program.cs 加入 OpenTelemetry

**目的**：讓服務自動追蹤請求，並把資料送到 Jaeger

**修改檔案**：`Demo.Gateway/Program.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// 加在 builder.Services.AddReverseProxy() 之前
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.Gateway"))   // Jaeger 裡顯示的服務名稱
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 自動追蹤進來的 HTTP 請求
        .AddHttpClientInstrumentation()   // 自動追蹤對下游服務的 HTTP 呼叫（YARP 轉發）
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));
```

**修改檔案**：`Demo.AuthService/Program.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// 加在 builder.Services.AddHealthChecks() 之後
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.AuthService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤進來的 HTTP 請求（login、refresh 等）
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));
```

**修改檔案**：`Demo.DataService/Program.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// 加在 builder.Services.AddHealthChecks() 之後
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.DataService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddEntityFrameworkCoreInstrumentation()   // 追蹤 EF Core 的 SQL 查詢（DataService 獨有）
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));
```

**修改檔案**：`Demo.RealTime/Program.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// 加在 builder.Services.AddHealthChecks() 之後
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.RealTime"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤 WebSocket upgrade 請求（SignalR 連線也是從 HTTP 開始）
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));
```

**修改檔案**：`Demo.GrpcService/Program.cs`

```csharp
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

// 加在 builder.Services.AddHealthChecks() 之後
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("Demo.GrpcService"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()   // 追蹤進來的 gRPC 請求（gRPC 底層是 HTTP/2）
        .AddHttpClientInstrumentation()   // 追蹤對 DataService gRPC 的呼叫
        .AddOtlpExporter(o => o.Endpoint = new Uri(
            builder.Configuration["Otlp:Endpoint"] ?? "http://localhost:4317")));
```

**逐段說明**：

`.ConfigureResource(r => r.AddService("Demo.Gateway"))`
- 設定「這個 Trace 是哪個服務產生的」
- 在 Jaeger UI 裡，你可以用服務名稱過濾

`.AddAspNetCoreInstrumentation()`
- 自動幫每個進來的 HTTP 請求建立一個 Span
- 你不需要在每個 endpoint 裡手動寫追蹤程式碼

`.AddHttpClientInstrumentation()`
- 自動幫 HttpClient 的每個呼叫建立子 Span
- YARP 的轉發也會被追蹤到（因為 YARP 底層用 HttpClient）

`.AddEntityFrameworkCoreInstrumentation()`
- 自動幫 EF Core 的每個 SQL 查詢建立子 Span
- 可以在 Jaeger 裡看到具體的 SQL 語句和耗時

`.AddOtlpExporter(o => o.Endpoint = ...)`
- 把收集到的 trace 資料用 OTLP gRPC 送到 Jaeger
- Endpoint 從設定檔讀取，不寫死

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| **TraceId** | 一條請求從頭到尾的唯一 ID，跨越所有服務 |
| **Span** | 某個服務/操作處理這條請求的一段時間記錄 |
| **Instrumentation** | 自動埋點，讓框架（AspNetCore、EF Core）自動產生 Span |
| **Exporter** | 負責把收集到的資料傳出去（這裡是 OTLP 格式送到 Jaeger）|
| **OTLP** | OpenTelemetry Protocol，OTel 定義的資料傳輸格式 |
| **Jaeger** | 開源分散式追蹤系統，接收 OTLP 資料並提供視覺化 UI |
| **all-in-one** | Jaeger 的開發版打包，把收集、儲存、UI 合在一個容器 |

---

## 驗證方式

### 1. 啟動所有服務

```bash
docker compose up --build
```

### 2. 發送一個請求

```bash
# 先登入取得 token
curl -X POST http://localhost:5010/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'

# 用 token 打 /api/items（替換 YOUR_TOKEN）
curl http://localhost:5010/api/items \
  -H "Authorization: Bearer YOUR_TOKEN"
```

### 3. 打開 Jaeger UI

瀏覽器開啟 `http://localhost:16686`

- 左上角 **Service** 下拉選 `Demo.Gateway`
- 點 **Find Traces**
- 點進一條 Trace，看到瀑布圖：
  ```
  Demo.Gateway ─── 5ms
  └── Demo.DataService ─── 120ms
      ├── GET /api/items ─── 5ms
      └── SELECT * FROM Items ─── 100ms （EF Core SQL）
  ```

### 4. 預期結果

- Trace 串跨 Gateway 和 DataService（同一個 TraceId）
- DataService 的 Span 包含 EF Core SQL 查詢的子 Span
- 可以清楚看到哪個環節最耗時
