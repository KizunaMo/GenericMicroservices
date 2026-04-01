# Docker Compose 多環境設定

## 一、為什麼需要多環境？

原本一個 `docker-compose.yml` 把所有設定寫死：

```
docker-compose.yml（問題版）
┌─────────────────────────────────────────┐
│ gateway:                                │
│   ports: "5010:8080"   ← 開發才需要       
│   environment:                          │
│     ASPNETCORE_ENVIRONMENT=Development  │  ← 正式應該是 Production
│                                         │
│ postgres:                               │
│   ports: "5432:5432"   ← 正式絕對不能開   │
└─────────────────────────────────────────┘
```

每次換環境都要手動改檔案，容易出錯。

---

## 二、解法：檔案疊加（Override）

Docker Compose 支援多個 yml 檔案合併，概念和 `appsettings.json` 一樣：

```
appsettings.json              ←  基底
appsettings.Production.json   ←  環境覆蓋
```

```
docker-compose.yml            ←  基底（共用設定）
docker-compose.override.yml   ←  開發用（自動載入）
docker-compose.prod.yml       ←  正式用（手動指定）
docker-compose.staging.yml    ←  staging 用（手動指定）
```

---

## 三、檔案職責圖

```
┌─────────────────────────────────────────────────────────┐
│              docker-compose.yml（基底）                  │
│                                                         │
│  所有環境共用的設定：                                      │
│  - build（怎麼 build image）                             │
│  - image（用哪個 image）                                 │
│  - environment（應用程式參數，不含 ASPNETCORE_ENV）        │
│  - depends_on（服務啟動順序）                             │
│  - volumes（資料持久化）                                  │
│  - healthcheck（health 探測設定）                         │
│                                                         │
│  不放的東西：                                             │
│  - ports（各環境不同）                                    │
│  - restart（開發不需要）                                  │
│  - ASPNETCORE_ENVIRONMENT（各環境不同）                    │
└────────────────────┬────────────────────────────────────┘
                     │ 合併
       ┌─────────────┼──────────────────┐
       ▼             ▼                  ▼
┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│  override    │ │   prod       │ │  staging     │
│  .yml        │ │   .yml       │ │  .yml        │
│              │ │              │ │              │
│ 開發環境       │ │ 正式環境      │ │ 測試環境      │
│ - ports 全開  │ │ - 只開 80     │ │ - 少量 ports │
│ - Dev 環境    │ │ - Production │ │ - Staging 環 │
│ - 不 restart  │ │ - restart    │ │ - restart    │
└──────────────┘ └──────────────┘ └──────────────┘
  自動載入，        手動指定 -f      手動指定 -f
  不需打參數
```

---

## 四、合併規則

```
基底（docker-compose.yml）      覆蓋（override.yml）        最終結果
──────────────────────────      ────────────────────        ──────────────────────────
gateway:                        gateway:                    gateway:
  environment:                    ports:                      environment:
    - Jwt__Key=xxx                  - "5010:8080"  ──→          - Jwt__Key=xxx
                                  environment:                   - ASPNETCORE_ENV=Dev
                                    - ASPNETCORE_ENV=Dev       ports:
                                                                 - "5010:8080"

postgres:                       postgres:                   postgres:
  image: postgres:16              ports:                      image: postgres:16
  healthcheck: ...                  - "5432:5432"  ──→        healthcheck: ...
                                                              ports:
                                                                - "5432:5432"

gateway:                        gateway:                    gateway:
  environment:                    environment:                environment:
    - ASPNETCORE_ENV=Dev            - ASPNETCORE_ENV=Prod ──→   - ASPNETCORE_ENV=Prod
    （同名 key → 被覆蓋）
```

| 設定項目 | 合併行為 | 說明 |
|---------|---------|------|
| `ports` | 追加 | 兩個檔案的 port 都保留 |
| `environment` | 追加＋覆蓋 | 新 key 追加，同名 key 由後者覆蓋 |
| `volumes` | 追加 | 兩個檔案的 volume 都保留 |
| `image` / `build` | 覆蓋 | 後者完全取代前者 |
| `depends_on` | 追加 | 兩個檔案的依賴都保留 |
| `restart` | 覆蓋 | 後者取代前者 |

---

## 五、指令與流程圖

### 開發模式

```
你打指令                                        Docker 做的事
──────────────────────────────────────────────────────────────────
                                                ┌─────────────────────────────┐
make dev                                        │ 1. 讀 docker-compose.yml     │
（展開為 docker compose                          │    （共用基底設定）            │
  -f docker-compose.yml                         │ 2. 讀 docker-compose         │
  -f docker-compose.override.yml                │    .override.yml             │
  -f docker-compose.dev.yml                     │    （ASPNETCORE_ENV=Dev）     │
  up --build -d）                               │ 3. 讀 docker-compose.dev.yml │
                                                │    （dev 用 port 對外）       │
                                                │ 4. 合併三個檔案               │
                                                │ 5. build 所有 image          │
                                                │ 6. 啟動所有容器（背景執行）     │
                                                └─────────────────────────────┘

結果：
- gateway 5010、各服務 port（5100、5128、5129、5200）對外開放
- 基礎設施 ports（5432、9090、3000、16686、15672）對外開放
- ASPNETCORE_ENVIRONMENT = Development → Swagger 開啟
- 容器崩潰不會自動重啟
```

### 正式模式

```
你打指令                                        Docker 做的事
──────────────────────────────────────────────────────────────────
                                                ┌───────────────────────────┐
docker compose                                  │ 1. 讀 docker-compose.yml  │
  -f docker-compose.yml                         │ 2. 讀 docker-compose.prod │
  -f docker-compose.prod.yml                    │    .yml（手動指定）         │
  up --build -d                                 │ 3. 合併兩個檔案             │
                                                │ 4. build 所有 image        │
  ↑ -f 指定第一個檔案（基底）                       │ 5. 啟動所有容器             │
      -f 指定第二個檔案（覆蓋）                     │ 6. 背景執行（-d）           │
          up 啟動                                └───────────────────────────┘
             --build 重新 build
                    -d 背景執行，不佔 Terminal

結果：
- 只有 80 port 和 TCP 5400 對外
- postgres、rabbitmq、jaeger 等不對外
- ASPNETCORE_ENVIRONMENT = Production
- 容器崩潰自動重啟（restart: unless-stopped）
```

### Staging 模式

```
你打指令
──────────────────────────────────────────────────────────
docker compose
  -f docker-compose.yml
  -f docker-compose.staging.yml
  up --build -d

結果：
- 介於開發和正式之間的設定
- 可能開放少數 port 方便 QA 測試
- ASPNETCORE_ENVIRONMENT = Staging
```

### `--build` 要不要加？

```
第一次啟動 → 一定要加 --build（沒有 image 可用）

程式碼有改動 → 要加 --build（否則跑的是舊的 image）

只是重啟服務（程式碼沒變）→ 不用加，省時間
  docker compose up -d
```

---

## 六、用執行檔取代手打指令

### 為什麼需要執行檔？

正式環境指令很長，每次手打容易打錯：
```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d
```

用執行檔只要打一個短指令：

```
你打           實際執行的指令
─────────────────────────────────────────────────────────────────────
make dev       docker compose up --build
make prod      docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d
make staging   docker compose -f docker-compose.yml -f docker-compose.staging.yml up --build -d
make down      docker compose down
make clean     docker compose down -v（連 DB volume 一起刪）
make logs      docker compose logs -f
make log s=X   docker compose logs -f X（看特定服務）
```

### 跨平台方案：Makefile（Mac）+ PowerShell（Windows）

```
專案根目錄
├── Makefile          ← Mac / Linux 使用
└── compose.ps1       ← Windows 使用

使用方式：
  Mac：    make dev
  Windows：.\compose.ps1 dev
```

兩個檔案的指令名稱完全對應，換電腦只是改前綴。

### Makefile（Mac / Linux）

已建立於 Solution 根目錄，`Makefile`。

> **注意**：Makefile 縮排**必須用 Tab**，不能用空格，否則報錯。

### PowerShell script（Windows）

已建立於 Solution 根目錄，`compose.ps1`。

執行前需要先允許 PowerShell 執行 script（只需做一次）：
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

之後每次使用：
```powershell
.\compose.ps1 dev
.\compose.ps1 prod
.\compose.ps1 down
.\compose.ps1 logs
.\compose.ps1 log gateway   # 看特定服務
```

---

## 七、Staging 範例（擴展用）

Staging 是介於開發和正式之間的環境，通常用於：
- QA 測試（接近正式但可以測試）
- 上線前最後驗證

```yaml
# docker-compose.staging.yml
services:

  gateway:
    ports:
      - "8080:8080"      # 對外，但用 8080 和正式的 80 區分
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  auth-service:
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  data-service:
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  realtime-service:
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  grpc-service:
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  worker:
    environment:
      - ASPNETCORE_ENVIRONMENT=Staging
    restart: unless-stopped

  tcp-service:
    ports:
      - "5400:5400"
    restart: unless-stopped

  prometheus:
    ports:
      - "9090:9090"      # Staging 開放，讓 QA 可以看指標

  grafana:
    ports:
      - "3000:3000"
    restart: unless-stopped

  jaeger:
    ports:
      - "16686:16686"    # Staging 開放，讓 QA 可以查追蹤
    restart: unless-stopped

  postgres:
    # 不對外，和正式一樣
    restart: unless-stopped

  rabbitmq:
    restart: unless-stopped
```

三個環境的 Port 對外比較：

```
                  開發(override)   Staging       正式(prod)
                  ─────────────   ───────       ──────────
gateway           5010            8080          80
auth-service      5100 ✓          ✗             ✗   （開發可直接存取 Swagger）
data-service      5128 ✓          ✗             ✗   （開發可直接存取 Swagger）
data-service gRPC 5129 ✓          ✗             ✗
realtime-service  5200 ✓          ✗             ✗   （SignalR 開發測試）
postgres          5432 ✓          ✗             ✗
prometheus        9090 ✓          9090 ✓        ✗
grafana           3000 ✓          3000 ✓        3000 ✓
jaeger            16686 ✓         16686 ✓       ✗
rabbitmq mgmt     15672 ✓         ✗             ✗

✓ = 對外開放    ✗ = 不對外
```

---

## 八、常見問題

**Q：為什麼 override 不能改名成 develop？**
`override` 是 Docker 的保留名稱，只有這個名字才自動載入。改成其他名稱就和 prod.yml 一樣，都需要手動 `-f`。

**Q：Makefile 的 Tab 縮排如何確認？**
在 Rider 或 VS Code 打開 Makefile，把游標放在指令行開頭，狀態列應顯示 Tab 而非 Space。

**Q：`make` 指令不存在？**
macOS 預設有安裝。如果沒有：
```bash
xcode-select --install
```
