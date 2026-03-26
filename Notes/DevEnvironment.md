# 開發環境管理筆記

## 一、確認哪些程式正在運行

### 用 Port 判斷（最直觀）

```bash
# 只看你的專案相關 port
lsof -iTCP -sTCP:LISTEN -P | grep -E "5128|5200|5000|5672|15672|5432"

# 看全部（含系統程序）
lsof -iTCP -sTCP:LISTEN -P
```

### 解讀輸出

```
COMMAND   PID   NAME
GenericMi 41510 localhost:5128  ← DataService（名稱被截斷，完整是 GenericMicroservices）
beam.smp  28094 localhost:5672  ← RabbitMQ AMQP
beam.smp  28094 *:15672         ← RabbitMQ Management UI
postgres  12239 localhost:5432  ← PostgreSQL
```

### 我們的 Port 對照表

| Port | 服務 | 說明 |
|---|---|---|
| 5128 | DataService（GenericMicroservices）| 主要 REST API |
| 5200 | Demo.RealTime | SignalR Hub |
| 5000 | Demo.Gateway | ⚠️ macOS ControlCenter 也用這個 port！ |
| 5432 | PostgreSQL | 資料庫 |
| 5672 | RabbitMQ AMQP | 訊息傳輸 |
| 15672 | RabbitMQ Management UI | 網頁監控介面 |
| Demo.Worker | **無 port** | 背景 Consumer，只連出去到 RabbitMQ，不監聽 |

---

## 二、Demo.Worker 看不到 Port 怎麼確認它在不在？

Worker 是 Background Service（消費者），只連到 RabbitMQ，不對外監聽任何 port。

**方法 1：用程序名稱查**
```bash
pgrep -a Demo.Worker
```

**方法 2：從 RabbitMQ Management UI 看**
打開 http://localhost:15672 → Connections tab：
- 看到 `Demo.Worker`（兩條）→ Worker 在跑
- 什麼都沒有 → Worker 沒跑

**方法 3：Rider 底部 Run 面板**
有 tab + 綠點 = 運行中，灰點 / 無 tab = 已停止

---

## 三、三個視角合在一起才完整

| 視角 | 能看到什麼 | 看不到什麼 |
|---|---|---|
| `lsof` Port 查詢 | 所有監聽 port 的服務 | Worker（無 port）|
| Rider Run 面板 | 所有 Rider 啟動的專案 | 非 Rider 啟動的程序 |
| RabbitMQ Connections | 有用 MassTransit 的服務 | Gateway、RealTime |

---

## 四、Port 衝突問題

### 為什麼會發生？

```
第一次 Run → 程序啟動，佔用 port
按 Stop   → Rider 可能只斷開 debug，程序繼續在背景跑
第二次 Run → 同一個 port 被搶 → 衝突 → crash
```

### 錯誤訊息長這樣

```
System.IO.IOException: Failed to bind to address http://127.0.0.1:5128: address already in use.
```

### 解決方式

```bash
# 清除特定 port（找出佔用的 PID 並 kill）
lsof -ti:5128 | xargs kill -9

# 一次清除所有 dotnet 程序
pkill -f dotnet
```

### 需要在程式碼處理嗎？

**不需要。** 這是基礎設施層的問題，應用程式看到 port 被佔應該直接 crash（fail fast），讓外部得知有問題。

| 環境 | 怎麼處理 |
|---|---|
| 本地開發 | 手動 kill，或確認 Rider Stop 完成後再 Run |
| Docker | 容器互相隔離，不會衝突 |
| Linux Server | `systemd` 管理程序生命週期 |
| Kubernetes | Pod 替換機制，舊停新才啟 |

---

## 五、Rider 同時運行多個專案

使用 **Compound Configuration**：

1. Run → Edit Configurations...
2. 左上 **+** → 選 **Compound**
3. 命名（例如 `All Services`）
4. 右側 **+** → 加入要同時跑的專案（DataService、Demo.Worker 等）
5. 選 `All Services` → Run

Rider 底部會出現**各自獨立的 tab**，每個 tab 顯示各自的 log。

---

## 六、已知的 Port 衝突問題

### macOS ControlCenter 佔用 Port 5000

macOS 系統的 ControlCenter 程序預設使用 port 5000，和 Demo.Gateway 衝突。

**解法（擇一）**：
- 把 Gateway 改到 port 5001（修改 launchSettings.json）
- 或每次啟動 Gateway 前先停用 ControlCenter（不推薦）

```bash
# 確認 5000 是誰在用
lsof -i:5000
```
