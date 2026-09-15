$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host '=== Backend tests ===' -ForegroundColor Cyan
Push-Location (Join-Path $root 'backend\tests\TradeWithBeast.Tests')
dotnet test --verbosity minimal
Pop-Location

Write-Host '=== Frontend tests ===' -ForegroundColor Cyan
Push-Location (Join-Path $root 'frontend')
npm run test:ci
Pop-Location

Write-Host 'All tests passed.' -ForegroundColor Green
