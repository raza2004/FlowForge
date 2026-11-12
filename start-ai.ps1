# Starts the Python AI service for local development.
#   1. makes sure the AI database (pgvector) container is running, creating it the first time
#   2. starts the service from the myenv folder, which reads its settings from myenv\.env
#
# Run from anywhere:   powershell -ExecutionPolicy Bypass -File D:\Dektop\FlowForge\start-ai.ps1
# Stop it with Ctrl+C. Docs and a health check are at http://127.0.0.1:8001/docs

$ErrorActionPreference = 'Stop'

if (-not (Test-Path "$PSScriptRoot\myenv\.env")) {
    Write-Host "myenv\.env is missing. Copy myenv\.env.example to myenv\.env and fill in your OpenRouter key." -ForegroundColor Red
    exit 1
}

docker start ff-pgvector 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Creating the AI database container (first time only)..."
    docker run -d --name ff-pgvector -e POSTGRES_PASSWORD=test -p 5433:5432 pgvector/pgvector:pg16 | Out-Null
    Start-Sleep -Seconds 6
}

Set-Location "$PSScriptRoot\myenv"
Write-Host "Starting the AI service on http://127.0.0.1:8001 (Ctrl+C to stop)" -ForegroundColor Green
.\Scripts\python.exe -m uvicorn app.main:app --port 8001
