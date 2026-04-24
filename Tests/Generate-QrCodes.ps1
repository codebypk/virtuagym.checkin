# PowerShell-Skript zum Generieren von QR-Code Bilder fuer Tests
# Voraussetzungen: ZXing.Net NuGet-Paket installiert

Add-Type -AssemblyName System.Drawing

# Finde ZXing.Net DLL im globalen NuGet-Cache oder lokalen packages
$zxingDll = $null

# Versuche im globalen NuGet-Cache zu finden
$globalNugetPath = Join-Path $env:USERPROFILE ".nuget\packages"
if (Test-Path $globalNugetPath) {
    $zxingDll = Get-ChildItem -Path $globalNugetPath -Filter "ZXing.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
}

# Fallback: versuche lokale packages
if (-not $zxingDll) {
    $packagesPath = Join-Path $PSScriptRoot "../../packages"
    if (Test-Path $packagesPath) {
        $zxingDll = Get-ChildItem -Path $packagesPath -Filter "ZXing.dll" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    }
}

if (-not $zxingDll) {
    Write-Host "X Fehler: ZXing.Net DLL nicht gefunden!" -ForegroundColor Red
    Write-Host "  Globaler NuGet-Cache: $globalNugetPath" -ForegroundColor Yellow
    Write-Host "  Bitte installieren Sie: dotnet add package ZXing.Net" -ForegroundColor Yellow
    exit 1
}

Write-Host "OK ZXing.Net gefunden: $($zxingDll.FullName)" -ForegroundColor Green

try {
    Add-Type -Path $zxingDll.FullName -ErrorAction Stop
} catch {
    Write-Host "X Fehler beim Laden von ZXing.Net: $_" -ForegroundColor Red
    exit 1
}

# Verzeichnis setup
$testResourcesPath = Join-Path $PSScriptRoot "TestResources\QrCodes"
if (-not (Test-Path $testResourcesPath)) {
    New-Item -ItemType Directory -Path $testResourcesPath -Force | Out-Null
    Write-Host "OK Verzeichnis erstellt: $testResourcesPath" -ForegroundColor Green
}

# QR-Code Generierungs-Funktion
function New-QrCode {
    param([string]$Content, [string]$OutputPath, [int]$Size = 200, [int]$Margin = 10)
    
    try {
        $writer = New-Object ZXing.BarcodeWriter
        $writer.Format = [ZXing.BarcodeFormat]::QR_CODE
        $writer.Options = New-Object ZXing.Common.EncodingOptions
        $writer.Options.Width = $Size
        $writer.Options.Height = $Size
        $writer.Options.Margin = $Margin
        
        $bitmap = $writer.Write($Content)
        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
        
        Write-Host "OK Erstellt: $(Split-Path $OutputPath -Leaf)" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "X Fehler: $_" -ForegroundColor Red
        return $false
    }
}

Write-Host "`n=== QR-Code Generierung fuer VirtuagymMemberCheckIn.Tests ===" -ForegroundColor Cyan

# Card IDs (RFID-Tags)
Write-Host "`nGeneriere Card-ID QR-Codes..." -ForegroundColor Yellow
$cardIds = "0009594224","0005524169"

$successCount = 0
foreach ($cardId in $cardIds) {
    $outputPath = Join-Path $testResourcesPath "qrcode_cardid_$cardId.png"
    if (New-QrCode -Content $cardId -OutputPath $outputPath) {
        $successCount++
    }
}

# Member IDs
Write-Host "`nGeneriere Member-ID QR-Codes..." -ForegroundColor Yellow
$memberIds = "33965531","33457087"

foreach ($memberId in $memberIds) {
    $outputPath = Join-Path $testResourcesPath "qrcode_memberid_$memberId.png"
    if (New-QrCode -Content $memberId -OutputPath $outputPath) {
        $successCount++
    }
}

Write-Host "`nOK Abgeschlossen: $successCount/14 QR-Codes erstellt" -ForegroundColor Green
Write-Host "Speicherort: $testResourcesPath" -ForegroundColor Green
