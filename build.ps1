# Builds SesliOkuma.exe from src\*.cs with the .NET Framework 4.8 C# compiler (C# 5 syntax).
$ErrorActionPreference = 'Stop'
$d = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $d 'SesliOkuma.exe'
$ico = Join-Path $d 'assets\SesliOkuma.ico'
$running = Get-Process SesliOkuma -ErrorAction SilentlyContinue
if ($running) { $running | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
$args = @('/nologo','/codepage:65001','/target:winexe','/platform:anycpu','/optimize+',"/out:$out",
  '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$fw\WPF\UIAutomationClient.dll","/r:$fw\WPF\UIAutomationTypes.dll")
if (Test-Path $ico) { $args += "/win32icon:$ico" }
$args += (Get-ChildItem (Join-Path $d 'src') -Filter *.cs | ForEach-Object FullName)
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
if (-not $env:SESLIOKUMA_NOSTART) { Start-Process $out }
Write-Host "built: $out (icon: $(Test-Path $ico))"