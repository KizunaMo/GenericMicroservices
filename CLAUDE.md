# 專案說明：GenericMicroservices 學習專案

## 學習者背景
- Unity 工程師，約 4 年跨平台開發經驗
- 熟悉：SOLID、MVVM、DI、Repository Pattern、C#
- 目標：學習後端架構，具備設計與擴展微服務的能力
- 參考框架：自建 AMO_Dev（Unity 套件），熟悉模組化設計

## 學習路徑（長期）

### Phase 1：REST API + 資料庫 ✅
- [x] 建立 DataService（EF Core + PostgreSQL）
- [x] CRUD REST API
- [x] Repository Pattern（通用底層 IRepository<T> + Repository<T>）
- [x] 錯誤處理與統一回應格式（ApiResponse<T> + ExceptionMiddleware）

### Phase 2：Gateway ✅
- [x] Demo.Gateway（YARP 路由轉發）
- [x] CORS 集中在 Gateway 處理（RequireCors）
- [x] 負載平衡概念（Cluster Destinations）

### Phase 3：即時通訊
- [x] 3-A：Demo.RealTime（SignalR / WebSocket）
- [x] 3-B：RabbitMQ（Message Broker，服務間非同步通訊）

### Phase 4：其他通訊協議
- [x] 4-A：gRPC（高效能服務間通訊）
- [ ] 4-B：TCP Socket Raw（自訂協議，對接硬體設備）

### Phase 5：安全性（正式上線前必備）
- [ ] 5-A：JWT 身份驗證（集中在 Gateway）
  - Token 簽發與驗證
  - 受保護的 API endpoint
  - Refresh Token 機制
- [ ] 5-B：HTTPS / TLS
  - 開發環境憑證設定
  - 正式環境憑證（Let's Encrypt）
- [ ] 5-C：Secrets 管理
  - 環境變數取代 appsettings.json 中的敏感資訊
  - .NET User Secrets（開發）
  - 正式環境 Secrets 注入方式
- [ ] 5-D：Rate Limiting
  - 限制每個 IP 的請求頻率
  - 防止暴力攻擊與 DDoS

### Phase 6：部署
- [ ] 6-A：Docker 容器化（每個服務獨立 Dockerfile）
- [ ] 6-B：Docker Compose 多服務整合
- [ ] 6-C：環境設定（開發 / 測試 / 正式三套設定）
- [ ] 6-C：健康檢查（Health Checks）

### Phase 7：可觀測性（Observability）
- [ ] 7-A：結構化日誌（Serilog）
- [ ] 7-B：分散式追蹤（OpenTelemetry）
- [ ] 7-C：指標監控（Prometheus + Grafana）

---

## 通訊協議全覽（參考）
詳見 Notes/NetworkProtocols.md

| 協議 | Phase | 用途 |
|---|---|---|
| REST / HTTP | Phase 1 ✅ | CRUD API |
| WebSocket / SignalR | Phase 3-A ✅ | 即時雙向通訊 |
| Message Broker（RabbitMQ）| Phase 3-B | 非同步服務解耦 |
| gRPC | Phase 4-A | 高效能服務間通訊 |
| TCP Socket Raw | Phase 4-B | 自訂協議 / 硬體對接 |

---

## 環境
- macOS + .NET 8.0 + PostgreSQL 16（Homebrew，localhost:5432）
- DB：demo_db，Username：yuweilyutcit，Password：空白
- 資料表：Items（Id, Name, Description）

## 服務 Port 對照
| 服務 | Port | 協議 | 說明 |
|---|---|---|---|
| Demo.Gateway | 5000 | HTTP/1.1 | 唯一對外入口 |
| GenericMicroservices（DataService）| 5128 | HTTP/1.1 | REST API + Swagger |
| GenericMicroservices（DataService）| 5129 | HTTP/2 | gRPC（供內部服務呼叫）|
| Demo.RealTime | 5200 | WebSocket | SignalR Hub |
| Demo.GrpcService | 5300 | HTTP/2 | gRPC |

## 教學規則（必須遵守）
1. 每次只執行一個步驟
2. 每步驟說明：做什麼、為什麼
3. 步驟完成後問：理解了嗎？可以繼續嗎？
4. 不可一次寫完所有代碼
5. 學習者提問時，先回答問題再繼續
6. 每完成一個階段，更新 LEARNING_LOG.md
7. 每個 Phase 有獨立的學習筆記（Notes/）
8. 學習者針對專有名詞提問時，建立獨立的學習筆記(Notes/)
