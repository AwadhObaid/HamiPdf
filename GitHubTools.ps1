$ErrorActionPreference = 'Stop'
function Assert-Native([string]$Step) { if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)." } }
function Initialize-GitHub {
    if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw 'Install Git for Windows.' }
    if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI: winget install --id GitHub.cli -e' }
    gh auth status
    if ($LASTEXITCODE -ne 0) { gh auth login --hostname github.com --web --git-protocol https; Assert-Native 'GitHub login' }
    $login = gh api user --jq .login
    Assert-Native 'Read account'
    if ($login -ne 'AwadhObaid') { throw 'Select the AwadhObaid account in GitHub CLI first.' }
    gh auth setup-git --hostname github.com
    Assert-Native 'Git authentication'
}
function Get-GitHubJsonOrNull([string]$Endpoint) {
    $prior = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $raw = @(& gh api $Endpoint 2>&1); $code = $LASTEXITCODE } finally { $ErrorActionPreference = $prior }
    $text = ($raw | ForEach-Object { $_.ToString() }) -join "`n"
    if ($code -eq 0) { return ($text | ConvertFrom-Json) }
    if ($text -match 'HTTP 404') { return $null }
    throw "GitHub request failed: $Endpoint`n$text"
}
function Assert-SourceManifest([string]$Root) {
    $manifest = Get-Content -LiteralPath (Join-Path $Root 'source-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        $path = Join-Path $Root $entry.path
        if (!(Test-Path -LiteralPath $path) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw "Source file changed or missing: $($entry.path). Use a matching update package before publishing."
        }
    }
    return $manifest
}
