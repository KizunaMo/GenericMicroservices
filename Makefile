# 將預設目標改為 help，這樣只輸入 make 就會看到選單
.DEFAULT_GOAL := help

# 宣告這不是檔案名稱，避免與目錄下的檔案衝突
.PHONY: help dev prod staging down clean logs log config-dev config-prod config-staging

## --- 指令說明選單 ---
help:
	@echo "================================================================="
	@echo "  GenericMicroservices 管理腳本"
	@echo "================================================================="
	@echo "  make dev           - [開發] 啟動開發環境 (背景執行)"
	@echo "  make prod          - [正式] 啟動正式環境 (背景執行)"
	@echo "  make staging       - [測試] 啟動 Staging 環境 (背景執行)"
	@echo "  make down          - 停止所有服務 (保留資料庫資料)"
	@echo "  make clean         - 停止並清除所有資料 (包含資料庫，請慎用！)"
	@echo "  make logs          - 查看所有服務 Log"
	@echo "  make log s=名稱     - 查看特定服務 Log (例如: make log s=auth-service)"
	@echo "================================================================="

## --- 環境啟動 ---

# 開發環境：預設會自動載入 docker-compose.yml + docker-compose.override.yml
# 加入 -d 是背景執行，terminal 不會被佔用
dev:
	docker compose up --build -d

# 正式環境
prod:
	docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d

# Staging 環境
staging:
	docker compose -f docker-compose.yml -f docker-compose.staging.yml up --build -d

## --- 管理與維護 ---

# 停止服務
down:
	docker compose down

# 深度清理
clean:
	docker compose down -v

# Log 追蹤
logs:
	docker compose logs -f

# 特定服務 Log (用法: make log s=gateway)
log:
	docker compose logs -f $(s)

## --- 設定檢查 (Debug 用) ---

config-dev:
	docker compose config

config-prod:
	docker compose -f docker-compose.yml -f docker-compose.prod.yml config

config-staging:
	docker compose -f docker-compose.yml -f docker-compose.staging.yml config