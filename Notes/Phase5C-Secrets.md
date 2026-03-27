# Phase 5-C：Secrets 管理

## 為什麼需要 Secrets 管理？

目前 `appsettings.json` 裡有敏感資訊：

```json
"Jwt": {
  "SecretKey": "dev-secret-key-must-be-at-least-32-chars!!"
},
"ConnectionStrings": {
  "AuthDb": "Host=localhost;Database=auth_db;Username=yuweilyutcit;Password="
}
```

這些資訊如果推上 GitHub：

```
任何人 clone 你的 repo
    └── 得到 JWT SecretKey → 可以偽造任何使用者的 Token
    └── 得到 DB 連線資訊 → 可以直接連你的資料庫
```

---

## 三個環境的 Secrets 策略

| 環境            | 工具                                          | 說明                                    |
|-----------------|-----------------------------------------------|-----------------------------------------|
| 開發（本機）    | .NET User Secrets                             | 儲存在本機家目錄，不進 git              |
| CI/CD / 測試   | 環境變數                                      | Pipeline 注入，不寫在程式碼裡           |
| 正式環境        | 環境變數 / AWS Secrets Manager / Azure Key Vault | 系統注入，程式碼完全不知道值是什麼  |

---

## .NET User Secrets

### 原理

```
appsettings.json（git 追蹤）
    └── SecretKey: ""（空白或預設提示文字）

~/.microsoft/usersecrets/<project-guid>/secrets.json（不進 git，只在本機）
    └── SecretKey: "真正的密鑰"

ASP.NET Core Configuration 系統（啟動時自動合併）
    └── 優先使用 User Secrets 的值（覆蓋 appsettings.json）
    └── 開發時讀到真實密鑰，其他人 clone 只讀到空白
```

### 儲存位置

```
macOS / Linux：~/.microsoft/usersecrets/<guid>/secrets.json
Windows：%APPDATA%\Microsoft\UserSecrets\<guid>\secrets.json
```

`<guid>` 是每個專案唯一的識別碼，存在 `.csproj` 的 `<UserSecretsId>` 欄位：

```xml
<PropertyGroup>
  <UserSecretsId>your-project-guid-here</UserSecretsId>
</PropertyGroup>
```

### User Secrets 只在開發環境生效

```csharp
// ASP.NET Core 預設行為：
// ASPNETCORE_ENVIRONMENT = "Development" 時，自動載入 User Secrets
// ASPNETCORE_ENVIRONMENT = "Production"  時，不載入 User Secrets
```

---

## Configuration 系統的優先順序（完整版）

```
優先順序（高 → 低）：
  1. 環境變數（ASPNETCORE_Jwt__SecretKey=xxx）← 最高，正式環境用
  2. User Secrets（~/.microsoft/usersecrets/）  ← 開發環境用
  3. appsettings.{Environment}.json              ← 環境特定設定
  4. appsettings.json                            ← 預設值或空白   ← 最低
```

後面的值被前面的覆蓋。正式環境通常只有環境變數，其他都不存在。

---

## 環境變數格式

ASP.NET Core 將 `__`（雙底線）視為層級分隔符：

```bash
# appsettings.json 結構：
{
  "Jwt": {
    "SecretKey": "xxx"
  }
}

# 對應的環境變數：
ASPNETCORE_Jwt__SecretKey=your-real-secret-key
```

```bash
# ConnectionStrings 也是一樣：
{
  "ConnectionStrings": {
    "AuthDb": "Host=localhost;..."
  }
}

# 對應的環境變數：
ASPNETCORE_ConnectionStrings__AuthDb=Host=localhost;Database=auth_db;...
```

---

## 操作指令

### 初始化 User Secrets（在專案目錄執行）

```bash
dotnet user-secrets init
```

這個指令在 `.csproj` 加入 `<UserSecretsId>`。

### 設定一個 Secret

```bash
dotnet user-secrets set "Jwt:SecretKey" "your-real-secret-key-here"
dotnet user-secrets set "ConnectionStrings:AuthDb" "Host=localhost;Database=auth_db;Username=xxx;Password=yyy"
```

注意：指令中用 `:` 分隔層級（不是 `__`），`__` 是環境變數的格式。

### 查看所有 Secrets

```bash
dotnet user-secrets list
```

### 移除一個 Secret

```bash
dotnet user-secrets remove "Jwt:SecretKey"
```

### 清除所有 Secrets

```bash
dotnet user-secrets clear
```

---

## appsettings.json 的處理方式

敏感欄位改成空白或提示文字，讓其他人 clone 後知道要自己填：

```json
{
  "Jwt": {
    "SecretKey": "",
    "Issuer": "demo-app",
    "Audience": "demo-app"
  },
  "ConnectionStrings": {
    "AuthDb": ""
  }
}
```

或者更明確的提示：

```json
{
  "Jwt": {
    "SecretKey": "REPLACE_WITH_USER_SECRETS_OR_ENV_VAR"
  }
}
```

---

## .gitignore 說明

User Secrets 儲存在家目錄（`~/.microsoft/usersecrets/`），完全在專案目錄之外，所以 `.gitignore` 不需要特別設定。

但有些開發者會在專案根目錄放 `.env` 檔案（另一種 Secrets 管理方式），這種情況要在 `.gitignore` 加：

```
.env
.env.local
```

本專案用 .NET User Secrets，不用 `.env`。

---

## 本專案需要保護的 Secrets

| 服務              | Secret                       | User Secrets Key                    |
|-------------------|------------------------------|-------------------------------------|
| Demo.AuthService  | JWT SecretKey                | `Jwt:SecretKey`                     |
| Demo.AuthService  | DB 連線字串                  | `ConnectionStrings:AuthDb`          |
| Demo.Gateway      | JWT SecretKey（驗 Token 用） | `Jwt:SecretKey`                     |

注意：Gateway 和 AuthService 的 `Jwt:SecretKey` **必須相同**（同一把 Key 簽發和驗證）。User Secrets 是各專案獨立的，需要分別設定。

---

## 正式環境：環境變數注入

Docker Compose 的注入方式（Phase 6 會實作）：

```yaml
services:
  auth-service:
    environment:
      - ASPNETCORE_Jwt__SecretKey=${JWT_SECRET}
      - ASPNETCORE_ConnectionStrings__AuthDb=${AUTH_DB_CONN}
```

`${JWT_SECRET}` 來自 CI/CD 的 Secrets 設定（GitHub Actions Secrets、AWS Parameter Store 等），不寫在任何程式碼或設定檔裡。

---

## 本專案實際操作紀錄

### 步驟一：初始化 User Secrets（每個服務各做一次）

在各個專案目錄下執行：

```bash
cd Demo.AuthService
dotnet user-secrets init

cd ../Demo.Gateway
dotnet user-secrets init
```

**這個指令做了什麼：**
在 `.csproj` 裡加入一行 `<UserSecretsId>`，這是這個專案的 Secrets 儲存位置的識別碼：

```xml
<!-- Demo.AuthService/Demo.AuthService.csproj -->
<PropertyGroup>
  <UserSecretsId>b82641d9-656d-4af3-8df4-af73e6d6b06d</UserSecretsId>
</PropertyGroup>
```

**如何確認有沒有做過：**
打開 `.csproj`，有 `<UserSecretsId>` 就代表已初始化。

---

### 步驟二：設定 Secrets（存到本機，不進 git）

```bash
# AuthService（在 Demo.AuthService 目錄執行）
dotnet user-secrets set "Jwt:SecretKey" "dev-secret-key-must-be-at-least-32-chars!!"
dotnet user-secrets set "ConnectionStrings:AuthDb" "Host=localhost;Database=auth_db;Username=yuweilyutcit;Password="

# Gateway（在 Demo.Gateway 目錄執行）
dotnet user-secrets set "Jwt:SecretKey" "dev-secret-key-must-be-at-least-32-chars!!"
```

**這些值存在哪裡：**

```
~/.microsoft/usersecrets/b82641d9-.../secrets.json  ← AuthService 的 Secrets
~/.microsoft/usersecrets/c99eb5bb-.../secrets.json  ← Gateway 的 Secrets
```

**如何查看目前設定了哪些 Secrets：**

```bash
# 方法一：切換到專案目錄再執行
cd Demo.AuthService
dotnet user-secrets list

# 方法二：在 solution 根目錄指定 --project
dotnet user-secrets list --project Demo.AuthService/Demo.AuthService.csproj
dotnet user-secrets list --project Demo.Gateway/Demo.Gateway.csproj
```

> 注意：`dotnet user-secrets` 必須在有 `.csproj` 的目錄執行，或用 `--project` 指定。
> 在 solution 根目錄執行會報錯：`Could not find a MSBuild project file`。

**如何用 Finder 找到這個檔案：**

```
Finder → 前往 → 前往檔案夾 → 輸入：~/.microsoft/usersecrets/
找到對應 GUID 的資料夾，裡面有 secrets.json
```

**secrets.json 長這樣：**

```json
{
  "Jwt:SecretKey": "dev-secret-key-must-be-at-least-32-chars!!",
  "ConnectionStrings:AuthDb": "Host=localhost;Database=auth_db;Username=yuweilyutcit;Password="
}
```

---

### 步驟三：清空 appsettings.json 的敏感值

**需要修改的檔案：**
- `Demo.AuthService/appsettings.json`
- `Demo.Gateway/appsettings.json`

**改法：敏感值改成空字串，非敏感值保留**

```json
// Demo.AuthService/appsettings.json（改後）
{
  "ConnectionStrings": {
    "AuthDb": ""           ← 清空，真實值在 User Secrets
  },
  "Jwt": {
    "SecretKey": "",       ← 清空，真實值在 User Secrets
    "Issuer":   "demo-app",   ← 非敏感，保留
    "Audience": "demo-app"    ← 非敏感，保留
  }
}
```

**什麼叫敏感？什麼可以留？**

| 值                          | 是否敏感 | 原因                              |
|-----------------------------|----------|-----------------------------------|
| `SecretKey`                 | 是       | 洩漏後可偽造任意 Token            |
| `ConnectionStrings`（含帳密）| 是      | 洩漏後可直接連 DB                 |
| `Issuer` / `Audience`       | 否       | 只是識別字串，沒有安全意義        |
| `Logging` 設定              | 否       | 純行為設定                        |
| `AllowedHosts`              | 否       | 純設定                            |

---

### 步驟四：驗證設定正確

重啟服務後測試登入，如果 Token 正常回傳，代表 Configuration 系統成功從 User Secrets 讀到值。

**如果服務啟動失敗（常見問題）：**
```
System.ArgumentException: JWT SecretKey is empty
```
→ User Secrets 沒設好，用 `dotnet user-secrets list` 確認。

---

## 完整的 Secrets 管理流程

```
開發階段：
  開發者 A clone repo
      └── appsettings.json 的 SecretKey 是空白
      └── 執行 dotnet user-secrets set "Jwt:SecretKey" "xxx"
      └── 本機的 ~/.microsoft/usersecrets/.../secrets.json 有真實值
      └── 程式啟動，Configuration 系統自動合併，讀到真實值

正式部署：
  CI/CD Pipeline
      └── 從 Secrets 管理系統取得真實值
      └── 注入到容器的環境變數
      └── 程式啟動，Configuration 系統讀環境變數（最高優先）
      └── appsettings.json 的空白值被覆蓋
```
