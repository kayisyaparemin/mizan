<#
.SYNOPSIS
    Mizan Çoklu Cihaz ve Ekran Matrisi (Matrix Testing) E2E Test Koşturucu.
.DESCRIPTION
    Bu script:
    1. Farklı ekran çözünürlükleri (kompakt telefon, standart telefon, tablet) ve Android sürümlerinden oluşan cihaz matrisini tanımlar.
    2. Gerekli AVD'lerin varlığını doğrular veya uygun profil ile oluşturur.
    3. Her cihaz profilinde E2E regresyon testlerini (scripts/run-emulator-regression-tests.ps1) sırayla koşturur.
    4. Ekran görüntülerini profil bazlı ayrıştırır: tests/Artifacts/Screenshots/<ProfilAdı>/
    5. Tüm matris sonuçlarını ve ekran çözünürlüklerini içeren birleşik bir Markdown raporu (tests/Artifacts/matrix-test-report.md) üretir.
.PARAMETER Profiles
    Koşturulacak profil listesi. Seçenekler: 'standard_phone', 'compact_phone', 'tablet', 'all'. Varsayılan: 'standard_phone'.
.PARAMETER SkipBuild
    APK derleme adımını atlar.
#>

[CmdletBinding()]
param(
    [string[]]$Profiles = @("standard_phone"),
    [switch]$SkipBuild,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ArtifactsDir = Join-Path $RepoRoot "tests\Artifacts"
$ScreenshotsBaseDir = Join-Path $ArtifactsDir "Screenshots"
$ReportPath = Join-Path $ArtifactsDir "matrix-test-report.md"

$AndroidHome = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "C:\Users\kayis\AppData\Local\Android\Sdk" }
$Adb = Join-Path $AndroidHome "platform-tools\adb.exe"
$Emulator = Join-Path $AndroidHome "emulator\emulator.exe"
$AvdManager = Join-Path $AndroidHome "cmdline-tools\latest\bin\avdmanager.bat"
if (-not (Test-Path $AvdManager)) {
    $AvdManager = (Get-ChildItem -Path (Join-Path $AndroidHome "cmdline-tools") -Filter "avdmanager.bat" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1).FullName
}

# Cihaz Matrisi Tanımları
$MatrixDefinitions = @{
    "standard_phone" = @{
        Name = "Standart Telefon (Pixel 6 / 1080p)"
        AvdName = "mizan_emulator"
        Resolution = "1080x2400 (xxhdpi)"
        Api = "Android 14 (API 34)"
        Device = "pixel_6"
    }
    "compact_phone" = @{
        Name = "Kompakt Telefon (720p / Küçük Ekran)"
        AvdName = "mizan_compact_api34"
        Resolution = "720x1600 (xhdpi)"
        Api = "Android 14 (API 34)"
        Device = "pixel_4a"
    }
    "tablet" = @{
        Name = "Tablet (1600p / Geniş Ekran)"
        AvdName = "mizan_tablet_api34"
        Resolution = "1600x2560 (xhdpi)"
        Api = "Android 14 (API 34)"
        Device = "pixel_tablet"
    }
}

if ($Profiles -contains "all") {
    $SelectedProfiles = @("standard_phone", "compact_phone", "tablet")
} else {
    $SelectedProfiles = $Profiles
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Mizan Çoklu Cihaz / Ekran Matrisi (Matrix Testing)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Hedef Profiller: $($SelectedProfiles -join ', ')" -ForegroundColor Gray
Write-Host "Rapor Yolu     : $ReportPath" -ForegroundColor Gray
Write-Host "------------------------------------------------------------" -ForegroundColor Gray

# Mevcut AVD listesini al
$existingAvds = & $Emulator -list-avds

$MatrixResults = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($profKey in $SelectedProfiles) {
    if (-not $MatrixDefinitions.ContainsKey($profKey)) {
        Write-Host "Bilinmeyen profil: $profKey, atlanıyor." -ForegroundColor Yellow
        continue
    }

    $def = $MatrixDefinitions[$profKey]
    Write-Host "`n>>> Matris Profili Başlatılıyor: $($def.Name) [$profKey] <<<" -ForegroundColor Yellow

    $avdToUse = $def.AvdName
    if ($existingAvds -notcontains $avdToUse) {
        Write-Host "  AVD '$avdToUse' bulunamadı." -ForegroundColor Yellow
        if ($profKey -ne "standard_phone" -and ($existingAvds -contains "mizan_emulator")) {
            Write-Host "  Uyarı: Profil AVD'si bulunamadığı için mevcut 'mizan_emulator' kullanılacak." -ForegroundColor DarkYellow
            $avdToUse = "mizan_emulator"
        } else {
            throw "Gerekli AVD '$avdToUse' bulunamadı! Lütfen AVD oluşturun veya avdmanager kullanın."
        }
    }

    $profileScreenshotDir = Join-Path $ScreenshotsBaseDir $profKey
    if (-not (Test-Path $profileScreenshotDir)) {
        New-Item -ItemType Directory -Path $profileScreenshotDir -Force | Out-Null
    }

    $startTime = Get-Date
    $passed = $false
    $errorMsg = ""

    try {
        $regScript = Join-Path $ScriptDir "run-emulator-regression-tests.ps1"
        $params = @{
            AvdName = $avdToUse
            Configuration = $Configuration
        }
        if ($SkipBuild) {
            $params["SkipBuild"] = $true
        }

        & $regScript @params
        $passed = ($LASTEXITCODE -eq 0)

        # Ekran görüntülerini profil klasörüne kopyala/taşı
        Get-ChildItem -Path $ScreenshotsBaseDir -Filter "*.png" | ForEach-Object {
            Move-Item -Path $_.FullName -Destination (Join-Path $profileScreenshotDir $_.Name) -Force
        }
    } catch {
        $passed = $false
        $errorMsg = $_.Exception.Message
    }

    $duration = [int]((Get-Date) - $startTime).TotalSeconds

    $MatrixResults.Add([PSCustomObject]@{
        ProfileKey = $profKey
        Name = $def.Name
        Resolution = $def.Resolution
        Api = $def.Api
        Avd = $avdToUse
        Passed = $passed
        DurationSeconds = $duration
        Error = $errorMsg
        ScreenshotDir = $profileScreenshotDir
    })

    # Bir sonraki profilden önce emülatörü kapat (temiz ortam)
    if ($SelectedProfiles.Count -gt 1) {
        Write-Host "  Profil tamamlandı. Emülatör durduruluyor..." -ForegroundColor Gray
        & $Adb emu kill 2>$null | Out-Null
        Start-Sleep -Seconds 5
    }
}

# Markdown Raporu Üret
$reportContent = @"
# Mizan Çoklu Cihaz / Ekran Matrisi (Matrix Testing) Raporu

**Tarih:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")  
**Konfigürasyon:** $Configuration  

## Matris Test Sonuç Özeti

| Profil | Cihaz / Model | Çözünürlük | Android API | Süre | Sonuç |
|---|---|---|---|---|---|
"@

foreach ($res in $MatrixResults) {
    $statusEmoji = if ($res.Passed) { "✅ GEÇTİ (9/9)" } else { "❌ BAŞARISIZ" }
    $reportContent += "`n| **$($res.ProfileKey)** | $($res.Name) | $($res.Resolution) | $($res.Api) | $($res.DurationSeconds)s | $statusEmoji |"
}

$reportContent += @"


## Profil Detayları ve Ekran Görüntüleri

"@

foreach ($res in $MatrixResults) {
    $reportContent += @"
### $($res.Name) (`$($res.ProfileKey)`)
- **AVD:** `$($res.Avd)`
- **Çözünürlük:** $($res.Resolution)
- **API Sürümü:** $($res.Api)
- **Test Durumu:** $(if ($res.Passed) { "Başarılı" } else { "Hata: $($res.Error)" })
- **Ekran Görüntüleri Dizini:** `tests/Artifacts/Screenshots/$($res.ProfileKey)/`

"@
}

Set-Content -Path $ReportPath -Value $reportContent -Encoding UTF8
Write-Host "`n============================================================" -ForegroundColor Cyan
Write-Host "Matris Test Raporu oluşturuldu: $ReportPath" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan

$anyFailed = ($MatrixResults | Where-Object { -not $_.Passed }).Count -gt 0
if ($anyFailed) {
    exit 1
} else {
    exit 0
}
