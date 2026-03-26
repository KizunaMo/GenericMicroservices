# Phase 2：Gateway 學習筆記

## 學習目標
建立統一入口點，讓所有外部請求先進 Gateway，再由 Gateway 轉發到對應的後端服務。

## 完成狀態：已完成

## 架構

```
Client
    ↓
Gateway (port 5000)       ← 唯一對外入口
    ↓ YARP 轉發
DataService (port 5128)   ← 不直接對外
```

## 使用工具：YARP

**YARP（Yet Another Reverse Proxy）**：Microsoft 官方的 .NET Reverse Proxy 套件。
- NuGet：`Yarp.ReverseProxy`
- 設定全部在 `appsettings.json`，不需要寫路由程式碼

## 關鍵設定（appsettings.json）

```json
"ReverseProxy": {
  "Routes": {
    "data-service-route": {
      "ClusterId": "data-service-cluster",
      "Match": { "Path": "/api/{**remainder}" }
    }
  },
  "Clusters": {
    "data-service-cluster": {
      "Destinations": {
        "destination1": { "Address": "http://localhost:5128" }
      }
    }
  }
}
```

- **Routes**：定義「哪些請求要轉發」，用 Path Pattern 比對
- **Clusters**：定義「轉發到哪個服務」，可以有多個 destination（負載平衡）
- **ClusterId**：把 Route 和 Cluster 連結起來

## 新增服務的方式

之後加 RealTimeService，只需要新增一組 Route + Cluster：

```json
"Routes": {
  "data-service-route": { ... },
  "realtime-service-route": {
    "ClusterId": "realtime-cluster",
    "Match": { "Path": "/hub/{**remainder}" }
  }
},
"Clusters": {
  "data-service-cluster": { ... },
  "realtime-cluster": {
    "Destinations": {
      "destination1": { "Address": "http://localhost:5200" }
    }
  }
}
```

## Gateway 的其他能力（之後階段）

| 功能 | 說明 |
|---|---|
| 負載平衡 | Cluster 加多個 destination，自動分流 |
| 認證集中 | 所有請求在 Gateway 驗證，後面服務不用各自處理 |
| Rate Limiting | 限制流量 |

## 疑惑與解答

### Q：為什麼服務名稱加 Demo 前綴？
因為這是學習專案，沒有實際業務名稱。真實專案的命名規則是 `{專案名稱}.{服務職責}`：
```
ECommerce.Gateway / ECommerce.OrderService / ECommerce.UserService
iBMS.Gateway / iBMS.DataService / iBMS.RealTimeService
```

### Q：Gateway 是獨立專案還是這個專案的一部分？
**它們是獨立的可執行程式，`.sln` 只是開發時的容器。**

```
開發時（Rider 裡看到的）        實際運作
GenericMicroservices.sln
├── GenericMicroservices/   →   DataService.exe（獨立程式，port 5128）
└── Demo.Gateway/           →   Gateway.exe（獨立程式，port 5000）
```

`.sln` 的作用是讓你在 Rider 裡同時開啟多個專案方便開發，不影響實際運作。
部署時每個服務是獨立的程式，各自運行在自己的 Server 或 Docker Container 裡。

### Q：YARP 的 Destination 是什麼意思？
Destination = 請求最終要送到的目標位址。同一個 Cluster 可以有多個 Destination：

```json
"Destinations": {
  "destination1": { "Address": "http://localhost:5128" },
  "destination2": { "Address": "http://localhost:5129" },
  "destination3": { "Address": "http://localhost:5130" }
}
```

YARP 會自動把流量分散到多個 Destination，這就是**負載平衡**。

---

## Port 管理（重要）

### 開發環境：launchSettings.json

```json
// 只在開發時有效，production 不讀這個檔案
"applicationUrl": "http://localhost:5000"   // Gateway
"applicationUrl": "http://localhost:5128"   // DataService
```

### 正式環境：環境變數

部署時用 `ASPNETCORE_URLS` 環境變數覆蓋 port，不改程式碼：

```bash
ASPNETCORE_URLS=http://+:80   dotnet Demo.Gateway.dll
ASPNETCORE_URLS=http://+:8001 dotnet GenericMicroservices.dll
```

### Docker 環境（Phase 5）

```yaml
# docker-compose.yml
services:
  gateway:
    ports:
      - "80:8080"      # 外部 80 → 容器內 8080，對外公開

  data-service:
    ports:
      - "8001:8080"    # 只內部使用，外部無法直接連
```

appsettings.json 裡的位址也會從 localhost 換成 Docker 服務名稱：

```json
// 開發時
"Address": "http://localhost:5128"

// Docker 環境（Docker 內建 DNS 自動解析服務名稱）
"Address": "http://data-service:8080"
```

### 三個環境的比較

| 環境 | 設定方式 | 位置 |
|---|---|---|
| 開發 | `launchSettings.json` | 各專案 Properties/ |
| 測試/正式 | 環境變數 `ASPNETCORE_URLS` | CI/CD 或 Server |
| Docker | `docker-compose.yml` port mapping | 部署設定檔 |

**核心原則**：程式碼裡不寫死 port，由環境決定。同一份程式碼在開發、測試、正式環境都能跑，只改設定不改程式碼。

---
