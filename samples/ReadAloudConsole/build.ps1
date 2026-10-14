# Builds the console sample against the engine DLL produced by ..\..\build.ps1 (C# 5, in-box csc).
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Resolve-Path (Join-Path $here '..\..')
$engine = Join-Path $root 'SesliOkuma.Engine.dll'
if (-not (Test-Path $engine)) { throw "Build the engine first: $root\build.ps1" }
$bin = Join-Path $here 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
Copy-Item $engine $bin -Force
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$fw\csc.exe" /nologo /codepage:65001 /target:exe "/out:$bin\ReadAloud.exe" "/r:$engine" (Join-Path $here 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
Write-Host "built: $bin\ReadAloud.exe   (try: .\bin\ReadAloud.exe ""Merhaba dünya"")"
