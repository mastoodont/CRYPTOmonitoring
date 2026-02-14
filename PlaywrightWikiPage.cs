# update-fuzz-tools.ps1
# Обновление Slither, Echidna и Medusa для Windows
# Igor, февраль 2026

# ================= Настройки =================
$ToolsDir       = "C:\Tools\Fuzzers"                  # Папка для Echidna (Medusa/Slither обычно в PATH или python)
$EchidnaExe     = Join-Path $ToolsDir "echidna.exe"

$GoBin          = "$env:USERPROFILE\go\bin"           # Путь для Medusa (go install)
$UseGoBinForMedusa = $true

# Создаём папку для Echidna, если нет
if (-not (Test-Path $ToolsDir)) {
    New-Item -ItemType Directory -Path $ToolsDir | Out-Null
    Write-Host "Создана папка: $ToolsDir" -ForegroundColor Green
}

# ================= Функции =================

function Get-LocalSlitherVersion {
    try {
        $output = & slither --version 2>$null
        if ($output) {
            # Пример: "0.11.5" или "Slither 0.11.5 ..."
            $ver = $output -replace '.*(\d+\.\d+\.\d+).*', '$1'
            return $ver.Trim()
        }
    } catch {}
    return $null
}

function Update-Slither {
    Write-Host "Обновляю Slither через pip..." -ForegroundColor Yellow
    
    # Вариант 1: обычный pip (самый надёжный)
    python -m pip install --upgrade slither-analyzer
    
    # Вариант 2: если установлен uv (современный и быстрый) — раскомментируй
    # uv tool upgrade slither-analyzer
    
    $newVer = Get-LocalSlitherVersion
    if ($newVer) {
        Write-Host "Slither обновлён → версия: $newVer" -ForegroundColor Green
    } else {
        Write-Host "Не удалось обновить/определить версию Slither" -ForegroundColor Yellow
        Write-Host "Попробуйте вручную: python -m pip install --upgrade slither-analyzer" -ForegroundColor Yellow
    }
}

function Get-LocalEchidnaVersion {
    $exe = if (Test-Path $EchidnaExe) { $EchidnaExe } else { "echidna" }
    try {
        $output = & $exe --version 2>$null
        if ($output) {
            $ver = $output -replace '.*Echidna\s+([^\s\(]+).*', '$1'
            return $ver.Trim()
        }
    } catch {}
    return $null
}

function Get-LatestEchidnaVersion {
    try {
        $release = Invoke-RestMethod -Uri "https://api.github.com/repos/crytic/echidna/releases/latest" -Headers @{ "User-Agent" = "PowerShell" }
        return $release.tag_name   # v2.3.1 или 2.3.2-xxx
    } catch {
        Write-Host "Не удалось получить latest Echidna: $_" -ForegroundColor Yellow
        return $null
    }
}

function Update-Echidna {
    param([string]$TargetDir)

    $latestTag = Get-LatestEchidnaVersion
    if (-not $latestTag) { return }

    Write-Host "Последняя версия Echidna: $latestTag" -ForegroundColor Cyan

    $current = Get-LocalEchidnaVersion
    $currentClean = if ($current) { $current -replace '^v','' } else { $null }
    $latestClean = $latestTag -replace '^v',''

    if ($currentClean -and $currentClean -eq $latestClean) {
        Write-Host "Echidna уже актуален ($current)" -ForegroundColor Green
        return
    }

    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/crytic/echidna/releases/latest" -Headers @{ "User-Agent" = "PowerShell" }
    $asset = $release.assets | Where-Object { $_.name -match "win64\.zip|windows.*\.zip" } | Select-Object -First 1

    if (-not $asset) {
        Write-Host "Не найден Windows-билд в релизе $latestTag" -ForegroundColor Red
        return
    }

    $url = $asset.browser_download_url
    $zipPath = Join-Path $env:TEMP "echidna-latest.zip"

    Write-Host "Скачиваю: $url ..." -ForegroundColor Yellow
    Invoke-WebRequest -Uri $url -OutFile $zipPath -UseBasicParsing

    Write-Host "Распаковываю в $TargetDir ..." -ForegroundColor Yellow
    Expand-Archive -Path $zipPath -DestinationPath $TargetDir -Force

    Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

    Write-Host "Echidna обновлён → проверь:" -ForegroundColor Green
    & (Join-Path $TargetDir "echidna.exe") --version
}

function Get-LocalMedusaVersion {
    $exe = if ($UseGoBinForMedusa) { Join-Path $GoBin "medusa.exe" } else { "medusa" }
    try {
        $output = & $exe --version 2>$null
        $ver = $output -replace '.*version\s+(v?[^\s]+).*', '$1'
        return $ver.Trim()
    } catch {}
    return $null
}

function Update-Medusa {
    Write-Host "Обновляю Medusa через go install..." -ForegroundColor Yellow
    go install github.com/crytic/medusa@latest

    $newVer = Get-LocalMedusaVersion
    if ($newVer) {
        Write-Host "Medusa обновлена → версия: $newVer" -ForegroundColor Green
    } else {
        Write-Host "Не удалось определить версию Medusa" -ForegroundColor Yellow
    }
}

# ================= Запуск =================

Write-Host "Обновление Slither / Echidna / Medusa" -ForegroundColor Magenta
Write-Host "Дата: $(Get-Date -Format 'yyyy-MM-dd HH:mm')" -ForegroundColor DarkGray
Write-Host ""

# Slither
$currentS = Get-LocalSlitherVersion
Write-Host "Slither сейчас: " -NoNewline
if ($currentS) { Write-Host $currentS -ForegroundColor Green } else { Write-Host "не найдена" -ForegroundColor Red }
Update-Slither

Write-Host ""

# Echidna
$currentE = Get-LocalEchidnaVersion
Write-Host "Echidna сейчас: " -NoNewline
if ($currentE) { Write-Host $currentE -ForegroundColor Green } else { Write-Host "не найдена" -ForegroundColor Red }
Update-Echidna -TargetDir $ToolsDir

Write-Host ""

# Medusa
$currentM = Get-LocalMedusaVersion
Write-Host "Medusa сейчас: " -NoNewline
if ($currentM) { Write-Host $currentM -ForegroundColor Green } else { Write-Host "не найдена" -ForegroundColor Red }
Update-Medusa

Write-Host ""
Write-Host "Готово!" -ForegroundColor Cyan
Write-Host "Рекомендую добавить в PATH:"
Write-Host "  - $ToolsDir (для Echidna)"
Write-Host "  - путь к python/Scripts (для Slither)"
Write-Host "  - $GoBin (для Medusa)"
