# Port 設定：開發 vs 正式環境

## 概念：Port 由誰決定？

.NET 應用程式的 Port 由以下來源決定，**優先順序由高到低**：

```
1. 程式碼（ConfigureKestrel）        ← 最高優先
2. 環境變數（ASPNETCORE_URLS）
3. appsettings.json（Kestrel 區塊）
4. launchSettings.json               ← 最低優先（只在開發時有效）
```

高優先的設定會覆蓋低優先的設定。

---

## 怎麼查看目前 Port

### 方法一：看 launchSettings.json

位置：`Properties/launchSettings.json`

```json
"profiles": {
  "http": {
    "applicationUrl": "http://localhost:5128"
  }
}
```

### 方法二：看程式碼（ConfigureKestrel）

```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5128, o => o.Protocols = HttpProtocols.Http1);
    options.ListenLocalhost(5129, o => o.Protocols = HttpProtocols.Http2);
});
```

### 方法三：終端機查詢（確認實際執行中的 Port）

```bash
# 查看所有 LISTEN 狀態的 Port
lsof -iTCP -sTCP:LISTEN -P

# 查詢特定 Port 是否被佔用
lsof -i tcp:5128
```

---

## 開發環境：launchSettings.json

`launchSettings.json` 只在**本機開發**時有效，不會被打包進正式環境。

```json
{
  "profiles": {
    "http": {
      "commandName": "Project",
      "applicationUrl": "http://localhost:5128",
      "environmentVariables": {
        "ASPNETCORE_ENVIRONMENT": "Development"
      }
    }
  }
}
```

**適合場景**：本機跑 Rider / VS Code，快速啟動。

---

## 正式環境：不寫死 Port

正式環境**不應該把 Port 寫死在程式碼裡**，原因：
- 同一份程式碼可能部署在不同機器，Port 不同
- 容器化（Docker）時，Port 由外部指定
- 雲端平台（K8s、Azure、AWS）會管理 Port 映射

### 正確做法一：環境變數（最常用）

```bash
# 啟動時傳入環境變數
ASPNETCORE_URLS="http://0.0.0.0:8080" dotnet MyApp.dll
```

程式碼不需要改，Port 由部署環境控制。

`0.0.0.0` 代表接受所有網路介面的連線（不像 `localhost` 只接受本機）。

### 正確做法二：appsettings.json（各環境不同設定）

```
appsettings.json              ← 預設值
appsettings.Development.json  ← 開發環境覆蓋
appsettings.Production.json   ← 正式環境覆蓋
```

```json
// appsettings.Production.json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  }
}
```

### 正確做法三：Docker Port Mapping

```dockerfile
# Dockerfile 內不指定 Port
EXPOSE 8080
```

```yaml
# docker-compose.yml
services:
  dataservice:
    ports:
      - "5128:8080"   # 外部:內部，外部 Port 由這裡決定
```

---

## 本專案的特殊情況（開發環境用 ConfigureKestrel）

本專案在 `Program.cs` 用 `ConfigureKestrel` 硬寫 Port，是因為需要同時監聽兩個 Port（HTTP/1.1 + HTTP/2），用 `launchSettings.json` 無法做到這個設定：

```csharp
// GenericMicroservices/Program.cs
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5128, o => o.Protocols = HttpProtocols.Http1);  // REST
    options.ListenLocalhost(5129, o => o.Protocols = HttpProtocols.Http2);  // gRPC
});
```

**這是開發環境的寫法。** 正式環境應該把 Port 移到環境變數或 `appsettings.Production.json`，讓部署工具控制。

---

## 本專案 Port 總覽

| 服務              | Port | 協議     | 用途                   | 設定來源             |
|-------------------|------|----------|------------------------|----------------------|
| Demo.Gateway      | 5000 | HTTP/1.1 | 對外入口               | launchSettings.json  |
| DataService       | 5128 | HTTP/1.1 | REST API + Swagger     | ConfigureKestrel     |
| DataService       | 5129 | HTTP/2   | gRPC（供內部服務呼叫） | ConfigureKestrel     |
| Demo.RealTime     | 5200 | HTTP/1.1 | SignalR Hub            | launchSettings.json  |
| Demo.GrpcService  | 5300 | HTTP/2   | gRPC                   | launchSettings.json  |

---

## 常見問題

### `address already in use`

Port 被其他程式佔用，或同一個 Port 被重複綁定。

```bash
# 找出是哪個程式佔用
lsof -i tcp:5128

# 強制終止（謹慎使用）
kill -9 <PID>
```

常見原因：
- 上一次啟動的服務還在執行中
- `launchSettings.json` 和 `ConfigureKestrel` 同時設定了同一個 Port

### `localhost` vs `0.0.0.0`

| 位址        | 說明                                   | 適合場景       |
|-------------|----------------------------------------|----------------|
| `localhost` | 只接受本機連線                          | 開發環境       |
| `127.0.0.1` | 同上（IPv4 明確指定）                   | 開發環境       |
| `0.0.0.0`   | 接受所有網路介面（包含外部網路）的連線  | 正式環境       |
