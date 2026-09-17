param([string]$IsccPath = '')
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
if (Get-Process HamiPdf -ErrorAction SilentlyContinue) { throw 'Close HamiPdf before building the installer.' }
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK first.' }
if (!$IsccPath) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($compiler) { $IsccPath = $compiler.Source }
    else {
        $candidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) {
    throw 'Install Inno Setup 6.3 or later from https://jrsoftware.org/isdl.php, then rerun. Or use -IsccPath with the full path to ISCC.exe.'
}
# Run the existing clean/build/regression gate in a separate PowerShell process.
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-run.ps1') -NoRun
if ($LASTEXITCODE -ne 0) { throw 'Build/regression gate failed. Installer was not created.' }
$stamp = (Get-Date -Format 'yyyyMMdd_HHmmss') + '_' + [Guid]::NewGuid().ToString('N').Substring(0,8)
$stage = Join-Path $PSScriptRoot "artifacts\$stamp"
$publish = Join-Path $stage 'publish'
$output = Join-Path $stage 'installer'
New-Item -ItemType Directory -Path $publish,$output -Force | Out-Null
$log = Join-Path $stage 'installer-build.log'
Start-Transcript -Path $log | Out-Null
try {
    [xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'HamiPdf.csproj') -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid application version.' }
    # Debug clean alone cannot invalidate generated WPF BAML in Release.
    # Remove only generated Release folders, never source or installer artifacts.
    foreach ($generated in @('obj\Release','bin\Release')) {
        $generatedPath = Join-Path $PSScriptRoot $generated
        if (Test-Path -LiteralPath $generatedPath) { Remove-Item -LiteralPath $generatedPath -Recurse -Force }
    }
    dotnet publish .\HamiPdf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($required in @('HamiPdf.exe','HamiPdf.dll','HamiPdf.runtimeconfig.json','coreclr.dll','Microsoft.Web.WebView2.Wpf.dll','Forms\Web\index.html','Forms\Web\forms.mjs','Forms\Web\pages.html','Forms\Web\pages.mjs','Forms\Web\pdfjs\build\pdf.worker.mjs','Forms\Web\pdfjs\web\pdf_viewer.mjs')) {
        if (!(Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Missing published file: $required" }
    }
    $loader = Get-ChildItem -LiteralPath $publish -Recurse -Filter WebView2Loader.dll
    if (!$loader) { throw 'WebView2 native loader is missing.' }
    # Verify actual published XAML/resources, not just the version on the EXE.
    $uiReport = Join-Path $publish 'ui-verification.json'
    $check = Start-Process -FilePath (Join-Path $publish 'HamiPdf.exe') -ArgumentList @('--verify-ui', ('"' + $uiReport + '"')) -PassThru
    if (!$check.WaitForExit(60000)) { $check.Kill(); throw 'Published UI verification timed out. Installer was not created.' }
    $check.Refresh()
    if ($check.ExitCode -ne 0 -or !(Test-Path -LiteralPath $uiReport)) { throw 'Published UI verification failed. Installer was not created.' }
    $verified = Get-Content -LiteralPath $uiReport -Raw | ConvertFrom-Json
    if (!$verified.ok -or $verified.version -ne $version) { throw 'Published UI version/features do not match the source.' }
    Write-Host '[OK] Published UI: About button, developer credit and high-resolution PNG verified.'
    & $IsccPath "/DPublishDir=$publish" "/DOutputDir=$output" "/DAppVersion=$version" (Join-Path $PSScriptRoot 'Packaging\HamiPdf.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
    $setup = Join-Path $output "HamiPdf-Setup-$version-win-x64.exe"
    if (!(Test-Path -LiteralPath $setup)) { throw 'Setup executable was not produced.' }
    $hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash
    "$hash  $([IO.Path]::GetFileName($setup))" | Set-Content -LiteralPath "$setup.sha256.txt" -Encoding ASCII
    Write-Host "[OK] Setup: $setup"
    Write-Host "[OK] SHA256: $hash"
    Write-Host 'For the current per-user installation, run .\install-machine.ps1 from normal PowerShell.'
    Write-Host 'New installations: run Setup. Target: Program Files\HamiPdf (administrator approval required).'
    Write-Host 'WebView2 Evergreen Runtime is required on the destination computer.'
    Write-Host 'This build is not digitally signed.'
} finally { Stop-Transcript | Out-Null; Write-Host "Log: $log" }
