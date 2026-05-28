# Регистрирует протокол whitespace:// для запуска WhiteSpace по ссылке-приглашению.
# Запуск: правый клик → «Выполнить с PowerShell» или: powershell -ExecutionPolicy Bypass -File register-protocol.ps1

$ErrorActionPreference = "Stop"
$exe = Join-Path $PSScriptRoot "WhiteSpace.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    Write-Error "Не найден $exe. Соберите проект и запустите скрипт из папки с exe."
}

$command = "`"$exe`" `"%1`""
$scheme = "whitespace"
$base = "HKCU:\Software\Classes\$scheme"

New-Item -Path $base -Force | Out-Null
Set-ItemProperty -Path $base -Name "(default)" -Value "URL:WhiteSpace Protocol"
New-ItemProperty -Path $base -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null

$cmdKey = Join-Path $base "shell\open\command"
New-Item -Path $cmdKey -Force | Out-Null
Set-ItemProperty -Path $cmdKey -Name "(default)" -Value $command

Write-Host "Готово: ссылки $scheme://… будут открывать $exe"
