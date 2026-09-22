param([switch]$NoRun)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$logDirectory = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
$logPath = Join-Path $logDirectory ('phase11_' + (Get-Date -Format 'yyyyMMdd_HHmmss') + '.log')
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
    dotnet run --project .\Tests\FormPageChecks\FormPageChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Interactive page preservation checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\OverlayChecks\OverlayChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Overlay/project checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\LaunchChecks\LaunchChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'File launch IPC checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\FormChecks\FormChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Interactive form checks failed. Send this log for diagnosis.' }
    dotnet run --project .\Tests\ScanChecks\ScanChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Scanner PDF export checks failed.' }
    dotnet run --project .\Tests\UpdateChecks\UpdateChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Update feed checks failed.' }
    dotnet build .\HamiPdf.csproj -c Debug --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Send this log for diagnosis.' }
    Write-Host '[OK] Phase 11 build and regression checks completed.'
    if (!$NoRun) {
        $appStart = Get-Date
        dotnet run --project .\HamiPdf.csproj -c Debug --no-build
        $applicationExitCode = $LASTEXITCODE
        if ($applicationExitCode -ne 0) {
            Write-Host "Application exit code: $applicationExitCode"
            $traceFolder = Join-Path $env:LOCALAPPDATA 'HamiPdf\logs'
            if (Test-Path -LiteralPath $traceFolder) {
                Get-ChildItem -LiteralPath $traceFolder -Filter 'shutdown_*.log' |
                    Where-Object { $_.LastWriteTime -ge $appStart } |
                    ForEach-Object { Write-Host $_.FullName; Get-Content -LiteralPath $_.FullName | Out-Host }
            }
            Get-WinEvent -FilterHashtable @{ LogName='Application'; StartTime=$appStart } -ErrorAction SilentlyContinue |
                Where-Object { $_.Id -in 1000,1001,1026 -and $_.Message -match 'HamiPdf' } |
                Select-Object -First 3 TimeCreated,Id,Message | Format-List | Out-Host
            throw "Application exited with code $applicationExitCode. Send this log."
        }
        Write-Host '[OK] Application closed normally (exit code 0).'
    }
}
finally {
    Stop-Transcript | Out-Null
    Write-Host "Log: $logPath"
}
