# Регистрирует протокол whitespace:// для текущего пользователя Windows.
# Запустите один раз из PowerShell (не от имени администратора не обязательно для HKCU):
#   powershell -ExecutionPolicy Bypass -File .\register-protocol.ps1

$ErrorActionPreference = "Stop"

$exePath = $null
if ($PSScriptRoot) {
    $candidate = Join-Path $PSScriptRoot "WhiteSpace.exe"
    if (Test-Path $candidate) {
        $exePath = (Resolve-Path $candidate).Path
    }
}

if (-not $exePath) {
    Write-Host "Не найден WhiteSpace.exe рядом со скриптом. Укажите полный путь к exe:" -ForegroundColor Yellow
    $exePath = Read-Host "Путь к WhiteSpace.exe"
}

if (-not (Test-Path $exePath)) {
    Write-Host "Файл не найден: $exePath" -ForegroundColor Red
    exit 1
}

$exePath = (Resolve-Path $exePath).Path
$command = "`"$exePath`" `"%1`""

New-Item -Path "HKCU:\Software\Classes\whitespace" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\whitespace" -Name "(Default)" -Value "URL:WhiteSpace"
New-ItemProperty -Path "HKCU:\Software\Classes\whitespace" -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null

New-Item -Path "HKCU:\Software\Classes\whitespace\shell\open\command" -Force | Out-Null
Set-ItemProperty -Path "HKCU:\Software\Classes\whitespace\shell\open\command" -Name "(Default)" -Value $command

Write-Host "Протокол whitespace:// зарегистрирован для:" -ForegroundColor Green
Write-Host "  $exePath"
