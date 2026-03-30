# Phase 5-B：HTTPS / TLS

## 為什麼需要 HTTPS？

目前 HTTP 的問題：

```
Client ──── HTTP ────► Gateway
            │
            └── Token、帳密都是明文傳輸
                網路中間任何人都能攔截並讀取
```

加上 HTTPS 之後：

```
Client ──── HTTPS（TLS 加密）────► Gateway
            │
            └── 傳輸內容是密文
                中間人攔截到的是亂碼，無法讀取
```

---

## TLS 是什麼？

TLS（Transport Layer Security）是加密協議。
HTTPS = HTTP + TLS，在 HTTP 的基礎上加上一層加密。

TLS 做兩件事：

| 功能 | 說明 |
|------|------|
| 加密傳輸 | 資料在傳輸中變成密文，防止竊聽 |
| 身份驗證 | 憑證證明「你連到的真的是目標伺服器，不是冒牌的」|

---

## 憑證是什麼？

TLS 需要「憑證（Certificate）」才能運作。
憑證是一份數位文件，包含：

```
・伺服器的公鑰（用來加密傳輸）
・伺服器的身份資訊（domain、組織名稱）
・發行機構的簽名（證明這份憑證是真的）
```

### 憑證種類

| 種類          | 發行者                         | 用途       | 瀏覽器信任 |
|---------------|--------------------------------|------------|------------|
| 自簽憑證      | 自己（用工具產生）              | 開發環境   | 否（顯示警告）|
| CA 憑證       | 受信任機構（Let's Encrypt 等） | 正式環境   | 是         |

開發時用自簽憑證，瀏覽器會顯示「連線不安全」警告，把它加入信任清單後就不再出現。

---

## .NET 開發環境設定

.NET SDK 內建開發用自簽憑證，一行指令產生並信任：

```bash
dotnet dev-certs https --trust
```

這個指令做了：
1. 產生本機開發用的自簽憑證
2. 安裝到系統信任清單（macOS 的 Keychain）
3. 之後 `https://localhost:xxxx` 瀏覽器不再顯示警告

確認憑證狀態：
```bash
dotnet dev-certs https --check
```

---

## 本專案 HTTPS 架構

**只有 Gateway 需要對外 HTTPS，內部服務用 HTTP：**

```
外部 Client
    │ HTTPS :5001（加密）
    ▼
Demo.Gateway
    │ HTTP（內部網路）
    ├──► Demo.AuthService  :5100
    ├──► Demo.DataService  :5128
    └──► Demo.RealTime     :5200
```

**為什麼內部可以用 HTTP？**

內部服務不對外暴露，只在同一台機器或內部網路溝通。
Gateway 這層加密已經保護了 Client 和伺服器之間的傳輸，這是最重要的一段。

正式環境若有更嚴格的安全需求（如 Zero Trust 架構），才會讓內部服務也用 HTTPS。

---

## Gateway 的 Port 設計

加上 HTTPS 後，Gateway 同時監聽兩個 Port：

| Port | 協議  | 用途                    |
|------|-------|-------------------------|
| 5000 | HTTP  | 開發時方便測試（可選保留）|
| 5001 | HTTPS | 加密連線，正式使用        |

---

## launchSettings.json 設定

```json
"profiles": {
  "http": {
    "commandName": "Project",
    "applicationUrl": "https://localhost:5001;http://localhost:5000"
  }
}
```

`applicationUrl` 用分號分隔，同時定義多個監聽位址。
.NET 啟動時會同時監聽兩個 Port。

**5001 只是慣例，不是強制規定：**

```
.NET 新建專案預設：
  http  → 5000
  https → 5001

這只是業界慣例，讓人看到 5001 就知道是 HTTPS。
你可以改成任何數字，例如 8080 / 8443。
```

**本專案所有 Port 都是自己定義的：**

```
全部在 launchSettings.json 或 appsettings.json 設定，
沒有任何一個 Port 是系統強制的。

Demo.Gateway     :5000 (HTTP)  :5001 (HTTPS)   ← launchSettings.json
Demo.AuthService :5100                          ← launchSettings.json
Demo.DataService :5128 (REST)  :5129 (gRPC)    ← ConfigureKestrel（Program.cs）
Demo.RealTime    :5200                          ← launchSettings.json
Demo.GrpcService :5300                          ← launchSettings.json
Demo.TcpService  :5400                          ← Program.cs（TcpListener）
```

**在哪裡設定 Port 的優先順序：**

```
優先順序（高 → 低）：
  1. ConfigureKestrel（Program.cs）  ← 最高，覆蓋一切
  2. 環境變數 ASPNETCORE_URLS
  3. appsettings.json 的 Kestrel 設定
  4. launchSettings.json             ← 最低，只在開發時生效

正式環境用環境變數控制 Port，不寫死在程式碼裡。
開發環境用 launchSettings.json 方便設定。
```

**如何查看目前服務跑在哪個 Port：**

服務啟動時 console 會印出：
```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://localhost:5001
      Now listening on: http://localhost:5000
```

---

## 正式環境：Let's Encrypt

開發用自簽憑證只適合本機，正式環境需要 CA 憑證。
Let's Encrypt 提供免費的 CA 憑證：

```
流程：
1. 擁有一個 domain（如 api.myapp.com）
2. Let's Encrypt 驗證你確實控制這個 domain
3. 發行憑證（有效期 90 天，可自動續期）
```

Docker + Nginx/Caddy 的部署方案通常內建自動取得和續期 Let's Encrypt 憑證，不需要手動處理。這屬於 Phase 6（部署）的範疇。

---

## HTTP vs HTTPS 什麼時候用？

| 情境                   | 建議    |
|------------------------|---------|
| 開發環境 localhost     | HTTP 或 HTTPS 都可以 |
| 對外服務（正式環境）   | 一定要 HTTPS         |
| 服務間內部溝通         | HTTP 可接受（同內網）|
| 傳輸敏感資料（Token、密碼）| 一定要 HTTPS    |

---

## 實作步驟（從零開始重現）

### 涉及的文件

| 檔案路徑 | 新增/修改 | 職責 |
|----------|-----------|------|
| 系統 Keychain（macOS）| 自動修改 | 信任開發用自簽憑證 |
| `Demo.Gateway/Properties/launchSettings.json` | 修改 | 讓 Gateway 同時監聽 HTTP 和 HTTPS |

只有這兩個地方需要改，因為 .NET SDK 已內建自簽憑證管理，ASP.NET Core 啟動時自動讀取憑證。

---

### 步驟 1：產生並信任開發用憑證

**目的**：讓瀏覽器不再對 localhost HTTPS 顯示「連線不安全」警告

```bash
dotnet dev-certs https --trust
```

這個指令做了三件事：
1. 產生本機開發用的自簽憑證（`.pfx` 格式）
2. 安裝到作業系統的憑證信任清單（macOS：Keychain，Windows：Certificate Store）
3. 之後 `https://localhost:xxxx` 瀏覽器視為受信任的連線

確認憑證狀態（是否已信任）：
```bash
dotnet dev-certs https --check
```

如果憑證損壞或需要重置：
```bash
dotnet dev-certs https --clean  # 刪除現有憑證
dotnet dev-certs https --trust  # 重新產生並信任
```

---

### 步驟 2：讓 Gateway 同時監聽 HTTP 和 HTTPS

**目的**：Gateway 對外同時提供 HTTP :5000 和 HTTPS :5001

**修改檔案**：`Demo.Gateway/Properties/launchSettings.json`

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "dotnetRunMessages": true,
      "launchBrowser": false,
      "applicationUrl": "https://localhost:5001;http://localhost:5000",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**關鍵改動**：`applicationUrl` 用分號分隔兩個位址，ASP.NET Core 啟動時會同時監聽兩個 Port。

**為什麼保留 HTTP :5000**：開發時用 Postman 或 curl 測試，不需要憑證，更方便。正式環境可以拿掉 HTTP，只保留 HTTPS。

---

### 驗證方式

1. 啟動 Gateway，console 應顯示：
```
Now listening on: https://localhost:5001
Now listening on: http://localhost:5000
```

2. 用 Postman 或 curl 測試 HTTPS 端點：
```bash
curl https://localhost:5001/auth/login \
  -X POST \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"password"}'
```
→ 預期正常回傳 Token（不應出現憑證錯誤）

3. 用瀏覽器開啟 `https://localhost:5001`（若有前端），確認沒有「連線不安全」警告
