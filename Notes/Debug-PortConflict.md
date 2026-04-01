# Port 衝突除錯指南

## 問題描述

Docker Compose 啟動時出現：

```
Error response from daemon: ports are not available: exposing port TCP 0.0.0.0:5432 -> 127.0.0.1:0: listen tcp 0.0.0.0:5432: bind: address already in use
```

意思是：有其他 process 已經占用了這個 port，Docker 容器無法綁定。

---

## 釐清流程

```
Docker 啟動失敗（port 被占用）
        │
        ▼
┌─────────────────────────┐
│ lsof -i :PORT -n -P     │  ← 一般查詢
└─────────────────────────┘
        │
        ├─ 有結果 → 直接看是哪個 process，跳到「停止 process」
        │
        └─ 沒結果（lsof 看不到）
                │
                ▼
        ┌─────────────────────────────────┐
        │ sudo lsof -i :PORT              │  ← 加 sudo，看系統層級 process
        │ netstat -an | grep PORT         │  ← 另一種確認方式
        └─────────────────────────────────┘
                │
                ├─ 有結果 → 找到 PID，跳到「停止 process」
                │
                └─ 確定有 process 但不知道是誰
                        │
                        ▼
                ┌──────────────────────────────────┐
                │ sudo launchctl list | grep 服務名 │  ← 查 launchd 管理的服務
                └──────────────────────────────────┘
```

---

## 指令說明

### 1. 查詢 port 占用（一般）

```bash
lsof -i :5432 -n -P
```

| 參數 | 意思 |
|------|------|
| `-i :5432` | 過濾 port 5432 的網路連線 |
| `-n` | 不做 DNS 反查（速度更快） |
| `-P` | 顯示 port 號碼而非服務名稱 |

**沒有輸出 = 沒有 process 占用（但不一定正確，lsof 有時看不到系統層級的 process）**

---

### 2. 用 sudo 查詢（可看到系統層級 process）

```bash
sudo lsof -i :5432
```

輸出範例：
```
COMMAND  PID     USER   FD   TYPE  DEVICE SIZE/OFF NODE NAME
postgres 547  postgres   7u  IPv6  ...         0t0  TCP *:postgresql (LISTEN)
postgres 547  postgres   8u  IPv4  ...         0t0  TCP *:postgresql (LISTEN)
```

| 欄位 | 意思 |
|------|------|
| `COMMAND` | 程式名稱 |
| `PID` | Process ID（用來 kill） |
| `USER` | 執行的使用者 |
| `NAME` | 監聽的 port/address |

---

### 3. 用 netstat 確認（備用）

```bash
netstat -an | grep 5432
```

輸出有 `LISTEN` 就代表有 process 在監聽：
```
tcp4   0   0  *.5432   *.*   LISTEN
tcp6   0   0  *.5432   *.*   LISTEN
```

---

### 4. 查 launchd 管理的服務

```bash
sudo launchctl list | grep postgres
```

輸出範例：
```
-    0    postgresql-17
```

| 欄位 | 意思 |
|------|------|
| 第一欄 `-` | PID（`-` 表示目前沒跑，數字表示正在執行） |
| 第二欄 `0` | 最後一次結束的 exit code（0 = 正常） |
| 第三欄 | 服務名稱 |

---

### 5. 停止 process（一次性）

```bash
sudo kill <PID>
```

例如：`sudo kill 547`

> **注意**：如果 launchd 的 plist 設定了 `KeepAlive = true`，kill 之後 launchd 會立刻重啟它。要真正停止，需要停用 launchd 服務。

---

### 6. 停用 launchd 服務（永久停止開機自啟）

```bash
sudo launchctl disable system/postgresql-17
sudo launchctl stop system/postgresql-17
```

| 指令 | 意思 |
|------|------|
| `disable` | 標記為停用，下次開機不再自動啟動 |
| `stop` | 立刻停止目前正在執行的服務 |

---

### 7. 重新啟用（如果之後需要）

```bash
sudo launchctl enable system/postgresql-17
sudo launchctl start system/postgresql-17
```

---

## 本案例根本原因

```
系統安裝了 PostgreSQL 17（不是 brew services 管理）
        │
        └─ 由 launchd 開機自動啟動（plist: postgresql-17）
                │
                └─ 占用 port 5432
                        │
                        └─ Docker 的 postgres 容器無法綁定 5432
                                │
                                └─ make dev 失敗
```

**brew services** 看不到它，是因為它不是用 brew 安裝或管理的，是獨立安裝的 PostgreSQL 17。

---

## Docker Compose 相關：.env 檔案

另一個常見問題：Docker Compose 需要 `.env` 檔案（注意有 `.` 開頭）。

```
GenericMicroservices/
├── .env          ← Docker Compose 會自動讀取這個
├── env           ← 這個不會被讀取！（少了 . 開頭）
└── docker-compose.yml
```

如果 `.env` 不存在，Docker Compose 會警告：
```
WARN: The "POSTGRES_PASSWORD" variable is not set. Defaulting to a blank string.
```

PostgreSQL 容器收到空的密碼後會直接拒絕啟動：
```
Error: Database is uninitialized and superuser password is not specified.
```

修復方式：
```bash
cp env .env
```

---

## 快速除錯 SOP

```
1. make dev 失敗，出現 port 占用錯誤
        ↓
2. sudo lsof -i :PORT
        ↓
3. 找到 PID 和 COMMAND
        ↓
4. sudo launchctl list | grep <服務名>
        ↓
5. sudo launchctl disable system/<服務名>
   sudo launchctl stop system/<服務名>
        ↓
6. make dev（應該成功）
```
