param()
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$repo = 'AwadhObaid/HamiPdf'
function Check-Exit([string]$step) { if ($LASTEXITCODE -ne 0) { throw "$step failed. Nothing was force-pushed." } }
if (!(Get-Command git -ErrorAction SilentlyContinue)) { throw 'Install Git for Windows, then reopen PowerShell.' }
if (!(Get-Command gh -ErrorAction SilentlyContinue)) { throw 'Install GitHub CLI: winget install --id GitHub.cli -e . Then reopen PowerShell.' }
gh auth status
if ($LASTEXITCODE -ne 0) { gh auth login --hostname github.com --web --git-protocol https; Check-Exit 'GitHub login' }
$userJson = gh api user
Check-Exit 'GitHub account check'
$user = $userJson | ConvertFrom-Json
if ($user.login -ne 'AwadhObaid') { throw 'Switch GitHub CLI to AwadhObaid before running this script.' }
if (!(Test-Path -LiteralPath '.git')) {
    git init --initial-branch=main
    Check-Exit 'Git initialization'
}
$branch = git branch --show-current
Check-Exit 'Branch check'
if ($branch -ne 'main') { throw "Current branch is $branch. Select the intended main branch before uploading." }
$remotes = @(git remote)
Check-Exit 'List remotes'
$origin = $null
if ($remotes -contains 'origin') { $origin = git remote get-url origin; Check-Exit 'Read origin' }
if ($origin -and $origin -notmatch '^(https://github\.com/AwadhObaid/HamiPdf(?:\.git)?/?|git@github\.com:AwadhObaid/HamiPdf(?:\.git)?)$') {
    throw "The folder has a different origin: $origin. It has not been changed."
}
$name = git config user.name
if (!$name) { git config user.name 'Awadh Faghmah'; Check-Exit 'Git author name' }
$email = git config user.email
if (!$email) { git config user.email ($user.id.ToString() + '+AwadhObaid@users.noreply.github.com'); Check-Exit 'Git author email' }
# The user explicitly selected AwadhObaid/HamiPdf, including its current public visibility.
$reposJson = gh repo list AwadhObaid --limit 1000 --json nameWithOwner,isPrivate,url
Check-Exit 'List repositories'
$remote = @($reposJson | ConvertFrom-Json) | Where-Object { $_.nameWithOwner -eq $repo } | Select-Object -First 1
if (!$remote) {
    gh repo create $repo --private --description 'HamiPdf - Windows PDF viewer, editor and interactive form filling. Developed by Awadh Faghmah.'
    Check-Exit 'Private repository creation'
    $repoJson = gh repo view $repo --json nameWithOwner,isPrivate,url
    Check-Exit 'Repository verification'
    $remote = $repoJson | ConvertFrom-Json
}
Write-Host ("Target: " + $remote.url + " ; private: " + $remote.isPrivate)
if (!$origin) { git remote add origin 'https://github.com/AwadhObaid/HamiPdf.git'; Check-Exit 'Set origin' }
gh auth setup-git --hostname github.com
Check-Exit 'Git authentication setup'
# The delivered folder contains source only. Personal PDFs, credentials, build outputs and logs are ignored.
git add --all
Check-Exit 'Stage source files'
$staged = @(git diff --cached --name-only)
Check-Exit 'Inspect staged source'
if ($staged.Count -gt 0) {
    Write-Host 'Files included in this commit:'
    $staged | ForEach-Object { Write-Host "  $_" }
    git commit -m 'Add HamiPdf 0.8.2 source, developer credits and Program Files installer'
    Check-Exit 'Commit'
}
git push -u origin main
Check-Exit 'Push main (if history differs, send the error; do not use --force)'
$localHead = git rev-parse HEAD
Check-Exit 'Read local commit'
$remoteRef = git ls-remote origin refs/heads/main
Check-Exit 'Verify remote commit'
if (!$remoteRef -or ($remoteRef -split '\s+')[0] -ne $localHead) { throw 'Remote main does not match local HEAD. Send the output for review.' }
Write-Host "[OK] Uploaded: $($remote.url)"
Write-Host "[OK] Commit: $localHead"
Write-Host ("[OK] Private: " + $remote.isPrivate)
