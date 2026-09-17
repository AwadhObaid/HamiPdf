param([switch]$NoRun)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ('phase08_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log')
Start-Transcript -Path $logPath | Out-Null
try {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 SDK using Visual Studio Installer.' }
    if (Get-Process HamiPdf -ErrorAction SilentlyContinue) { throw 'Close HamiPdf before cleaning/building.' }
    # Restore first: the installed framework may differ from cached obj assets.
    dotnet restore .\HamiPdf.csproj --force
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed. Check internet access and the nuget.org package source.' }
    dotnet clean .\HamiPdf.csproj -c Debug
    if ($LASTEXITCODE -ne 0) { throw 'Clean failed.' }
    dotnet run --project .\Tests\GeometryChecks\GeometryChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Geometry checks failed. Do not continue to save PDFs.' }
    dotnet run --project .\Tests\PageChecks\PageChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Page operation checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\OverlayChecks\OverlayChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Overlay/project checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\LaunchChecks\LaunchChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'File launch IPC checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\FormChecks\FormChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Interactive form checks failed. Send this log for diagnosis.' }
    dotnet build .\HamiPdf.csproj -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Send this log for diagnosis.' }
    Write-Host '[OK] Phase 08 build and regression checks completed.'
    if (!$NoRun) {
        dotnet run --project .\HamiPdf.csproj -c Debug --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Application exited with an error.' }
    }
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "Log: $logPath"
}
