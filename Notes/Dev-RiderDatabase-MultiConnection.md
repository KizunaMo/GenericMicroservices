# Rider Database 多連線管理筆記

## 這份筆記記錄什麼？

這個專案有兩個獨立的 PostgreSQL：
- **Homebrew postgres**：本機安裝，`dotnet run` 時使用
- **Docker postgres**：容器內，`docker compose up` 時使用

兩個的資料**完全分開**，要在 Rider 裡分別建立連線才能清楚區分。

---

## 概念：為什麼會有兩個 DB？

```
你的 Mac
  ├── Homebrew PostgreSQL（macOS 服務，開機常駐）
  │     port: 5432
  │     user: yuweilyutcit / 空密碼
  │     資料：本機 dotnet run 時寫入
  │
  └── Docker PostgreSQL（容器，docker compose up 時才存在）
        port: 5432（透過 docker-compose.override.yml 暴露）
        user: postgres / postgres（.env 設定）
        資料：Docker 環境寫入，存在 named volume 裡
```

### 同一個 port，不同資料

兩個 postgres 都綁在 `localhost:5432`，同時只能跑一個：

| 狀態 | localhost:5432 是誰 |
|------|---------------------|
| 只有 Homebrew 在跑 | Homebrew postgres |
| `docker compose up` 有跑 postgres | Docker postgres（override.yml 的 ports: 5432:5432）|
| 兩個同時跑 | port 衝突，Docker postgres 啟動失敗 |

---

## Docker 資料會消失嗎？

**不會。** docker-compose.yml 使用 named volume：

```yaml
postgres:
  volumes:
    - postgres-data:/var/lib/postgresql/data   # 資料存在 volume 裡

volumes:
  postgres-data:   # named volume，獨立於容器
```

| 指令 | 容器 | Volume（資料） |
|------|------|----------------|
| `docker compose stop` | 停止 | **保留** ✅ |
| `docker compose down` | 刪除 | **保留** ✅ |
| `docker compose down -v` | 刪除 | **刪除** ⚠️ 慎用 |

`docker compose up` 重新啟動後，資料完整恢復。

---

## Rider 操作：建立兩個獨立連線

### 步驟 1：開啟 Database 面板

右側邊欄點 **Database** 圖示，或 **View → Tool Windows → Database**

---

### 步驟 2：新增 Homebrew 本機連線

點左上角 **`+`** → **Data Source** → **PostgreSQL**

| 欄位 | 值 |
|------|-----|
| Name | `Local Homebrew Postgres` |
| Host | `localhost` |
| Port | `5432` |
| User | `yuweilyutcit` |
| Password | （空白） |
| Database | （空白，之後手動展開選） |

點 **Test Connection** → 看到綠色勾勾 → **OK**

---

### 步驟 3：新增 Docker 連線

再按一次 **`+`** → **Data Source** → **PostgreSQL**

| 欄位 | 值 |
|------|-----|
| Name | `Docker Postgres` |
| Host | `localhost` |
| Port | `5432` |
| User | `postgres` |
| Password | `postgres` |
| Database | （空白） |

> ⚠️ Docker 沒跑時 Test Connection 會失敗，這是正常的。
> 等 `docker compose up -d postgres` 執行後再連線。

---

### 步驟 4：切換使用

**看本機資料**：
1. 確認 Docker postgres 沒有跑（或未佔用 5432）
2. 在 Database 面板展開 **Local Homebrew Postgres**

**看 Docker 資料**：
1. 先啟動 Docker postgres：`docker compose up -d postgres`
2. 在 Database 面板展開 **Docker Postgres**
3. 若顯示無法連線，右鍵 → **Refresh**

---

## 帳號密碼對照

| 環境 | User | Password | 來源 |
|------|------|----------|------|
| Homebrew 本機 | `yuweilyutcit` | （空白） | macOS 安裝時設定 |
| Docker | `postgres` | `postgres` | `.env` 的 `POSTGRES_USER` / `POSTGRES_PASSWORD` |

> Docker 的帳密定義在 `.env`（不進 git），範本在 `.env.example`。

---

## 資料庫一覽

兩個環境各自有獨立的三個 DB：

| DB 名稱 | 說明 |
|---------|------|
| `demo_db` | Demo.DataService 的 Items 資料 |
| `auth_db` | Demo.AuthService 的 Users、RefreshTokens |
| `grpc_db` | Demo.GrpcService 的 GrpcItems 資料 |

本機和 Docker 的 `demo_db` 資料不同步，各自獨立。

---

## 常見問題

### Q：Rider 連線失敗「Connection refused」

Docker 沒跑，或 Homebrew postgres 服務停止。

```bash
# 確認 Homebrew postgres 狀態
brew services list | grep postgresql

# 啟動 Homebrew postgres
brew services start postgresql@16

# 只啟動 Docker postgres（不啟動其他服務）
docker compose up -d postgres
```

### Q：Docker postgres 啟動失敗（port 已被佔用）

Homebrew postgres 和 Docker postgres 搶 5432 port。

```bash
# 停掉 Homebrew postgres，讓 Docker 用 5432
brew services stop postgresql@16
docker compose up -d postgres
```

### Q：想清空 Docker 資料重來

```bash
docker compose down -v   # 刪容器 + volume（資料全清）
docker compose up        # 重新建立乾淨的 DB
```
