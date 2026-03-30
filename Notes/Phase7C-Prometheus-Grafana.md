# Phase 7-C：指標監控（Prometheus + Grafana）

## 概念說明

### 三種可觀測性工具的分工

| 工具 | 類型 | 問的問題 | 已完成 |
|------|------|---------|-------|
| Serilog | Log（日誌）| 發生了什麼事？ | Phase 7-A |
| OpenTelemetry + Jaeger | Trace（追蹤）| 一條請求怎麼走的、哪裡慢？ | Phase 7-B |
| Prometheus + Grafana | Metrics（指標）| 系統現在狀態如何？趨勢怎麼走？ | Phase 7-C |

三者互補，缺一不可：
- Log 告訴你「某個請求登入失敗」
- Trace 告訴你「這個請求在 AuthService 花了 300ms」
- Metrics 告訴你「過去 5 分鐘 AuthService 的失敗請求數突然從 0 飆到 500」

---

### Prometheus 是什麼？

**Prometheus** 是一個**時間序列資料庫**，專門儲存「時間 + 數值」格式的資料：

```
process_working_set_bytes{job="gateway"}  = 31457280   (at 07:00:00)
process_working_set_bytes{job="gateway"}  = 31522816   (at 07:00:15)
process_working_set_bytes{job="gateway"}  = 32112640   (at 07:00:30)
```

**關鍵特點：Pull 模式**

Prometheus 是主動去**拉取**（Pull）資料，而不是服務推送過來：

```
Prometheus → [每 15 秒] GET /metrics → 你的服務
```

這跟 OpenTelemetry（服務主動 Push 到 Jaeger）相反。好處是：服務不需要知道 Prometheus 在哪，只要暴露 `/metrics` endpoint 就好。

**PromQL（查詢語言）**

Prometheus 有自己的查詢語言 PromQL，可以做數學運算：

```promql
# 某個服務的記憶體用量
process_working_set_bytes{job="gateway"}

# 所有服務的記憶體總和
sum(process_working_set_bytes)

# 計算每秒請求數（對 Counter 類型的指標）
rate(http_requests_total[1m])
```

---

### Grafana 是什麼？

**Grafana** 是**視覺化儀表板**工具，本身不儲存資料，只是：
1. 連到 Prometheus（或其他資料來源）
2. 用 PromQL 查詢資料
3. 把數值畫成圖表

```
                Pull 模式架構（Prometheus 主動抓取）
                ─────────────────────────────────────

  你的服務（暴露 /metrics endpoint）
  ┌─────────────────────┐
  │  Demo.Gateway       │ :8080/metrics  ─┐
  │  Demo.AuthService   │ :8080/metrics  ─┤
  │  Demo.DataService   │ :5128/metrics  ─┼──► Prometheus :9090
  │  Demo.RealTime      │ :8080/metrics  ─┤    每 15 秒 GET /metrics 抓一次
  │  Demo.GrpcService   │ :8080/metrics  ─┘    儲存為時間序列資料
  └─────────────────────┘
         （你不需要知道 Prometheus 在哪，只要暴露 /metrics）

  Prometheus :9090 ◄──── PromQL 查詢 ──── Grafana :3000
                                                  ▲
                                     瀏覽器 http://localhost:3000
                                     （帳密：admin / admin）

  對比 OpenTelemetry（Push 模式）：
  Demo.Gateway → 主動推送 Trace → Jaeger :4317
```

**Grafana 能做的事**：
- 建立儀表板：把多個指標放在同一頁，一眼看清系統狀態
- 設定告警：記憶體超過閾值時發通知
- 探索資料：Drilldown → Metrics 可以瀏覽所有可用指標

---

### /metrics endpoint 長什麼樣？

你的服務加了 `app.UseMetricServer()` 之後，瀏覽器打開 `/metrics` 會看到：

```
# HELP process_working_set_bytes Process working set
# TYPE process_working_set_bytes gauge
process_working_set_bytes 31457280

# HELP dotnet_collection_count_total GC collection count
# TYPE dotnet_collection_count_total counter
dotnet_collection_count_total{generation="0"} 5
dotnet_collection_count_total{generation="1"} 2
dotnet_collection_count_total{generation="2"} 0

# HELP http_requests_in_progress The number of requests currently in progress
# TYPE http_requests_in_progress gauge
http_requests_in_progress{method="GET",controller=""} 0
```

格式說明：
- `# HELP`：這個指標的說明
- `# TYPE`：指標類型（gauge、counter、histogram）
- `{label="value"}`：標籤，讓同一個指標可以區分不同維度（method、controller 等）

---

### 指標類型

| 類型 | 說明 | 例子 |
|------|------|------|
| **Gauge** | 可上可下的瞬時值 | 記憶體用量、當前連線數 |
| **Counter** | 只增不減的累積值 | 總請求數、總錯誤數 |
| **Histogram** | 分佈統計（分桶）| 請求耗時分佈（有幾個在 0-10ms、10-50ms...）|

Counter 本身只是累積數字，要看「速率」需要用 PromQL 的 `rate()` 函數：
```promql
rate(http_requests_total[1m])   # 最近 1 分鐘的每秒請求數
```

---

## 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| `Demo.*/Program.cs`（5個服務）| 修改 | 加入 `app.UseMetricServer()` 暴露 `/metrics` |
| `Demo.*/Program.cs`（5個服務）| 修改 | 加入 `app.UseHttpMetrics()` 追蹤 HTTP 請求指標 |
| `prometheus.yml` | 新增 | 告訴 Prometheus 要去哪些服務抓資料 |
| `docker-compose.yml` | 修改 | 加入 Prometheus（port 9090）和 Grafana（port 3000）容器 |
| `Demo.GrpcService/appsettings.json` | 修改 | Kestrel 改為 Http1AndHttp2，讓 Prometheus 能用 HTTP/1.1 抓取 |

---

## 套件說明

| 套件 | 用途 |
|------|------|
| `prometheus-net.AspNetCore` | 暴露 `/metrics` endpoint，讓 Prometheus 來抓 |

安裝指令（在 Solution 根目錄執行）：

```bash
for svc in Demo.Gateway Demo.AuthService Demo.DataService Demo.RealTime Demo.GrpcService; do
  dotnet add $svc package prometheus-net.AspNetCore
done
```

---

## 實作步驟

### 步驟 1：各服務 Program.cs 加入 metrics

**目的**：暴露 `/metrics` endpoint，並追蹤 HTTP 請求指標

**修改檔案**：所有 5 個 HTTP 服務的 `Program.cs`

```csharp
using Prometheus;   // 加在 using 區塊

// app.Build() 之後，app.Run() 之前加入：
app.UseHttpMetrics();        // 追蹤每個 HTTP 請求的 method、status、duration
app.UseMetricServer();       // 暴露 /metrics endpoint 給 Prometheus 抓取
```

**為什麼需要兩行**：
- `UseMetricServer()`：只是把已收集的數值「暴露出去」，負責 `/metrics` endpoint
- `UseHttpMetrics()`：是 Middleware，攔截每個 HTTP 請求並記錄 method、status code、耗時等
- 沒有 `UseHttpMetrics()` 的話，只有 process 和 .NET runtime 的基礎指標，沒有 HTTP 請求相關的指標

**middleware 順序重要**：`UseHttpMetrics()` 要加在 `UseRouting()` 之後、endpoint mapping 之前。

---

### 步驟 2：GrpcService appsettings.json 修改

**目的**：GrpcService 預設只接受 HTTP/2（gRPC 需要），但 Prometheus 用 HTTP/1.1 抓 `/metrics`

**修改檔案**：`Demo.GrpcService/appsettings.json`

```json
"Kestrel": {
  "EndpointDefaults": {
    "Protocols": "Http1AndHttp2"   // 原本是 Http2，改為同時接受兩種
  }
}
```

**為什麼沒有影響 gRPC**：gRPC client 呼叫時會自動使用 HTTP/2，允許 HTTP/1.1 只是讓 Prometheus 的 scrape 請求不被拒絕。

---

### 步驟 3：建立 prometheus.yml

**目的**：告訴 Prometheus 要定時去哪些 URL 抓 `/metrics`

**新增檔案**：`prometheus.yml`（放在 Solution 根目錄）

```yaml
global:
  scrape_interval: 15s   # 每 15 秒抓一次

scrape_configs:
  - job_name: "gateway"
    static_configs:
      - targets: ["gateway:8080"]

  - job_name: "auth-service"
    static_configs:
      - targets: ["auth-service:8080"]

  - job_name: "data-service"
    static_configs:
      - targets: ["data-service:5128"]   # DataService 用自訂 port

  - job_name: "realtime-service"
    static_configs:
      - targets: ["realtime-service:8080"]

  - job_name: "grpc-service"
    static_configs:
      - targets: ["grpc-service:8080"]
```

**說明**：
- `job_name`：在 Prometheus 裡這組 target 的名稱，會成為 `job` label
- `targets`：用 Docker 服務名稱（不是 localhost），Prometheus 和服務在同一個 Docker 網路
- Prometheus 會自動在每個 target 後面加上 `/metrics` path

---

### 步驟 4：docker-compose.yml 加入 Prometheus 和 Grafana

**修改檔案**：`docker-compose.yml`

```yaml
prometheus:
  image: prom/prometheus:latest
  ports:
    - "9090:9090"      # Prometheus UI
  volumes:
    - ./prometheus.yml:/etc/prometheus/prometheus.yml   # 掛入設定檔

grafana:
  image: grafana/grafana:latest
  ports:
    - "3000:3000"      # Grafana UI（預設帳密：admin / admin）
  environment:
    - GF_SECURITY_ADMIN_PASSWORD=admin
  depends_on:
    - prometheus
```

**volumes 說明**：`./prometheus.yml:/etc/prometheus/prometheus.yml` 是把本機的 `prometheus.yml` 掛進容器內的指定路徑。Prometheus 啟動時會讀這個路徑的設定。

---

## 專有名詞

| 名詞 | 說明 |
|------|------|
| **時間序列資料庫** | 以時間為索引儲存數值的資料庫，適合趨勢分析 |
| **Pull 模式** | Prometheus 主動去服務拉資料，相對於服務主動推送（Push）|
| **Scrape** | Prometheus 去服務抓一次 `/metrics` 的動作 |
| **PromQL** | Prometheus Query Language，用來查詢和計算指標資料 |
| **Gauge** | 可上可下的瞬時值（記憶體、連線數）|
| **Counter** | 只增不減的累積計數（請求總數、錯誤總數）|
| **Histogram** | 分佈統計，把數值分成多個桶（bucket）計算落在各區間的數量 |
| **Label** | 指標的維度標籤，例如 `{job="gateway", method="GET"}` |
| **rate()** | PromQL 函數，把 Counter 轉換成每秒速率 |

---

## Prometheus UI 常用查詢

瀏覽器開 `http://localhost:9090`：

```promql
# 各服務的記憶體用量（Bytes）
process_working_set_bytes

# GC 垃圾回收次數
dotnet_collection_count_total

# 當前處理中的 HTTP 請求數（需要 UseHttpMetrics）
http_requests_in_progress

# 過去 1 分鐘每秒 HTTP 請求數（需要 UseHttpMetrics）
rate(http_requests_total[1m])

# 各服務 Target 健康狀態
# → Status > Target health（查看哪些服務 UP/DOWN）
```

---

## Grafana 使用流程

瀏覽器開 `http://localhost:3000`（帳密：admin / admin）

### 第一次設定資料來源

1. 左側 **Connections → Data sources**
2. **Add data source → Prometheus**
3. URL 填 `http://prometheus:9090`（Docker 內部名稱）
4. **Save & test**

### 瀏覽所有指標

左側 **Drilldown → Metrics** → 選資料來源 `prometheus` → 看到所有指標的縮圖

### 建立儀表板

1. 左側 **Dashboards → New Dashboard**
2. **Add visualization**
3. 選 Prometheus 資料來源
4. 輸入 PromQL 查詢
5. 選圖表類型（折線、長條、儀表盤）

---

## 驗證方式

### 1. 確認 /metrics 有資料

```bash
# 直接用 curl 抓 Gateway 的 metrics（在本機打 Docker port）
curl http://localhost:5010/metrics
```

預期看到一大堆文字，包含 `process_working_set_bytes`、`dotnet_collection_count_total` 等。

### 2. Prometheus Target 全部 UP

`http://localhost:9090` → Status → Target health

預期：auth-service、data-service、gateway、realtime-service、grpc-service 全部綠色 UP。

### 3. Prometheus 查詢有資料

查詢框輸入 `process_working_set_bytes`，切換到 Graph，能看到每個服務的折線。

### 4. Grafana 有圖

`http://localhost:3000` → Drilldown → Metrics，能看到指標縮圖。
