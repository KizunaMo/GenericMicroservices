# 專案說明：GenericMicroservices 學習專案

## 學習者背景
- Unity 工程師，約 4 年跨平台開發經驗
- 熟悉：SOLID、MVVM、DI、Repository Pattern、C#
- 目標：學習後端架構，具備設計與擴展微服務的能力

## 學習路徑（長期）
### Phase 1：REST API + 資料庫（進行中）
- [x] 建立 DataService（EF Core + PostgreSQL）
- [x] CRUD REST API
- [ ] Repository Pattern
- [ ] 錯誤處理與統一回應格式

### Phase 2：Gateway
- [ ] Demo.Gateway（YARP 路由轉發）
- [ ] 負載平衡概念

### Phase 3：即時通訊
- [ ] Demo.RealTime（WebSocket / SignalR）
- [ ] 服務間通訊（HTTP / Message Broker）

### Phase 4：其他通訊協議
- [ ] TCP/IP
- [ ] gRPC

### Phase 5：部署
- [ ] Docker 容器化
- [ ] Docker Compose 多服務整合

## 環境
- macOS + .NET 8.0 + PostgreSQL 16（Homebrew，localhost:5432）
- DB：demo_db，Username：yuweilyutcit，Password：空白
- 資料表：Items（Id, Name, Description）

## 教學規則（必須遵守）
1. 每次只執行一個步驟
2. 每步驟說明：做什麼、為什麼
3. 步驟完成後問：理解了嗎？可以繼續嗎？
4. 不可一次寫完所有代碼
5. 學習者提問時，先回答問題再繼續
6. 每完成一個階段，更新 LEARNING_LOG.md