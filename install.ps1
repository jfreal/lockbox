# Builds Lockbox and installs it for the current Windows user.
# Puts Lockbox.exe in %LOCALAPPDATA%\Programs\Lockbox and adds a Start menu shortcut.
# Works in Windows PowerShell 5.1. Your vault (%LOCALAPPDATA%\Lockbox) is never touched.

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
$target = Join-Path $env:LOCALAPPDATA 'Programs\Lockbox'

if (Get-Process -Name Lockbox -ErrorAction SilentlyContinue) {
    throw 'Lockbox is running. Close it, then run this again.'
}

dotnet publish (Join-Path $repo 'src\Lockbox.App\Lockbox.App.csproj') `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=None `
    -o $target
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)." }

$exe = Join-Path $target 'Lockbox.exe'
$shortcutPath = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Lockbox.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $target
$shortcut.IconLocation = "$exe,0"
$shortcut.Description = 'Encrypted secrets, on this PC'
$shortcut.Save()

Write-Host "Installed $exe"
Write-Host "Start menu shortcut: $shortcutPath"
