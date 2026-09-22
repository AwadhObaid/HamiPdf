param([string]$IsccPath = '')
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'GitHubTools.ps1')
Set-Location $PSScriptRoot
Initialize-GitHub
$manifest = Assert-SourceManifest $PSScriptRoot
$receiptPath = Join-Path $PSScriptRoot 'source-upload.json'
if (!(Test-Path -LiteralPath $receiptPath)) { throw 'Run upload-github.ps1 first to upload and verify this source.' }
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'source-manifest.json') -Algorithm SHA256).Hash
if ($receipt.manifestHash -ne $manifestHash -or $receipt.version -ne $manifest.version) { throw 'The source upload receipt does not match this package.' }
$sourceHead = gh api repos/AwadhObaid/HamiPdf/commits/main --jq .sha
Assert-Native 'Verify source branch'
if ($sourceHead -ne $receipt.commit) { throw 'Remote source changed since upload. Review the source before publishing.' }
$version = [string]$manifest.version
$tag = "v$version"
$repo = 'AwadhObaid/HamiPdf-Releases'
# A published version is never overwritten.
$repoInfo = Get-GitHubJsonOrNull "repos/$repo"
if ($repoInfo -and $repoInfo.private) { throw 'The release repository must be public for application update checks.' }
if ($repoInfo) {
    $existing = Get-GitHubJsonOrNull "repos/$repo/releases/tags/$tag"
    if ($existing -and !$existing.draft) { throw "Version $tag is already published. Do not overwrite it; use a new version." }
}
$buildArgs = @('-NoProfile','-ExecutionPolicy','Bypass','-File',(Join-Path $PSScriptRoot 'build-installer.ps1'))
if ($IsccPath) { $buildArgs += @('-IsccPath',$IsccPath) }
& powershell.exe @buildArgs
Assert-Native 'Build installer and regression gates'
$null = Assert-SourceManifest $PSScriptRoot
$name = "HamiPdf-Setup-$version-win-x64.exe"
$setup = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'artifacts') -Recurse -File -Filter $name |
    Where-Object { Test-Path -LiteralPath ($_.FullName + '.sha256.txt') } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (!$setup) { throw 'Verified installer was not found.' }
$expected = ((Get-Content -LiteralPath ($setup.FullName + '.sha256.txt') -Raw).Trim() -split '\s+')[0]
$hash = (Get-FileHash -LiteralPath $setup.FullName -Algorithm SHA256).Hash
if ($hash -ne $expected) { throw 'Installer checksum mismatch.' }
$delivery = Join-Path $setup.DirectoryName 'release-assets'
New-Item -ItemType Directory -Path $delivery -Force | Out-Null
Copy-Item -LiteralPath $setup.FullName -Destination (Join-Path $delivery $name) -Force
Copy-Item -LiteralPath $setup.FullName -Destination (Join-Path $delivery 'HamiPdf-Setup.exe') -Force
$notes = Join-Path $delivery 'release-notes.md'
$body = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'RELEASE_NOTES_AR.md') -Raw
$body += "`r`n`r`nSource: https://github.com/AwadhObaid/HamiPdf/commit/$sourceHead`r`nSHA256: $hash`r`n"
[IO.File]::WriteAllText($notes,$body,(New-Object Text.UTF8Encoding($false)))
$checksums = Join-Path $delivery 'SHA256SUMS.txt'
[IO.File]::WriteAllText($checksums,"$hash  $name`n$hash  HamiPdf-Setup.exe`n",(New-Object Text.UTF8Encoding($false)))
if (!$repoInfo) {
    gh repo create $repo --public --add-readme --disable-issues --disable-wiki --description 'Official HamiPdf Windows installers and release notes. Source: AwadhObaid/HamiPdf'
    Assert-Native 'Create public release repository'
}
$existing = Get-GitHubJsonOrNull "repos/$repo/releases/tags/$tag"
$assets = @((Join-Path $delivery $name),(Join-Path $delivery 'HamiPdf-Setup.exe'),$checksums)
if ($existing) {
    if (!$existing.draft) { throw 'This version was published while building. No assets were changed.' }
    gh release upload $tag @assets --repo $repo --clobber
    Assert-Native 'Upload draft assets'
    gh release edit $tag --repo $repo --notes-file $notes --title "HamiPdf $version"
    Assert-Native 'Update draft notes'
} else {
    gh release create $tag @assets --repo $repo --draft --title "HamiPdf $version" --notes-file $notes
    Assert-Native 'Create draft release'
}
$verify = Join-Path $env:TEMP ('HamiPdf-release-check-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verify | Out-Null
try {
    gh release download $tag --repo $repo --dir $verify --pattern $name --pattern 'HamiPdf-Setup.exe' --pattern 'SHA256SUMS.txt'
    Assert-Native 'Download uploaded assets for verification'
    foreach ($file in @($name,'HamiPdf-Setup.exe')) {
        if ((Get-FileHash -LiteralPath (Join-Path $verify $file) -Algorithm SHA256).Hash -ne $hash) { throw "Uploaded asset mismatch: $file. Release remains a draft." }
    }
    if ((Get-FileHash -LiteralPath (Join-Path $verify 'SHA256SUMS.txt')).Hash -ne (Get-FileHash -LiteralPath $checksums).Hash) { throw 'Uploaded checksum file mismatch.' }
} finally { Remove-Item -LiteralPath $verify -Recurse -Force }
gh release edit $tag --repo $repo --draft=false --latest
Assert-Native 'Publish verified release'
$latest = Get-GitHubJsonOrNull "repos/$repo/releases/latest"
if (!$latest -or $latest.tag_name -ne $tag -or $latest.draft -or $latest.prerelease) { throw 'Latest release verification failed. Inspect GitHub before retrying.' }
Write-Host "[OK] Published HamiPdf ${version}: https://github.com/$repo/releases/tag/$tag"
Write-Host "[OK] Direct download: https://github.com/$repo/releases/latest/download/HamiPdf-Setup.exe"
Write-Host "[OK] Setup: $($setup.FullName)"
