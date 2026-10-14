# Release helper: bumps version, builds exe + installer, commits, tags and publishes a GitHub release.
# Usage: .\release.ps1 -Version 1.0.1 [-Notes "..."]
param([Parameter(Mandatory=$true)][string]$Version, [string]$Notes = "")
$ErrorActionPreference = 'Stop'
$d = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $d
if (git status --porcelain) { throw "Working tree not clean - commit or stash first." }
$ai = Join-Path $d 'src\AssemblyInfo.cs'
(Get-Content $ai -Raw) -replace 'Assembly(File)?Version\("[\d\.]+"\)', ('Assembly$1Version("' + $Version + '.0")') | Set-Content $ai -Encoding UTF8 -NoNewline
$eai = Join-Path $d 'engine\AssemblyInfo.cs'
(Get-Content $eai -Raw) -replace 'Assembly(File)?Version\("[\d\.]+"\)', ('Assembly$1Version("' + $Version + '.0")') | Set-Content $eai -Encoding UTF8 -NoNewline
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
Compress-Archive -Path (Join-Path $d 'SesliOkuma.exe'), (Join-Path $d 'SesliOkuma.Engine.dll'), (Join-Path $d 'README.md'), (Join-Path $d 'LICENSE.txt') -DestinationPath $portable
$sha = (Get-FileHash $setup -Algorithm SHA256).Hash
$shaFile = "$setup.sha256"
"$sha  $(Split-Path -Leaf $setup)" | Set-Content $shaFile -Encoding ASCII -NoNewline
# NuGet package of the engine; pushed to nuget.org only when NUGET_API_KEY is set, always attached to the release.
$nuget = Join-Path $d 'tools\nuget\nuget.exe'
if (-not (Test-Path $nuget)) { New-Item -ItemType Directory -Force (Split-Path $nuget) | Out-Null; Invoke-WebRequest 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget -UseBasicParsing }
& $nuget pack (Join-Path $d 'nuget\SesliOkuma.Engine.nuspec') -Version $Version -OutputDirectory (Join-Path $d 'dist') -NoPackageAnalysis | Out-Null
$nupkg = Join-Path $d "dist\SesliOkuma.Engine.$Version.nupkg"
if (-not (Test-Path $nupkg)) { throw "NuGet package not produced: $nupkg" }
git add -A; git commit -q -m "release: v$Version" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"; git tag "v$Version"; git push -q; git push -q --tags
if (-not $Notes) { $Notes = "Sesli Okuma $Version" }
gh release create "v$Version" $setup $shaFile $portable $nupkg --title "Sesli Okuma $Version" --notes $Notes
Write-Host "Released v$Version  SHA256=$sha"
if ($env:NUGET_API_KEY) { & $nuget push $nupkg -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY -NonInteractive -SkipDuplicate; Write-Host "nuget.org: SesliOkuma.Engine $Version pushed" }
else { Write-Host "nuget.org: set NUGET_API_KEY to publish SesliOkuma.Engine (package attached to the GitHub release)" }
# winget: submit the update PR (the package is published); manifests go to temp, not into the repo.
if (Get-Command wingetcreate -ErrorAction SilentlyContinue) {
  $env:WINGET_CREATE_GITHUB_TOKEN = (gh auth token)
  $wgOut = Join-Path $env:TEMP "sesliokuma-winget-$Version"
  $setupUrl = "https://github.com/korayorhun/SesliOkuma/releases/download/v$Version/SesliOkuma-Setup-$Version.exe"
  wingetcreate update KorayOrhun.SesliOkuma --version $Version --urls "$setupUrl|x64|user" --out $wgOut --prtitle "Update version: KorayOrhun.SesliOkuma version $Version" --submit
  Remove-Item Env:WINGET_CREATE_GITHUB_TOKEN
} else { Write-Host "winget: wingetcreate not found - submit the update manually" }

# scoop: keep the personal bucket (korayorhun/scoop-bucket) on the latest portable zip
try {
  $zipHash = (Get-FileHash $portable -Algorithm SHA256).Hash.ToLower()
  $json = @"
{
    "version": "$Version",
    "description": "Read selected text aloud in any Windows app with one hotkey: natural voices, word highlighting, screen OCR, free translation.",
    "homepage": "https://github.com/korayorhun/SesliOkuma",
    "license": "MIT",
    "url": "https://github.com/korayorhun/SesliOkuma/releases/download/v$Version/SesliOkuma-Portable-$Version.zip",
    "hash": "$zipHash",
    "shortcuts": [
        [
            "SesliOkuma.exe",
            "Sesli Okuma"
        ]
    ],
    "checkver": "github",
    "autoupdate": {
        "url": "https://github.com/korayorhun/SesliOkuma/releases/download/v`$version/SesliOkuma-Portable-`$version.zip"
    }
}
"@
  $b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
  $previous = gh api repos/korayorhun/scoop-bucket/contents/bucket/sesliokuma.json --jq .sha 2>$null
  $apiArgs = @('-X', 'PUT', 'repos/korayorhun/scoop-bucket/contents/bucket/sesliokuma.json',
    '-f', "message=sesliokuma: Update to version $Version`n`nCo-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>", '-f', "content=$b64")
  if ($previous) { $apiArgs += @('-f', "sha=$previous") }
  gh api @apiArgs --jq .commit.sha | Out-Null
  Write-Host "scoop bucket updated to $Version"
} catch { Write-Host "scoop bucket update failed: $_" }
