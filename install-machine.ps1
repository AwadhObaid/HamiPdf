param([string]$SetupPath = '')
$ErrorActionPreference = 'Stop'
# Run as the desktop user, not as another administrator. Elevate only the new Setup.
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from a normal (not Administrator) PowerShell window under the user who installed the old HamiPdf.'
}
if (Get-Process HamiPdf -ErrorAction SilentlyContinue) { throw 'Close HamiPdf before changing the installation.' }
if (!$SetupPath) {
    $artifactRoot = Join-Path $PSScriptRoot 'artifacts'
    $candidate = Get-ChildItem -LiteralPath $artifactRoot -Filter 'HamiPdf-Setup-0.8.2-win-x64.exe' -File -Recurse |
        Where-Object { Test-Path -LiteralPath ($_.FullName + '.sha256.txt') } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (!$candidate) { throw 'Build the 0.8.2 installer first with build-installer.ps1.' }
    $SetupPath = $candidate.FullName
}
$setup = (Resolve-Path -LiteralPath $SetupPath).ProviderPath
if ([IO.Path]::GetFileName($setup) -ne 'HamiPdf-Setup-0.8.2-win-x64.exe') { throw 'Select HamiPdf-Setup-0.8.2-win-x64.exe.' }
if (!(Test-Path -LiteralPath "$setup.sha256.txt")) { throw 'The successful-build SHA256 file is missing. Rebuild Setup before removing the old installation.' }
if (Test-Path -LiteralPath "$setup.sha256.txt") {
    $expected = ((Get-Content -LiteralPath "$setup.sha256.txt" -Raw).Trim() -split '\s+')[0]
    if ($expected -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid Setup SHA256 file. Rebuild before continuing.' }
    if ((Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash -ne $expected) { throw 'Setup SHA256 mismatch. Rebuild before continuing.' }
}
$uninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{8B43D632-67AA-45C5-BE93-F759615B6C19}_is1'
function Get-OldUninstallers {
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry64, [Microsoft.Win32.RegistryView]::Registry32)) {
        $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $key = $base.OpenSubKey($uninstallKey)
            if ($null -ne $key) {
                try {
                    $command = [string]$key.GetValue('UninstallString')
                    if (!$command) { throw 'The old installation has no uninstaller. Repair it or remove it from Windows Settings first.' }
                    # Inno stores a quoted executable path without arguments. Never evaluate registry text as shell code.
                    $exe = $command.Trim().Trim('"')
                    if ([IO.Path]::GetFileName($exe) -notmatch '^unins\d+\.exe$' -or !(Test-Path -LiteralPath $exe -PathType Leaf)) {
                        throw 'The old uninstaller path is invalid. Remove the old HamiPdf from Windows Settings first.'
                    }
                    $exe
                } finally { $key.Dispose() }
            }
        } finally { $base.Dispose() }
    }
}
$oldUninstallers = @(Get-OldUninstallers | Select-Object -Unique)
foreach ($exe in $oldUninstallers) {
    Write-Host 'Remove the previous per-user HamiPdf in the uninstaller window, then this script will continue.'
    $process = Start-Process -FilePath $exe -ArgumentList '/NORESTART' -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw 'Old uninstall did not complete. The new Setup has not been started.' }
}
if (@(Get-OldUninstallers).Count -ne 0) { throw 'The old installation is still registered. Setup has not been started.' }
Write-Host 'Installing HamiPdf for all users. Accept the Windows administrator prompt.'
$process = Start-Process -FilePath $setup -Verb RunAs -PassThru -Wait
if ($process.ExitCode -ne 0) { throw "Setup did not finish successfully (exit $($process.ExitCode)). You can rerun this script." }
$machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry64)
try {
    $key = $machine.OpenSubKey($uninstallKey)
    if ($null -eq $key) { throw 'Machine installation registration was not found.' }
    try { $installedDir = [string]$key.GetValue('InstallLocation') } finally { $key.Dispose() }
} finally { $machine.Dispose() }
$installedExe = Join-Path $installedDir 'HamiPdf.exe'
if (!(Test-Path -LiteralPath $installedExe)) { throw 'Installed HamiPdf.exe was not found.' }
# A manually chosen default PDF app can have an HKCU Applications entry pointing to the old EXE.
# Retarget only that existing HamiPdf entry; do not change the user's default-app selection.
$applicationKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Classes\Applications\HamiPdf.exe', $true)
if ($null -ne $applicationKey) {
    try {
        $commandKey = $applicationKey.CreateSubKey('shell\open\command')
        try { $commandKey.SetValue('', ('"' + $installedExe + '" "%1"')) } finally { $commandKey.Dispose() }
        $iconKey = $applicationKey.CreateSubKey('DefaultIcon')
        try { $iconKey.SetValue('', ('"' + $installedExe + '",0')) } finally { $iconKey.Dispose() }
    } finally { $applicationKey.Dispose() }
}
Write-Host "[OK] Installed: $installedExe"
Write-Host 'Use the new desktop shortcut. If Windows asks which app opens PDFs, choose HamiPdf and Always.'
