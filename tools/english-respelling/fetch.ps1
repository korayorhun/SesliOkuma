# Downloads the sources make.py needs into cache\ (not committed): CMUdict (BSD-style) and zemberek-nlp's
# Turkish dictionaries (Apache-2.0). Run once, then: py -3 make.py
$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot 'cache'
New-Item -ItemType Directory -Force $cache | Out-Null
$zemberek = 'https://raw.githubusercontent.com/ahmetaa/zemberek-nlp/master/morphology/src/main/resources/tr/'
$files = @{
    'cmudict.dict'          = 'https://raw.githubusercontent.com/cmusphinx/cmudict/master/cmudict.dict'
    'LICENSE-cmudict'       = 'https://raw.githubusercontent.com/cmusphinx/cmudict/master/LICENSE'
    'master-dictionary.dict' = $zemberek + 'master-dictionary.dict'
    'non-tdk.dict'          = $zemberek + 'non-tdk.dict'
    'proper.dict'           = $zemberek + 'proper.dict'
    'person-names.dict'     = $zemberek + 'person-names.dict'
    'locations-tr.dict'     = $zemberek + 'locations-tr.dict'
    'abbreviations.dict'    = $zemberek + 'abbreviations.dict'
}
foreach ($name in $files.Keys) {
    Invoke-WebRequest $files[$name] -OutFile (Join-Path $cache $name) -UseBasicParsing
    Write-Host "$name  $([Math]::Round((Get-Item (Join-Path $cache $name)).Length / 1KB)) KB"
}
