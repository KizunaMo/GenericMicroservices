# Docker 中的 localhost 問題

## 問題描述

在開發時很容易碰到：**程式寫 `localhost` 卻連線失敗，改成 `127.0.0.1` 就成功**。
這兩個看起來一樣，其實有根本差異。

---

## 一、localhost vs 127.0.0.1

### `127.0.0.1` 是什麼

這是一個固定的 **IPv4 位址**，代表「自己這台機器的回送介面（loopback）」。
你寫 `127.0.0.1`，系統直接連那個 IP，不需要查詢任何東西。

### `localhost` 是什麼

這是一個**主機名稱**（hostname），不是 IP。
系統收到 `localhost` 時，需要先查 `/etc/hosts` 或 DNS，把它解析成一個 IP，才能連線。

### 問題在哪裡

現代系統（macOS、Linux）預設會優先解析成 **IPv6**：

```
localhost → ::1        （IPv6 loopback，系統優先嘗試）
localhost → 127.0.0.1  （IPv4 loopback，備用）
```

但 .NET Kestrel 預設監聽的是 **IPv4**（`0.0.0.0` 或 `127.0.0.1`）。

結果：
```
你寫 localhost → 系統解析成 ::1（IPv6）→ Kestrel 不在那裡 → 連線失敗
你寫 127.0.0.1 → 直接 IPv4 → Kestrel 在那裡 → 連線成功
```

### 建議

開發時直接寫 `127.0.0.1`，避免 DNS 解析的不確定性：

```csharp
// Unity 連 SignalR Hub
// 不建議
.WithUrl("http://localhost:5010/hub/chat")

// 建議
.WithUrl("http://127.0.0.1:5010/hub/chat")
```

---

## 二、在 Docker 裡，localhost 更複雜

Docker 裡的 `localhost` 問題不只是 IPv4/IPv6，還有**「你是誰的 localhost」**的問題。

### 核心概念：每個容器是獨立的機器

Docker 的每個容器都是一個獨立的環境，有自己的網路介面。
所以在容器內說 `localhost`，指的是**容器自己**，不是你的 Mac。

```
你的 Mac
├── Terminal（本機）
│     localhost = Mac 本機
│
└── Docker 容器
      ├── gateway 容器
      │     localhost = gateway 容器自己
      │
      └── auth-service 容器
            localhost = auth-service 容器自己
```

### 各種情境對照

| 誰在連 | 寫法 | 實際連到哪裡 | 結果 |
|--------|------|-------------|------|
| Mac 本機（Unity / Postman） | `localhost:5010` | Mac 的 5010 port | 可能成功（看 IPv6 問題） |
| Mac 本機（Unity / Postman） | `127.0.0.1:5010` | Mac 的 5010 port | 成功（直接 IPv4） |
| gateway 容器內 | `localhost:5100` | gateway 容器自己的 5100 | 失敗（auth-service 不在這裡） |
| gateway 容器內 | `auth-service:8080` | auth-service 容器 | 成功（Docker 內網） |
| gateway 容器內 | `host.docker.internal:5100` | Mac 宿主機的 5100 | 成功（特殊名稱） |

### 實際案例：Gateway 連 AuthService

開發環境（本機直接跑），Gateway 的設定寫：
```json
"Address": "http://localhost:5100"
```
這樣可以，因為兩個服務都在 Mac 本機上跑。

Docker 環境，Gateway 的設定必須改成：
```json
"Address": "http://auth-service:8080"
```
因為在容器內，`localhost` 是 gateway 自己，找不到 auth-service。
必須用服務名稱，Docker 內網會自動解析。

這就是為什麼這個專案有 `appsettings.Production.json`，專門覆蓋 Docker 環境的位址設定。

---

## 三、`host.docker.internal` 是什麼

這是 Docker Desktop 提供的特殊主機名稱，讓**容器內**可以連回 **Mac 宿主機**。

使用場景：
- 容器想連 Mac 本機跑的服務（例如本機跑的 PostgreSQL）
- 開發時還沒把某個服務容器化，先用本機版

```yaml
# docker-compose.yml 中，某服務想連本機的 PostgreSQL
environment:
  - ConnectionStrings__DB=Host=host.docker.internal;Database=mydb;...
```

> 注意：`host.docker.internal` 只在 Docker Desktop（Mac / Windows）有效，Linux 上需要額外設定。

---

## 四、總結

| 場景 | 正確寫法 |
|------|---------|
| Mac 本機連本機服務 | `127.0.0.1:<port>`（避免 IPv6 問題） |
| 容器連同網路的其他容器 | `<服務名稱>:<容器內port>`（例如 `auth-service:8080`） |
| 容器連 Mac 宿主機 | `host.docker.internal:<port>` |
| Mac 本機連 Docker 容器 | `127.0.0.1:<對外port>`（compose 的 ports 映射） |
