# 套件總覽

本專案各服務安裝的 NuGet 套件、用途說明與安裝指令。

> 版本規則：套件主版號需對應 .NET 主版號。
> 本專案使用 .NET 8，所以套件一律指定 `--version 8.x.x`（第三方套件除外）。
> 不指定版本會裝到最新版（可能是 v10），導致不相容錯誤。

---

## Demo.DataService

```bash
cd Demo.DataService
dotnet add package Microsoft.EntityFrameworkCore                --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design        --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL       --version 8.0.0
dotnet add package Grpc.AspNetCore                             --version 2.57.0
dotnet add package MassTransit.RabbitMQ                        --version 8.3.6
dotnet add package Microsoft.AspNetCore.OpenApi                --version 8.0.21
dotnet add package Swashbuckle.AspNetCore                      --version 6.6.2
```

| 套件                                   | 用途                                                            |
|----------------------------------------|-----------------------------------------------------------------|
| Microsoft.EntityFrameworkCore          | ORM 核心，讓 C# 物件對應資料庫資料表，不需手寫 SQL               |
| Microsoft.EntityFrameworkCore.Design   | 開發工具，提供 `dotnet ef` CLI 指令（產生 Migration 等）         |
| Npgsql.EntityFrameworkCore.PostgreSQL  | EF Core 的 PostgreSQL 驅動，讓 EF Core 能連接 PostgreSQL        |
| Grpc.AspNetCore                        | 讓 ASP.NET Core 服務同時支援 gRPC Server（處理來自 GrpcService 的呼叫）|
| MassTransit.RabbitMQ                   | RabbitMQ 的高階封裝，處理連線、序列化、重試，不用直接操作 AMQP   |
| Microsoft.AspNetCore.OpenApi           | 自動產生 OpenAPI（Swagger）規格文件                             |
| Swashbuckle.AspNetCore                 | Swagger UI，讓 `/swagger` 頁面可以互動測試 API                  |

---

## Demo.GrpcService

```bash
cd Demo.GrpcService
dotnet add package Grpc.AspNetCore                       --version 2.57.0
dotnet add package Microsoft.EntityFrameworkCore         --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design  --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.0.0
```

| 套件                                   | 用途                                                      |
|----------------------------------------|-----------------------------------------------------------|
| Grpc.AspNetCore                        | gRPC Server 支援，含 protobuf 編譯工具（Grpc.Tools）      |
| Microsoft.EntityFrameworkCore          | ORM 核心                                                  |
| Microsoft.EntityFrameworkCore.Design   | `dotnet ef` CLI 工具（Migration 用）                      |
| Npgsql.EntityFrameworkCore.PostgreSQL  | PostgreSQL 驅動，連接 grpc_db                             |

---

## Demo.AuthService

```bash
cd Demo.AuthService
dotnet add package Microsoft.EntityFrameworkCore                --version 8.0.0
dotnet add package Microsoft.EntityFrameworkCore.Design        --version 8.0.0
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL       --version 8.0.0
dotnet add package BCrypt.Net-Next                             --version 4.0.3
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.0.0
```

| 套件                                          | 用途                                                              |
|-----------------------------------------------|-------------------------------------------------------------------|
| Microsoft.EntityFrameworkCore                 | ORM 核心，操作 auth_db                                            |
| Microsoft.EntityFrameworkCore.Design          | `dotnet ef` CLI 工具                                              |
| Npgsql.EntityFrameworkCore.PostgreSQL         | PostgreSQL 驅動，連接 auth_db                                     |
| BCrypt.Net-Next                               | 密碼雜湊，儲存和驗證時使用，**絕不儲存明文密碼**                   |
| Microsoft.AspNetCore.Authentication.JwtBearer | 簽發 JWT Token（AuthService 負責簽發）                            |

---

## Demo.Gateway

```bash
cd Demo.Gateway
dotnet add package Yarp.ReverseProxy                                --version 2.3.0
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer   --version 8.0.0
```

| 套件                                          | 用途                                                              |
|-----------------------------------------------|-------------------------------------------------------------------|
| Yarp.ReverseProxy                             | 反向代理，根據路徑將請求轉發到對應的內部服務                       |
| Microsoft.AspNetCore.Authentication.JwtBearer | JWT Token **驗證**（Gateway 只驗 Token，不簽發）                  |

> Gateway 不持有 DB，不簽發 Token。
> 簽發 Token 是 Demo.AuthService 的職責。

---

## Demo.Worker

```bash
cd Demo.Worker
dotnet add package MassTransit.RabbitMQ            --version 8.3.6
dotnet add package Microsoft.Extensions.Hosting    --version 8.0.1
```

| 套件                         | 用途                                                        |
|------------------------------|-------------------------------------------------------------|
| MassTransit.RabbitMQ         | RabbitMQ Consumer，訂閱訊息並處理                           |
| Microsoft.Extensions.Hosting | 讓 Console App 支援 IHostedService（背景常駐服務）           |

---

## Demo.GrpcTestConsole（測試用）

```bash
cd Demo.GrpcTestConsole
dotnet add package Google.Protobuf   --version 3.34.1
dotnet add package Grpc.Net.Client   --version 2.76.0
dotnet add package Grpc.Tools        --version 2.78.0
```

| 套件             | 用途                                                              |
|------------------|-------------------------------------------------------------------|
| Google.Protobuf  | Protobuf 序列化／反序列化核心，處理二進位資料格式                  |
| Grpc.Net.Client  | gRPC Client，建立連線並呼叫遠端 gRPC 服務                         |
| Grpc.Tools       | 開發工具，從 `.proto` 檔案自動產生 C# 程式碼（Build 時執行）       |

---

## 套件版本對應規則

```
.NET 版本   套件主版號
─────────   ──────────
.NET 8    → 8.x.x   （Microsoft.* 系列）
.NET 9    → 9.x.x
.NET 10   → 10.x.x

第三方套件（Yarp、MassTransit、Grpc、Npgsql）有自己的版本號，
不跟隨 .NET 版本，但需確認支援目標 .NET 版本。
```

## 如何查看已安裝的套件

方法一：看 `.csproj` 檔案（最直接）
```xml
<PackageReference Include="套件名稱" Version="版本號" />
```

方法二：CLI 指令
```bash
dotnet list package
```

方法三：Rider → 右鍵專案 → Manage NuGet Packages
