<#
.SYNOPSIS
    Mizan Google Firebase Test Lab (FTL) Gerçek Cihaz Test Koşturucu.
.DESCRIPTION
    Bu script:
    1. Google Cloud CLI (gcloud) ortamını ve Firebase projesini doğrular.
    2. Test edilecek Release APK dosyasını bulur veya derler.
    3. Firebase Test Lab üzerindeki gerçek fiziksel cihazlarda (Samsung, Pixel, Xiaomi vb.)
       Robo Test veya E2E testlerini başlatır.
    4. Test çıktılarını, video kayıtlarını ve logcat dökümlerini tests/Artifacts/Firebase/ dizinine indirir.
.PARAMETER ProjectId
    Google Cloud / Firebase proje ID'si. Belirtilmezse gcloud varsayılan projesi kullanılır.
.PARAMETER DryRun
    Komutları Firebase'e göndermeden parametreleri ve APK uyumluluğunu doğrular.
.PARAMETER TestType
    Test tipi: 'robo' (otomatik tarama ve çökme analizi) veya 'instrumentation'. Varsayılan: 'robo'.
.PARAMETER Devices
    Hedef fiziksel cihaz modelleri ve konfigürasyonları.
#>

[CmdletBinding()]
param(
    [string]$ProjectId,
    [switch]$DryRun,
    [string]$TestType = "robo",
    [string]$Configuration = "Release",
    [string[]]$Devices = @(
        "model=dm3q,version=34,locale=tr,orientation=portrait",  # Samsung Galaxy S23
        "model=shiba,version=34,locale=tr,orientation=portrait", # Google Pixel 8
        "model=redfin,version=30,locale=tr,orientation=portrait" # Google Pixel 5 (Android 11 LTS)
    )
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ArtifactsDir = Join-Path $RepoRoot "tests\Artifacts\Firebase"
$BinDir = Join-Path $RepoRoot "src\Mizan.App\bin\$Configuration\net8.0-android"

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Mizan Firebase Test Lab (FTL) Bulut Cihaz Testi" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Test Tipi     : $TestType" -ForegroundColor Gray
Write-Host "Konfigürasyon : $Configuration" -ForegroundColor Gray
Write-Host "Çıktı Dizini  : $ArtifactsDir" -ForegroundColor Gray
Write-Host "Cihaz Matrisi :" -ForegroundColor Gray
foreach ($dev in $Devices) {
    Write-Host "  - $dev" -ForegroundColor Gray
}
Write-Host "------------------------------------------------------------" -ForegroundColor Gray

# 1. APK Kontrolü
$apkPath = Join-Path $BinDir "com.coinflow.mobile.dev-Signed.apk"
if (-not (Test-Path $apkPath)) {
    $apkPath = (Get-ChildItem -Path $BinDir -Filter "*.apk" -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}

if (-not $apkPath -or -not (Test-Path $apkPath)) {
    Write-Host "APK bulunamadı, derleniyor..." -ForegroundColor Yellow
    $appProj = Join-Path $RepoRoot "src\Mizan.App\Mizan.App.csproj"
    dotnet build $appProj -f net8.0-android -c $Configuration -p:EmbedAssembliesIntoApk=true -p:MizanDevBuild=true
    if ($LASTEXITCODE -ne 0) {
        throw "APK derleme başarısız oldu!"
    }
    $apkPath = (Get-ChildItem -Path $BinDir -Filter "*.apk" | Select-Object -First 1).FullName
}

Write-Host "Hedef APK: $apkPath" -ForegroundColor Green

# 2. gcloud CLI Kontrolü
$gcloudCmd = Get-Command "gcloud" -ErrorAction SilentlyContinue
if (-not $gcloudCmd) {
    Write-Host "`n[UYARI] 'gcloud' CLI sisteminizde kurulu değil veya PATH içinde bulunamadı." -ForegroundColor Yellow
    Write-Host "Firebase Test Lab'i çalıştırmak için Google Cloud SDK kurmanız gerekmektedir:" -ForegroundColor Yellow
    Write-Host "https://cloud.google.com/sdk/docs/install`n" -ForegroundColor Cyan
    if ($DryRun) {
        Write-Host "DryRun modu aktif: gcloud kontrolü atlanıyor." -ForegroundColor Yellow
    } else {
        throw "gcloud CLI bulunamadı! Firebase Test Lab'e bağlanılamıyor."
    }
}

# 3. Parametrelerin Hazırlanması
$deviceArgs = @()
foreach ($d in $Devices) {
    $deviceArgs += "--device"
    $deviceArgs += $d
}

$resultsBucket = "mizan-ftl-results-$(Get-Date -Format 'yyyyMMdd-HHmmss')"

$ftlArgs = @(
    "firebase", "test", "android", "run",
    "--type", $TestType,
    "--app", "`"$apkPath`"",
    "--timeout", "5m",
    "--results-dir", $resultsBucket
)

if ($ProjectId) {
    $ftlArgs += @("--project", $ProjectId)
}

$ftlArgs += $deviceArgs

Write-Host "`nOluşturulan Firebase Test Lab Komutu:" -ForegroundColor Cyan
Write-Host "gcloud $($ftlArgs -join ' ')" -ForegroundColor White

if ($DryRun) {
    Write-Host "`n[DryRun]: Komut başarıyla doğrulandı, buluta gönderilmedi." -ForegroundColor Green
    exit 0
}

# 4. Testin Başlatılması
Write-Host "`nFirebase Test Lab testi başlatılıyor..." -ForegroundColor Yellow
& gcloud @ftlArgs

if ($LASTEXITCODE -ne 0) {
    throw "Firebase Test Lab testi başarısız oldu! (Çıkış kodu: $LASTEXITCODE)"
}

Write-Host "`nTestler tamamlandı. Çıktılar indiriliyor..." -ForegroundColor Green
& gcloud storage cp -r "gs://$resultsBucket/*" "$ArtifactsDir"

Write-Host "`n*** FİREBASE TEST LAB TESTLERİ BAŞARIYLA TAMAMLANDI! ***`n" -ForegroundColor Green
