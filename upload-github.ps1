$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubTools.ps1')
Initialize-GitHub
$manifest = Assert-SourceManifest $PSScriptRoot
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'source-manifest.json') -Algorithm SHA256).Hash
$receiptPath = Join-Path $PSScriptRoot 'source-upload.json'
$work = Join-Path $env:TEMP ('HamiPdf-upload-' + [guid]::NewGuid().ToString('N'))
git -c core.autocrlf=false clone --branch main https://github.com/AwadhObaid/HamiPdf.git $work
Assert-Native 'Clone source repository'
Push-Location $work
try {
    git config core.autocrlf false
    Assert-Native 'Configure checkout'
    $head = git rev-parse HEAD
    Assert-Native 'Read remote head'
    $alreadyUploaded = $false
    if (Test-Path -LiteralPath $receiptPath) {
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        $alreadyUploaded = $receipt.commit -eq $head -and $receipt.manifestHash -eq $manifestHash
    }
    # Recover if the previous push succeeded but saving the local receipt was interrupted.
    $remoteManifest = Join-Path $work 'source-manifest.json'
    if (!$alreadyUploaded -and (Test-Path -LiteralPath $remoteManifest)) {
        if ((Get-FileHash -LiteralPath $remoteManifest -Algorithm SHA256).Hash -eq $manifestHash) {
            $null = Assert-SourceManifest $work
            $alreadyUploaded = $true
        }
    }
    if (!$alreadyUploaded) {
        if ($head -ne $manifest.baseCommit) { throw "Remote main changed: $head. No remote files were changed; send this output for review." }
        foreach ($entry in $manifest.files) {
            $destination = Join-Path $work $entry.path
            New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
            Copy-Item -LiteralPath (Join-Path $PSScriptRoot $entry.path) -Destination $destination -Force
        }
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'source-manifest.json') -Destination (Join-Path $work 'source-manifest.json') -Force
        git add --all
        Assert-Native 'Stage source changes'
        git -c core.whitespace=cr-at-eol diff --cached --check -- '*.cs' '*.xaml' '*.csproj' '*.ps1'
        Assert-Native 'Check source patch formatting'
        git -c user.name='Awadh Faghmah' -c user.email='29819140+AwadhObaid@users.noreply.github.com' commit -m "Release $($manifest.version): tested scanner support and GitHub update notifications"
        Assert-Native 'Commit source'
        git push origin HEAD:main
        Assert-Native 'Push source'
        $head = git rev-parse HEAD
        Assert-Native 'Read source commit'
    }
    $remote = git ls-remote origin refs/heads/main
    Assert-Native 'Verify remote source'
    if (($remote -split '\s+')[0] -ne $head) { throw 'Remote source verification failed.' }
    $receiptJson = @{version=$manifest.version;commit=$head;manifestHash=$manifestHash} | ConvertTo-Json
    [IO.File]::WriteAllText($receiptPath,$receiptJson,(New-Object Text.UTF8Encoding($false)))
    Write-Host "[OK] Uploaded HamiPdf $($manifest.version): $head"
    Write-Host "https://github.com/AwadhObaid/HamiPdf/commit/$head"
} finally { Pop-Location; Write-Host "Upload checkout: $work" }
