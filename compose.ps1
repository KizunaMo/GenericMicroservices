# Docker Compose 多環境啟動腳本（Windows PowerShell）
#
# 使用方式：
#   .\compose.ps1 dev              開發環境
#   .\compose.ps1 prod             正式環境
#   .\compose.ps1 staging          Staging 環境
#   .\compose.ps1 down             停止所有服務
#   .\compose.ps1 clean            停止並刪除所有資料（含 DB，慎用！）
#   .\compose.ps1 logs             查看所有 log
#   .\compose.ps1 log gateway      查看特定服務 log
#   .\compose.ps1 config-dev       查看開發環境合併後設定
#   .\compose.ps1 config-prod      查看正式環境合併後設定
#   .\compose.ps1 config-staging   查看 Staging 合併後設定
#
# 第一次使用前，需要在 PowerShell 執行（只做一次）：
#   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

param(
    [Parameter(Position = 0)]
    [string]$Command,

    [Parameter(Position = 1)]
    [string]$Service
)

switch ($Command) {
    "dev" {
        Write-Host "[dev] 啟動開發環境..." -ForegroundColor Cyan
        docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.dev.yml up --build -d
    }
    "prod" {
        Write-Host "[prod] 啟動正式環境..." -ForegroundColor Green
        docker compose -f docker-compose.yml -f docker-compose.prod.yml up --build -d
    }
    "staging" {
        Write-Host "[staging] 啟動 Staging 環境..." -ForegroundColor Yellow
        docker compose -f docker-compose.yml -f docker-compose.staging.yml up --build -d
    }
    "down" {
        Write-Host "[down] 停止所有服務..." -ForegroundColor Gray
        docker compose down
    }
    "clean" {
        Write-Host "[clean] 停止並刪除所有資料（含 DB Volume）..." -ForegroundColor Red
        $confirm = Read-Host "確定要刪除所有資料嗎？(y/N)"
        if ($confirm -eq "y") {
            docker compose down -v
        } else {
            Write-Host "已取消。" -ForegroundColor Gray
        }
    }
    "logs" {
        Write-Host "[logs] 查看所有 log..." -ForegroundColor Cyan
        docker compose logs -f
    }
    "log" {
        if (-not $Service) {
            Write-Host "請指定服務名稱，例如：.\compose.ps1 log gateway" -ForegroundColor Red
            exit 1
        }
        Write-Host "[log] 查看 $Service 的 log..." -ForegroundColor Cyan
        docker compose logs -f $Service
    }
    "config-dev" {
        Write-Host "[config-dev] 開發環境合併設定：" -ForegroundColor Cyan
        docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.dev.yml config
    }
    "config-prod" {
        Write-Host "[config-prod] 正式環境合併設定：" -ForegroundColor Green
        docker compose -f docker-compose.yml -f docker-compose.prod.yml config
    }
    "config-staging" {
        Write-Host "[config-staging] Staging 合併設定：" -ForegroundColor Yellow
        docker compose -f docker-compose.yml -f docker-compose.staging.yml config
    }
    default {
        Write-Host ""
        Write-Host "用法：.\compose.ps1 <指令> [服務名稱]" -ForegroundColor White
        Write-Host ""
        Write-Host "  dev              開發環境（前景執行，顯示 log）"
        Write-Host "  prod             正式環境（背景執行）"
        Write-Host "  staging          Staging 環境（背景執行）"
        Write-Host "  down             停止所有服務（保留 DB 資料）"
        Write-Host "  clean            停止並刪除所有資料（含 DB，慎用！）"
        Write-Host "  logs             查看所有服務 log"
        Write-Host "  log <服務>       查看特定服務 log，例如：log gateway"
        Write-Host "  config-dev       查看開發環境合併後的完整設定"
        Write-Host "  config-prod      查看正式環境合併後的完整設定"
        Write-Host "  config-staging   查看 Staging 合併後的完整設定"
        Write-Host ""
    }
}
