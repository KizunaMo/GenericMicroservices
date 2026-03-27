# gRPC：HTTP/2 設定與常見問題

## 為什麼 gRPC 需要 HTTP/2？

gRPC 使用 Protocol Buffers 二進位格式，並依賴 HTTP/2 的特性（多工、串流、Header 壓縮）。
**HTTP/1.1 完全不支援 gRPC。**

---

## 開發環境的挑戰：沒有 TLS 的 HTTP/2

正式環境 gRPC 走 HTTPS（TLS），HTTP/2 自動協商。
開發環境通常用明文 HTTP（無 TLS），HTTP/2 需要額外設定。

---

## Server 端設定：讓服務同時支援 HTTP/1.1 和 HTTP/2

**錯誤做法一**：只靠 `appsettings.json` 設定 Kestrel
```json
// 不可靠，可能被 launchSettings.json 覆蓋
"Kestrel": {
  "EndpointDefaults": { "Protocols": "Http1AndHttp2" }
}
```

**錯誤做法二**：`ConfigureEndpointDefaults`
```csharp
// 對 launchSettings.json 建立的 endpoint 不生效
builder.WebHost.ConfigureKestrel(options =>
{
    options.ConfigureEndpointDefaults(o => o.Protocols = HttpProtocols.Http1AndHttp2);
});
```

**正確做法**：用兩個 Port，各自明確指定協議
```csharp
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5128, o => o.Protocols = HttpProtocols.Http1);  // REST / Swagger
    options.ListenLocalhost(5129, o => o.Protocols = HttpProtocols.Http2);  // gRPC 專用
});
```

同時要移除 `launchSettings.json` 的 `applicationUrl`，避免 port 衝突：
```json
// launchSettings.json - 移除 applicationUrl，改由程式碼控制
"http": {
  "commandName": "Project",
  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
}
```

---

## Client 端設定：允許明文 HTTP/2

.NET 預設不允許用明文（非 TLS）HTTP/2，需要手動開啟：

```csharp
// Program.cs（Client 服務的啟動程式）
// 放在最頂端，啟動前設定
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
```

---

## 建立 gRPC Channel 連線

```csharp
// Client 呼叫另一個服務的 gRPC
using var channel = GrpcChannel.ForAddress("http://localhost:5129");
var client = new DataItemService.DataItemServiceClient(channel);
var result = await client.GetAllItemsAsync(new GetAllDataItemsRequest());
```

---

## 本專案的 Port 架構

| 服務              | Port | 協議     | 用途                   |
|-------------------|------|----------|------------------------|
| Demo.Gateway      | 5000 | HTTP/1.1 | 對外入口               |
| DataService       | 5128 | HTTP/1.1 | REST API + Swagger     |
| DataService       | 5129 | HTTP/2   | gRPC（供內部服務呼叫） |
| Demo.GrpcService  | 5300 | HTTP/2   | gRPC                   |

---

## 錯誤訊息對照

| 錯誤訊息                       | 原因                              | 解法                                        |
|--------------------------------|-----------------------------------|---------------------------------------------|
| `HTTP_1_1_REQUIRED`            | Server 拒絕 HTTP/2                | Server 端明確設定 HTTP/2 Port               |
| `address already in use`       | Port 被重複綁定                   | 移除 launchSettings.json 的 applicationUrl  |
| `Http2UnencryptedSupport` 錯誤 | Client 未開啟明文 HTTP/2          | 加 AppContext.SetSwitch                     |

---

## 完整架構圖

```
外部 Client（Postman / 瀏覽器）
  │
  │ REST（HTTP/1.1）
  ↓
Gateway（5000）
  │
  │ REST（HTTP/1.1）
  ↓
DataService（5128）        ← REST + Swagger

Demo.GrpcService（5300）   ← 內部服務，不對外
  │
  │ gRPC（HTTP/2，明文）
  ↓
DataService（5129）        ← gRPC 專用 Port
  │
  ↓
demo_db
```

> **開發測試**：用 Postman（支援 gRPC）直接對 Demo.GrpcService 測試，
> 不代表真實情況下有外部 Client 直接呼叫它。
