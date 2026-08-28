# Release helper: bumps version, builds exe + installer, commits, tags and publishes a GitHub release.
# Usage: .\release.ps1 -Version 1.0.1 [-Notes "..."]
param([Parameter(Mandatory=$true)][string]$Version, [string]$Notes = "")
$ErrorActionPreference = 'Stop'
$d = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $d
if (git status --porcelain) { throw "Working tree not clean - commit or stash first." }
$ai = Join-Path $d 'src\AssemblyInfo.cs'
(Get-Content $ai -Raw) -replace 'Assembly(File)?Version\("[\d\.]+"\)', ('Assembly$1Version("' + $Version + '.0")') | Set-Content $ai -Encoding UTF8 -NoNewline
$iss = Join-Path $d 'installer\SesliOkuma.iss'
(Get-Content $iss -Raw) -replace '#define MyAppVersion "[\d\.]+"', ('#define MyAppVersion "' + $Version + '"') | Set-Content $iss -Encoding UTF8 -NoNewline
$env:SESLIOKUMA_NOSTART = '1'
try { & (Join-Path $d 'build.ps1') } finally { $env:SESLIOKUMA_NOSTART = $null }
$iscc = Get-ChildItem "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup not found (winget install JRSoftware.InnoSetup)" }
& $iscc.FullName /Q $iss; if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }
$setup = Join-Path $d "dist\SesliOkuma-Setup-$Version.exe"
if (-not (Test-Path $setup)) { throw "Installer not produced: $setup" }
$portable = Join-Path $d "dist\SesliOkuma-Portable-$Version.zip"
if (Test-Path $portable) { [IO.File]::Delete($portable) }
Compress-Archive -Path (Join-Path $d 'SesliOkuma.exe'), (Join-Path $d 'README.md'), (Join-Path $d 'LICENSE.txt') -DestinationPath $portable
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
$shaFile = "$setup.sha256"
"$sha  $(Split-Path -Leaf $setup)" | Set-Content $shaFile -Encoding ASCII -NoNewline
git add -A; git commit -q -m "release: v$Version"; git tag "v$Version"; git push -q; git push -q --tags
if (-not $Notes) { $Notes = "Sesli Okuma $Version" }
gh release create "v$Version" $setup $shaFile $portable --title "Sesli Okuma $Version" --notes $Notes
Write-Host "Released v$Version  SHA256=$sha"
Write-Host "winget: update winget\*.yaml (PackageVersion, InstallerUrl, InstallerSha256), then: wingetcreate submit winget"
