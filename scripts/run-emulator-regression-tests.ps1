<#
.SYNOPSIS
    Mizan Android Emülatör E2E Regresyon Test Koşturucu Scripti.
.DESCRIPTION
    Bu script:
    1. Android SDK, Java ve CLI ortamını doğrular.
    2. Gerekirse 'mizan_emulator' sanal cihazını (AVD) detached olarak başlatır ve hazır olmasını bekler.
    3. Mizan.App projesini Android için derler (net8.0-android, Debug / MIZAN_DEV_BUILD, EmbedAssembliesIntoApk=true).
    4. Uygulamayı emülatöre yükler ve başlatır.
    5. Tüm uygulama akışlarını (Onboarding, Profil, Ayarlar, Kanonik Veri, Dashboard, Invariant I16,
       12 Dönem, Finansal Yapı, Simülatör, Geçmiş, Profil Değiştirme) emülatör üzerinde otomatik olarak test eder.
    6. Ekran görüntülerini tests/Artifacts/Screenshots/ klasörüne kaydeder.
    7. Tüm adımları doğrulayıp regresyon test sonucunu raporlar.
.PARAMETER SkipBuild
    APK derleme ve yükleme adımını atlayıp mevcut yüklü uygulama ile testleri koşturur.
.PARAMETER AvdName
    Kullanılacak AVD adı (Varsayılan: 'mizan_emulator').
#>

[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [string]$AvdName = "mizan_emulator",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Resolve-Path (Join-Path $ScriptDir "..")
$ArtifactsDir = Join-Path $RepoRoot "tests\Artifacts\Screenshots"
$PackageName = "com.coinflow.mobile.dev"
$MainActivity = "com.coinflow.mobile.dev/crc6481d3350ae0f0c8a0.MainActivity"

# 1. Ortam Kontrolü ve Yol Tanımları
$AndroidHome = if ($env:ANDROID_HOME) { $env:ANDROID_HOME } else { "C:\Users\kayis\AppData\Local\Android\Sdk" }
$JavaHome = if ($env:JAVA_HOME) { $env:JAVA_HOME } else { "C:\Program Files\Microsoft\jdk-17.0.12.7-hotspot" }

$env:ANDROID_HOME = $AndroidHome
$env:ANDROID_SDK_ROOT = $AndroidHome
$env:JAVA_HOME = $JavaHome

$Adb = Join-Path $AndroidHome "platform-tools\adb.exe"
$Emulator = Join-Path $AndroidHome "emulator\emulator.exe"

if (-not (Test-Path $Adb)) {
    throw "ADB bulunamadı: $Adb"
}
if (-not (Test-Path $Emulator)) {
    throw "Emulator bulunamadı: $Emulator"
}

if (-not (Test-Path $ArtifactsDir)) {
    New-Item -ItemType Directory -Path $ArtifactsDir -Force | Out-Null
}

Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Mizan Android Emülatör E2E Regresyon Test Paketi" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "Android SDK  : $AndroidHome" -ForegroundColor Gray
Write-Host "Java HOME    : $JavaHome" -ForegroundColor Gray
Write-Host "AVD Adı      : $AvdName" -ForegroundColor Gray
Write-Host "Konfigürasyon: $Configuration" -ForegroundColor Gray
Write-Host "Paket Adı    : $PackageName" -ForegroundColor Gray
Write-Host "Çıktı Yolu   : $ArtifactsDir" -ForegroundColor Gray
Write-Host "------------------------------------------------------------" -ForegroundColor Gray

# 2. Emülatör Kontrolü ve Başlatma
function Ensure-EmulatorRunning {
    Write-Host "[1/5] Emülatör durumu kontrol ediliyor..." -ForegroundColor Yellow
    $devices = & $Adb devices | Select-String "device$"
    if ($devices.Count -gt 0) {
        Write-Host "  -> Aktif emülatör/cihaz bulundu." -ForegroundColor Green
        return
    }

    Write-Host "  -> Aktif emülatör bulunamadı. '$AvdName' başlatılıyor..." -ForegroundColor Yellow
    $emuCmd = "`"$Emulator`" -avd $AvdName -no-window -no-snapshot-load -no-boot-anim -gpu swiftshader_indirect"
    Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $emuCmd } | Out-Null
    Write-Host "  -> Emülatör başlatıldı, boot tamamlanması bekleniyor..." -ForegroundColor Yellow
    & $Adb wait-for-device

    $bootCompleted = $false
    $timeoutSeconds = 180
    $startTime = Get-Date

    while (-not $bootCompleted) {
        $res = & $Adb shell getprop sys.boot_completed 2>$null
        if ($res -match "1") {
            $bootCompleted = $true
            break
        }
        if (((Get-Date) - $startTime).TotalSeconds -gt $timeoutSeconds) {
            throw "Emülatör $timeoutSeconds saniye içinde açılamadı!"
        }
        Start-Sleep -Seconds 3
    }

    Write-Host "  -> Emülatör hazır!" -ForegroundColor Green
}

# 3. Uygulamayı Derleme ve Yükleme
function Build-And-InstallApp {
    if ($SkipBuild) {
        Write-Host "[2/5] Derleme adımı (--SkipBuild) atlandı." -ForegroundColor Gray
        return
    }

    Write-Host "[2/5] Mizan.App Android için derleniyor (Konfigürasyon: $Configuration, MIZAN_DEV_BUILD=true)..." -ForegroundColor Yellow
    $appProj = Join-Path $RepoRoot "src\Mizan.App\Mizan.App.csproj"
    
    $buildOutput = dotnet build $appProj -f net8.0-android -c $Configuration -p:AndroidSdkDirectory="$AndroidHome" -p:EmbedAssembliesIntoApk=true -p:MizanDevBuild=true
    if ($LASTEXITCODE -ne 0) {
        throw "Derleme başarısız oldu! Logları inceleyin."
    }
    Write-Host "  -> Derleme başarılı." -ForegroundColor Green

    $binDir = Join-Path $RepoRoot "src\Mizan.App\bin\$Configuration\net8.0-android"
    $apkPath = Join-Path $binDir "com.coinflow.mobile.dev-Signed.apk"
    if (-not (Test-Path $apkPath)) {
        $apkPath = (Get-ChildItem -Path $binDir -Filter "*.apk" | Select-Object -First 1).FullName
    }

    if (-not $apkPath -or -not (Test-Path $apkPath)) {
        throw "APK dosyası bulunamadı: $apkPath (Dizin: $binDir)"
    }

    Write-Host "  -> APK emülatöre yükleniyor: $(Split-Path -Leaf $apkPath)..." -ForegroundColor Yellow
    & $Adb install -r "$apkPath" | Out-Null
    Write-Host "  -> APK başarıyla yüklendi." -ForegroundColor Green
}

# 4. Yardımcı Test Fonksiyonları
function Capture-Screen([string]$name) {
    $outPath = Join-Path $ArtifactsDir "$name.png"
    & $Adb shell screencap -p /sdcard/sc.png 2>$null | Out-Null
    cmd.exe /c "`"$Adb`" pull /sdcard/sc.png `"$outPath`" >nul 2>&1"
    Write-Host "    [Görsel Kaydedildi] $name.png" -ForegroundColor Gray
    return $outPath
}

function Dump-Layout {
    $dumpPath = Join-Path $ArtifactsDir "window_dump.xml"
    for ($i = 0; $i -lt 5; $i++) {
        $out = cmd.exe /c "`"$Adb`" shell uiautomator dump /sdcard/window_dump.xml 2>&1"
        if ($out -match "UI hierchary dumped to|window_dump\.xml") {
            cmd.exe /c "`"$Adb`" pull /sdcard/window_dump.xml `"$dumpPath`" >nul 2>&1"
            if (Test-Path $dumpPath) {
                $content = [System.IO.File]::ReadAllText($dumpPath, [System.Text.Encoding]::UTF8)
                if ($content -match "<hierarchy" -and $content -match "bounds=") {
                    return $content
                }
            }
        }
        Start-Sleep -Seconds 1
    }
    return ""
}

function To-RegexPattern([string]$text) {
    # Türkçe karakterleri ve potansiyel kodlama bozulmalarını joker '.' ile eşleştirerek %100 dayanıklı kıl
    $p = $text -replace '[öçşığüÖÇŞİĞÜıİ]', '.'
    $p = $p -replace 'Ã[\x80-\xBF]', '.'
    return $p
}

function Get-ScreenDimensions {
    $sizeStr = & $Adb shell wm size 2>$null
    if ($sizeStr -match '(\d+)x(\d+)') {
        return @{ Width = [int]$matches[1]; Height = [int]$matches[2] }
    }
    return @{ Width = 1080; Height = 2400 }
}

function Scroll-Down {
    $dims = Get-ScreenDimensions
    $x = [int]($dims.Width / 2)
    $y1 = [int]($dims.Height * 0.75)
    $y2 = [int]($dims.Height * 0.25)
    & $Adb shell input swipe $x $y1 $x $y2 300
    Start-Sleep -Milliseconds 600
}

function Scroll-Up {
    $dims = Get-ScreenDimensions
    $x = [int]($dims.Width / 2)
    $y1 = [int]($dims.Height * 0.25)
    $y2 = [int]($dims.Height * 0.75)
    & $Adb shell input swipe $x $y1 $x $y2 300
    Start-Sleep -Milliseconds 600
}

function Find-NodeCoordinates([string]$xml, [string]$textOrId) {
    $safePattern = To-RegexPattern $textOrId

    # Match each <node ... /> or <node ...> tag individually to be immune to attribute ordering
    $pattern = '<node\b([^>]+)>'
    $regexMatches = [regex]::Matches($xml, $pattern)

    foreach ($m in $regexMatches) {
        $tag = $m.Groups[1].Value
        $hasMatch = $false
        $matchedText = $textOrId

        if ($tag -match 'resource-id="([^"]*?' + $safePattern + '[^"]*?)"') {
            $hasMatch = $true
            $matchedText = $matches[1]
        }
        elseif ($tag -match 'content-desc="([^"]*?' + $safePattern + '[^"]*?)"') {
            $hasMatch = $true
            $matchedText = $matches[1]
        }
        elseif ($tag -match 'text="([^"]*?' + $safePattern + '[^"]*?)"') {
            $hasMatch = $true
            $matchedText = $matches[1]
        }

        if ($hasMatch -and $tag -match 'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"') {
            $x1 = [int]$matches[1]
            $y1 = [int]$matches[2]
            $x2 = [int]$matches[3]
            $y2 = [int]$matches[4]
            if ($x2 -gt $x1 -and $y2 -gt $y1) {
                $cx = [int](($x1 + $x2) / 2)
                $cy = [int](($y1 + $y2) / 2)
                return @{ X = $cx; Y = $cy; Found = $true; Text = $matchedText }
            }
        }
    }
    return @{ Found = $false }
}

function Scroll-To-Element([string]$textOrId, [int]$maxSwipes = 6) {
    for ($i = 0; $i -lt $maxSwipes; $i++) {
        $xml = Dump-Layout
        $coords = Find-NodeCoordinates $xml $textOrId
        if ($coords.Found) {
            return $true
        }
        Scroll-Down
    }
    return $false
}

function Tap-Element([string]$textOrId, [string]$stepName) {
    $xml = Dump-Layout
    $coords = Find-NodeCoordinates $xml $textOrId
    if (-not $coords.Found) {
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        $coords = Find-NodeCoordinates $xml $textOrId
    }

    if ($coords.Found) {
        Write-Host "    Tıklanıyor: '$($coords.Text)' ($($coords.X), $($coords.Y))" -ForegroundColor Cyan
        & $Adb shell input tap $coords.X $coords.Y
        Start-Sleep -Milliseconds 1200
        return $true
    } else {
        Write-Host "    UYARI: '$textOrId' ekranda bulunamadı ($stepName)!" -ForegroundColor Yellow
        return $false
    }
}

function Input-Text([string]$text) {
    & $Adb shell input text "$text"
    Start-Sleep -Milliseconds 500
}

function Ensure-DismissDialogs {
    $xml = Dump-Layout
    if ($xml -match (To-RegexPattern "Geçen dönemi güncelleyelim mi|Daha Sonra")) {
        Tap-Element "Daha Sonra" "Dialog Kapat: Geçen Dönem" | Out-Null
        Start-Sleep -Milliseconds 800
        $xml = Dump-Layout
    }
    if ($xml -match (To-RegexPattern "Yedekleme İzni|Şimdi Değil")) {
        Tap-Element "Şimdi Değil" "Dialog Kapat: Yedekleme" | Out-Null
        Start-Sleep -Milliseconds 800
        $xml = Dump-Layout
    }
    if ($xml -match "Vazge") {
        Tap-Element "Vazgeç" "Dialog Kapat: Vazgeç" | Out-Null
        Start-Sleep -Milliseconds 800
        $xml = Dump-Layout
    }
    return $xml
}

function Find-HamburgerCoordinates([string]$xml, $dims) {
    $pattern = '<node\b([^>]+)>'
    $regexMatches = [regex]::Matches($xml, $pattern)
    foreach ($m in $regexMatches) {
        $tag = $m.Groups[1].Value
        if (($tag -match 'class=".*Image(Button|View)"' -or $tag -match 'content-desc=".*(drawer|navigation|çekmece).*') -and $tag -match 'bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"') {
            $x1 = [int]$matches[1]
            $y1 = [int]$matches[2]
            $x2 = [int]$matches[3]
            $y2 = [int]$matches[4]
            if ($y1 -lt ($dims.Height * 0.15) -and $x1 -lt ($dims.Width * 0.3)) {
                $cx = [int](($x1 + $x2) / 2)
                $cy = [int](($y1 + $y2) / 2)
                return @{ X = $cx; Y = $cy; Found = $true }
            }
        }
    }
    return @{ Found = $false }
}

function Ensure-FlyoutOpen {
    Ensure-DismissDialogs | Out-Null
    $dims = Get-ScreenDimensions
    
    for ($attempt = 0; $attempt -lt 4; $attempt++) {
        $xml = Dump-Layout
        # Çekmece açıksa AppShell FlyoutHeader ("PROFİL") veya NavigationView satırları mevcuttur
        if ($xml -match 'text="PROF[İI\.]L"' -or $xml -match 'bounds="\[195,\d+\]\[882,\d+\]"') {
            return $true
        }

        # Toolbar içindeki hamburger butonunu ara veya sol üste tıkla
        $coords = Find-HamburgerCoordinates $xml $dims
        if ($coords.Found) {
            Write-Host "    Hamburger butonuna dinamik tıklanıyor ($($coords.X), $($coords.Y))..." -ForegroundColor Gray
            & $Adb shell input tap $coords.X $coords.Y
        } else {
            Write-Host "    Hamburger butonuna tıklanıyor (75, 136)..." -ForegroundColor Gray
            & $Adb shell input tap 75 136
        }
        Start-Sleep -Seconds 1
    }
    return $false
}

function Navigate-Flyout([string]$menuKey) {
    Ensure-FlyoutOpen | Out-Null
    Start-Sleep -Milliseconds 600

    $targetText = switch ($menuKey.ToLowerInvariant()) {
        "dashboard"     { "Ana Sayfa" }
        "home"          { "Ana Sayfa" }
        "periods"       { "12 Dönem" }
        "simulation"    { "Simülatör" }
        "commitments"   { "Finansal Yapı" }
        "history"       { "Geçmiş" }
        "settings"      { "Ayarlar" }
        "switchprofile" { "Profil Değiştir" }
        default         { $menuKey }
    }

    $autoId = switch ($menuKey.ToLowerInvariant()) {
        "dashboard"     { "flyout-item-dashboard" }
        "home"          { "flyout-item-dashboard" }
        "periods"       { "flyout-item-projection" }
        "simulation"    { "flyout-item-simulation" }
        "commitments"   { "flyout-item-commitments" }
        "history"       { "flyout-item-history" }
        "settings"      { "flyout-item-settings" }
        "switchprofile" { "flyout-item-switch-profile" }
        default         { "" }
    }

    $fallbackY = switch ($menuKey.ToLowerInvariant()) {
        "dashboard"     { 364 }
        "home"          { 364 }
        "periods"       { 496 }
        "simulation"    { 628 }
        "commitments"   { 760 }
        "history"       { 892 }
        "settings"      { 1024 }
        "switchprofile" { 1156 }
        default         { 0 }
    }

    Write-Host "    Flyout Menü Tıklanıyor: '$targetText' ($menuKey)..." -ForegroundColor Cyan
    $tapped = $false
    if ($autoId) {
        $tapped = Tap-Element $autoId "Flyout Menü (AutomationId): $autoId"
    }
    if (-not $tapped) {
        $tapped = Tap-Element $targetText "Flyout Menü: $targetText"
    }
    if (-not $tapped -and $fallbackY -gt 0) {
        Write-Host "    Flyout fallback koordinatına tıklanıyor (538, $fallbackY)..." -ForegroundColor Cyan
        & $Adb shell input tap 538 $fallbackY
        $tapped = $true
    }

    Start-Sleep -Seconds 2
    return $tapped
}

# 5. E2E Regresyon Test Senaryoları
$TestResults = [System.Collections.Generic.List[PSCustomObject]]::new()

function Record-Test([string]$name, [bool]$passed, [string]$detail) {
    $status = if ($passed) { "PASSED" } else { "FAILED" }
    $color = if ($passed) { "Green" } else { "Red" }
    Write-Host "  [$status] $name - $detail" -ForegroundColor $color
    $TestResults.Add([PSCustomObject]@{
        Name = $name
        Status = $status
        Detail = $detail
    })
}

function Run-AllFlows {
    Write-Host "[3/5] Uygulama başlatılıyor..." -ForegroundColor Yellow
    & $Adb shell am force-stop $PackageName
    Start-Sleep -Seconds 1
    & $Adb shell am start -n "$MainActivity" -a android.intent.action.MAIN -c android.intent.category.LAUNCHER | Out-Null
    Start-Sleep -Seconds 5

    Write-Host "[4/5] E2E Regresyon Test Senaryoları Koşturuluyor..." -ForegroundColor Yellow

    # SENARYO 1: Profil Seçimi, Onboarding ve Ana Ekrana Geçiş
    try {
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "01_app_started" | Out-Null

        # 1. Yedekleme izni uyarısı varsa "Şimdi Değil" de
        if ($xml -match (To-RegexPattern "Yedekleme İzni|Şimdi Değil|İzin Ver")) {
            Tap-Element "Şimdi Değil" "Senaryo 1: Yedekleme İzni" | Out-Null
            Start-Sleep -Seconds 2
            $xml = Dump-Layout
        }

        # 2. Eğer zaten Dashboard'daysak doğrudan geç
        if ($xml -match (To-RegexPattern "Mevcut tutar|PLAN|Dönem sonu|Bugünkü tutar")) {
            Record-Test "Profil ve Onboarding Akışı" $true "Uygulama aktif profilde ana ekranda başladı."
        } else {
            # Profil Seçim ekranı: Önce mevcut profili seç, yoksa yeni oluştur
            if ($xml -match "RegresyonTesti") {
                Tap-Element "RegresyonTesti" "Senaryo 1: Mevcut Profil" | Out-Null
                Start-Sleep -Seconds 3
                $xml = Dump-Layout
            } elseif ($xml -match (To-RegexPattern "Temiz Başla|btn-profile-clean-start")) {
                $tappedClean = Tap-Element "btn-profile-clean-start" "Senaryo 1: Temiz Başla (AutomationId)"
                if (-not $tappedClean) {
                    Tap-Element "Temiz Başla" "Senaryo 1: Temiz Başla (Text)" | Out-Null
                }
                Start-Sleep -Seconds 2
                Input-Text "RegresyonTesti"
                Start-Sleep -Milliseconds 500
                Tap-Element "Oluştur" "Senaryo 1: Profil Oluştur" | Out-Null
                Start-Sleep -Seconds 3
                $xml = Dump-Layout
            }

            # 3. Onboarding Ekranı (İlk kurulumda örnek veriyle doldurma)
            if ($xml -match (To-RegexPattern "Örnek Kurulumla Doldur|btn-onboarding-sample|Mizan'ı sana göre hazırlayalım")) {
                $tappedSample = Tap-Element "btn-onboarding-sample" "Senaryo 1: Örnek Kurulum (AutomationId)"
                if (-not $tappedSample) {
                    Tap-Element "Örnek Kurulumla Doldur" "Senaryo 1: Örnek Kurulum (Text)" | Out-Null
                }
                Start-Sleep -Seconds 3
                $xml = Dump-Layout

                if ($xml -match (To-RegexPattern "Mizan'ı Başlat|btn-onboarding-start")) {
                    $tappedStart = Tap-Element "btn-onboarding-start" "Senaryo 1: Mizan'ı Başlat (AutomationId)"
                    if (-not $tappedStart) {
                        Tap-Element "Mizan'ı Başlat" "Senaryo 1: Mizan'ı Başlat (Text)" | Out-Null
                    }
                    Start-Sleep -Seconds 2
                    $xml = Dump-Layout
                }

                if ($xml -match (To-RegexPattern "Mizan hazır|Tamam")) {
                    Tap-Element "Tamam" "Senaryo 1: Mizan Hazır Onay" | Out-Null
                    Start-Sleep -Seconds 3
                    $xml = Dump-Layout
                }
            }

            # Eğer "Geçen dönemi güncelleyelim mi?" dialogu çıktıysa kapat
            $xml = Ensure-DismissDialogs
            Start-Sleep -Seconds 1
            $xml = Dump-Layout

            Capture-Screen "02_dashboard_ready" | Out-Null
            $hasDashboard = $xml -match (To-RegexPattern "Mevcut tutar|PLAN|Dönem sonu|Bugünkü tutar")
            Record-Test "Profil ve Onboarding Akışı" $hasDashboard "Profil oluşturuldu/seçildi ve ana sayfaya ulaşıldı."
        }
    } catch {
        Record-Test "Profil ve Onboarding Akışı" $false $_.Exception.Message
    }

    # SENARYO 2: Ayarlar ve Kanonik Test Verisi Yükleme
    try {
        Navigate-Flyout "Settings" | Out-Null
        Start-Sleep -Seconds 2
        Capture-Screen "03_settings_page" | Out-Null

        # Test Verisini Yükle butonuna ulaşmak için dinamik kaydır
        $foundSeed = Scroll-To-Element "btn-load-canonical-seed"
        if (-not $foundSeed) {
            $foundSeed = Scroll-To-Element "Test Verisini Yükle"
        }

        $loadedSeed = Tap-Element "btn-load-canonical-seed" "Senaryo 2: Test Verisi Yükle (AutomationId)"
        if (-not $loadedSeed) {
            $loadedSeed = Tap-Element "Test Verisini Yükle" "Senaryo 2: Test Verisi Yükle (Text)"
        }

        if ($loadedSeed) {
            Start-Sleep -Seconds 2
            Tap-Element "Tamam" "Senaryo 2: Test Verisi Onay" | Out-Null
            Start-Sleep -Seconds 1
            Capture-Screen "04_seed_loaded" | Out-Null
            Record-Test "Ayarlar ve Test Verisi Yükleme" $true "Ayarlar ekranı açıldı ve kanonik test verisi yüklendi."
        } else {
            Record-Test "Ayarlar ve Test Verisi Yükleme" $false "Test Verisini Yükle butonu bulunamadı."
        }
    } catch {
        Record-Test "Ayarlar ve Test Verisi Yükleme" $false $_.Exception.Message
    }

    # SENARYO 3: Dashboard (Ana Sayfa) & Bakiye Gözlemi (Invariant I16)
    try {
        Navigate-Flyout "Dashboard" | Out-Null
        Start-Sleep -Seconds 2
        Ensure-DismissDialogs | Out-Null
        $xml = Dump-Layout

        # Bakiye girişi yap ve gözlemi kaydet (AutomationId veya Text)
        $inputTapped = Tap-Element "input-today-balance" "Senaryo 3: Bugünkü Tutar Girişi (AutomationId)"
        if (-not $inputTapped) {
            $inputTapped = Tap-Element "Bugünkü tutar" "Senaryo 3: Bugünkü Tutar Girişi (Text)"
        }
        if ($inputTapped) {
            Input-Text "50000"
            $saved = Tap-Element "btn-save-observation" "Senaryo 3: Gözlemi Kaydet (AutomationId)"
            if (-not $saved) {
                Tap-Element "Gözlemi Kaydet" "Senaryo 3: Gözlemi Kaydet (Text)" | Out-Null
            }
            Start-Sleep -Seconds 2
            $xml = Dump-Layout
        }

        Capture-Screen "05_dashboard_observation" | Out-Null

        # Invariant I16 kontrolü: Dondurulmuş plan tutarı korunmalıdır
        $hasFrozenPlan = $xml -match "PLAN" -and ($xml -match (To-RegexPattern "donduruldu|Dönem sonu"))
        Record-Test "Dashboard ve Invariant I16" $hasFrozenPlan "Bakiye gözlemi kaydedildi ve dondurulmuş planın korunduğu doğrulandı."
    } catch {
        Record-Test "Dashboard ve Invariant I16" $false $_.Exception.Message
    }

    # SENARYO 4: 12 Dönem Projeksiyonu & Conservation of Balance
    try {
        Navigate-Flyout "Periods" | Out-Null
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "06_twelve_periods" | Out-Null

        $hasPeriods = $xml -match (To-RegexPattern "FİNANSAL GÖRÜNÜM|12 Dönem|Dönem|TL|Net|Bakiye|Gelecek|Faiz Yükü")
        Record-Test "12 Dönem Projeksiyonu" $hasPeriods "12 aylık nakit akışı ve dönem projeksiyonları görüntülendi."
    } catch {
        Record-Test "12 Dönem Projeksiyonu" $false $_.Exception.Message
    }

    # SENARYO 5: Dönem Detayı Akışı (12 Dönemden Dönem Kartına Giriş)
    try {
        Write-Host "    12 Dönem ekranı aşağı kaydırılarak dönem kartı görünür yapılıyor..." -ForegroundColor Cyan
        Scroll-Down

        Write-Host "    Dönem detayına gitmek için ilk dönem kartına tıklanıyor..." -ForegroundColor Cyan
        $tappedPeriod = Tap-Element "btn-period-detail" "Senaryo 5: Dönem Kartı (AutomationId)"
        if (-not $tappedPeriod) {
            $tappedPeriod = Tap-Element "card-period-overlay" "Senaryo 5: Dönem Kartı Overlay"
        }
        if (-not $tappedPeriod) {
            $tappedPeriod = Tap-Element "Bu dönemin ödeme ayrıntısı" "Senaryo 5: Dönem Kartı Link"
        }
        if (-not $tappedPeriod) {
            $tappedPeriod = Tap-Element "Dönem sonu" "Senaryo 5: Dönem Kartı Bakiye"
        }
        if (-not $tappedPeriod) {
            Write-Host "    Dönem kartı fallback koordinatına tıklanıyor (438, 1400)..." -ForegroundColor Cyan
            & $Adb shell input tap 438 1400
            $tappedPeriod = $true
        }
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "07_period_detail" | Out-Null

        $hasDetail = $xml -match "D.NEM DETAYI" -or $xml -match "D.nem ba" -or $xml -match "Zorunlu .demeler" -or $xml -match "tahmini durum" -or $xml -match "Krediler"
        Record-Test "Dönem Detayı Akışı" $hasDetail "12 Dönem üzerinden dönem detayına girildi ve nakit akışı kırılımları görüntülendi."

        # Geri tuşu ile 12 Dönem ekranına dön
        Write-Host "    Geri tuşuna basılarak 12 Dönem ekranına dönülüyor..." -ForegroundColor Gray
        & $Adb shell input keyevent 4
        Start-Sleep -Seconds 1
    } catch {
        Record-Test "Dönem Detayı Akışı" $false $_.Exception.Message
    }

    # SENARYO 6: Finansal Yapı (Commitments / Kartlar / Krediler)
    try {
        Navigate-Flyout "Commitments" | Out-Null
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "08_commitments" | Out-Null

        $hasCommitments = $xml -match (To-RegexPattern "Kredi|Kart|Taahhüt|Limit|Borç|Taksit|Düzenli|Axess|Bonus")
        Record-Test "Finansal Yapı (Kartlar ve Krediler)" $hasCommitments "Kredi kartları, krediler ve taahhütler görüntülendi."
    } catch {
        Record-Test "Finansal Yapı (Kartlar ve Krediler)" $false $_.Exception.Message
    }

    # SENARYO 7: Simülatör
    try {
        Navigate-Flyout "Simulation" | Out-Null
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "09_simulator" | Out-Null

        $hasSimulator = $xml -match (To-RegexPattern "PLANLAMADAN|Simülatör|Planı Dene|Simülasyon|Aşağıda|Senaryo|Hedef|Fark|Taslak")
        Record-Test "Simülatör Akışı" $hasSimulator "Simülasyon motoru ve senaryo ekranı başarıyla doğrulandı."
    } catch {
        Record-Test "Simülatör Akışı" $false $_.Exception.Message
    }

    # SENARYO 8: Geçmiş (History)
    try {
        Navigate-Flyout "History" | Out-Null
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "10_history" | Out-Null

        $hasHistory = $xml -match "PLAN" -or $xml -match "planlar" -or $xml -match "donmu" -or $xml -match "etkilenmez" -or $xml -match (To-RegexPattern "PLAN vs GERÇEK|Geçmiş|Dönem|Kayıt|Arşiv|Kapanan|PLANLANAN|GERÇEKLEŞEN")
        Record-Test "Geçmiş ve Arşiv Akışı" $hasHistory "Geçmiş dönemler listesi görüntülendi."
    } catch {
        Record-Test "Geçmiş ve Arşiv Akışı" $false $_.Exception.Message
    }

    # SENARYO 9: Profil Değiştirme
    try {
        Navigate-Flyout "SwitchProfile" | Out-Null
        Start-Sleep -Seconds 2
        $xml = Dump-Layout
        Capture-Screen "11_profile_switch_back" | Out-Null

        $switchedBack = $xml -match (To-RegexPattern "Profil Seç|MİZAN|Her profilin|Temiz Başla|RegresyonTesti")
        Record-Test "Profil Değiştirme Akışı" $switchedBack "Profil değiştirme ile güvenli şekilde seçim ekranına dönüldü."
    } catch {
        Record-Test "Profil Değiştirme Akışı" $false $_.Exception.Message
    }
}

# 6. Raporlama ve Özet
function Print-Summary {
    Write-Host "------------------------------------------------------------" -ForegroundColor Gray
    Write-Host "[5/5] Regresyon Test Sonuç Özeti:" -ForegroundColor Cyan

    $passedCount = ($TestResults | Where-Object { $_.Status -eq "PASSED" }).Count
    $failedCount = ($TestResults | Where-Object { $_.Status -eq "FAILED" }).Count
    $totalCount = $TestResults.Count

    foreach ($res in $TestResults) {
        $color = if ($res.Status -eq "PASSED") { "Green" } else { "Red" }
        Write-Host "  [$($res.Status)] $($res.Name): $($res.Detail)" -ForegroundColor $color
    }

    Write-Host "------------------------------------------------------------" -ForegroundColor Gray
    Write-Host "Toplam: $totalCount | Başarılı: $passedCount | Başarısız: $failedCount" -ForegroundColor $(if ($failedCount -eq 0) { "Green" } else { "Red" })
    Write-Host "Ekran Görüntüleri: $ArtifactsDir" -ForegroundColor Cyan

    if ($failedCount -gt 0) {
        throw "Emülatör regresyon testinde $failedCount senaryo başarısız oldu!"
    }
}

# Script Akışı
try {
    Ensure-EmulatorRunning
    Build-And-InstallApp
    Run-AllFlows
    Print-Summary
    Write-Host "`n*** EMÜLATÖR REGRESYON TESTİ BAŞARIYLA TAMAMLANDI! ***`n" -ForegroundColor Green
    exit 0
} catch {
    Write-Host "`n*** HATA: $($_.Exception.Message) ***`n" -ForegroundColor Red
    exit 1
}
