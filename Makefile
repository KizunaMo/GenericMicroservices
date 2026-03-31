# 開發環境（自動載入 override.yml，前景執行看 log）
dev:
	docker compose up --build

# 正式環境（手動指定 prod.yml，背景執行）
prod:
	docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d

# Staging 環境
staging:
	docker compose -f docker-compose.yml -f docker-compose.staging.yml up --build -d

# 停止所有服務（保留 DB volume）
down:
	docker compose down

# 停止並清除所有資料（DB volume 一起刪，慎用！）
clean:
	docker compose down -v

# 查看所有 log（持續追蹤）
logs:
	docker compose logs -f

# 查看特定服務 log，用法：make log s=gateway
log:
	docker compose logs -f $(s)

# 確認合併後的最終設定（不真正啟動）
config-dev:
	docker compose config

config-prod:
	docker compose -f docker-compose.yml -f docker-compose.prod.yml config

config-staging:
	docker compose -f docker-compose.yml -f docker-compose.staging.yml config
