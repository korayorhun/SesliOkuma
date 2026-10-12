# Builds SesliOkuma.exe from src\*.cs with the .NET Framework 4.8 C# compiler (C# 5 syntax).
$ErrorActionPreference = 'Stop'
$d = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$out = Join-Path $d 'SesliOkuma.exe'
$ico = Join-Path $d 'assets\SesliOkuma.ico'
$running = Get-Process SesliOkuma -ErrorAction SilentlyContinue
if ($running) { $running | Stop-Process -Force; Start-Sleep -Milliseconds 800 }
# Windows built-in OCR (WinRT): Windows.winmd + the WindowsRuntime bridge + the System.Runtime facade.
$winmd = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\UnionMetadata" -Directory -ErrorAction SilentlyContinue |
  Where-Object { $_.Name -match '^\d' } | Sort-Object { [Version]$_.Name } -Descending |
  ForEach-Object { Join-Path $_.FullName 'Windows.winmd' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $winmd) { throw 'Windows.winmd not found - install a Windows 10/11 SDK (needed for the built-in OCR).' }
$wrt = 'C:\Windows\Microsoft.NET\assembly\GAC_MSIL\System.Runtime.WindowsRuntime\v4.0_4.0.0.0__b77a5c561934e089\System.Runtime.WindowsRuntime.dll'
$facade = Get-ChildItem "${env:ProgramFiles(x86)}\Reference Assemblies\Microsoft\Framework\.NETFramework" -Directory -ErrorAction SilentlyContinue |
  Sort-Object Name -Descending | ForEach-Object { Join-Path $_.FullName 'Facades\System.Runtime.dll' } | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $facade) { throw 'System.Runtime facade not found - install the .NET Framework 4.8 targeting pack.' }
$args = @('/nologo','/codepage:65001','/target:winexe','/platform:anycpu','/optimize+',"/out:$out",
  '/r:System.Windows.Forms.dll','/r:System.Drawing.dll','/r:System.Web.Extensions.dll','/r:System.IO.Compression.dll','/r:System.IO.Compression.FileSystem.dll',"/r:$fw\WPF\UIAutomationClient.dll","/r:$fw\WPF\UIAutomationTypes.dll","/r:$fw\WPF\WindowsBase.dll","/r:$winmd","/r:$wrt","/r:$facade")
if (Test-Path $ico) { $args += "/win32icon:$ico" }
$args += (Get-ChildItem (Join-Path $d 'src') -Filter *.cs | ForEach-Object FullName)
& "$fw\csc.exe" @args
if ($LASTEXITCODE -ne 0) { throw "csc failed ($LASTEXITCODE)" }
if (-not $env:SESLIOKUMA_NOSTART) { Start-Process $out }
Write-Host "built: $out (icon: $(Test-Path $ico))"