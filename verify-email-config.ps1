# Email Service Configuration Verification Script
Write-Host ""
Write-Host "=== MyStore Email Service Configuration Check ===" -ForegroundColor Cyan
Write-Host ""

$settingsFile = "MyStore.Functions/local.settings.json"

if (-not (Test-Path $settingsFile)) {
    Write-Host "ERROR: local.settings.json not found" -ForegroundColor Red
    exit 1
}

$settings = Get-Content $settingsFile | ConvertFrom-Json
$connectionString = $settings.Values.'AzureCommunicationServices__ConnectionString'
$fromEmail = $settings.Values.'Email__FromEmail'

Write-Host "Configuration Status:" -ForegroundColor Cyan
Write-Host ""

if ([string]::IsNullOrWhiteSpace($connectionString)) {
    Write-Host "  Email Service: DISABLED (not configured)" -ForegroundColor Yellow
    Write-Host "  The app will work, but NO emails will be sent." -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To enable, see EMAIL-SERVICE-SETUP.md" -ForegroundColor Cyan
} else {
    Write-Host "  Email Service: ENABLED" -ForegroundColor Green
    Write-Host "  From: $fromEmail" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Test by sending registration request (see EMAIL-SERVICE-SETUP.md)" -ForegroundColor Cyan
}
Write-Host ""
