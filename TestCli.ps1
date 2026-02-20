# TestCli.ps1
# Run with:
# Run Command: powershell -NoProfile -ExecutionPolicy Bypass -File .\TestCli.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\TestCli.ps1
# (or) pwsh -NoProfile -ExecutionPolicy Bypass -File .\TestCli.ps1

$ErrorActionPreference = "Stop"

# --- Config ---
$RepoRoot = Get-Location
$CliProject = "ChemVerify.Cli"

$InputDir = ".\ChemVerify.Tests\TestData\Input"
$Clean    = Join-Path $InputDir "CleanProcedure.txt"
$Mixed    = Join-Path $InputDir "MixedScenario.txt"
$Malformed= Join-Path $InputDir "MalformedUnderspecified.txt"

$OutDir = Join-Path $RepoRoot "out\cli-tests"

# --- Helpers ---
function Assert-FileExists([string]$path) {
  if (-not (Test-Path $path)) {
    throw "Missing file: $path"
  }
}

function Ensure-CleanDir([string]$path) {
  if (Test-Path $path) { Remove-Item -Recurse -Force $path }
  New-Item -ItemType Directory -Force -Path $path | Out-Null
}

function Run-Case(
  [string]$name,
  [string]$path,
  [string]$profile,
  [string]$format,
  [int]$expectedExit,
  [string]$outFile
) {
  Write-Host "== $name =="

  Assert-FileExists $path

  $args = @(
    "run", "--project", $CliProject, "--",
    "analyze", $path,
    "--profile", $profile,
    "--format", $format,
    "--out", $outFile
  )

  & dotnet @args | Out-Null
  $code = $LASTEXITCODE

  if ($code -ne $expectedExit) {
    throw "Expected exit $expectedExit, got $code for $name"
  }

  if (-not (Test-Path $outFile)) {
    throw "Expected output file not created: $outFile"
  }

  Write-Host "PASS ($code)"
}

function Assert-JsonHasKeys([string]$path, [string[]]$keys) {
  $raw = Get-Content $path -Raw
  $obj = $raw | ConvertFrom-Json

  foreach ($k in $keys) {
    if (-not ($obj.PSObject.Properties.Name -contains $k)) {
      throw "JSON missing key '$k' in $path"
    }
  }
}

function Assert-SarifLooksValid([string]$path) {
  $raw = Get-Content $path -Raw
  $obj = $raw | ConvertFrom-Json

  if ($obj.version -ne "2.1.0") {
    throw "SARIF version expected 2.1.0, got '$($obj.version)'"
  }
  if (-not $obj.runs -or $obj.runs.Count -lt 1) {
    throw "SARIF missing runs[]"
  }
  $run0 = $obj.runs[0]
  if (-not $run0.tool -or -not $run0.tool.driver) {
    throw "SARIF missing tool.driver"
  }
  if (-not $run0.results -or $run0.results.Count -lt 1) {
    throw "SARIF missing results[]"
  }
  $first = $run0.results[0]
  if (-not $first.ruleId) { throw "SARIF first result missing ruleId" }
  if (-not $first.message -or -not $first.message.text) { throw "SARIF first result missing message.text" }
}

function Assert-FilesIdentical([string]$a, [string]$b) {
  $ha = (Get-FileHash $a -Algorithm SHA256).Hash
  $hb = (Get-FileHash $b -Algorithm SHA256).Hash
  if ($ha -ne $hb) {
    throw "Files differ (SHA256 mismatch):`n  $a`n  $b`nTip: if this is due to runId/timestamps, add --deterministic and re-run."
  }
}

# --- Start ---
Write-Host "ChemVerify CLI Full Test Suite"
Write-Host "Repo: $RepoRoot"
Write-Host ""

Ensure-CleanDir $OutDir

# 0) Basic file existence
Assert-FileExists $Clean
Assert-FileExists $Mixed
Assert-FileExists $Malformed

# 1) Low → exit 0 + JSON shape
$lowJson = Join-Path $OutDir "low.json"
Run-Case -name "Low/CleanProcedure (json)" -path $Clean -profile "Default" -format "json" -expectedExit 0 -outFile $lowJson
Assert-JsonHasKeys -path $lowJson -keys @("engineVersion","ruleSetVersion","severity","verdict","summary","riskDrivers")

# 2) Medium → exit 1 + JSON shape
$medJson = Join-Path $OutDir "medium.json"
Run-Case -name "Medium/MixedScenario (json)" -path $Mixed -profile "Default" -format "json" -expectedExit 1 -outFile $medJson
Assert-JsonHasKeys -path $medJson -keys @("engineVersion","ruleSetVersion","severity","verdict","summary","riskDrivers")

# 3) High → exit 2 + JSON shape
$highJson = Join-Path $OutDir "high.json"
Run-Case -name "High/MalformedUnderspecified (json)" -path $Malformed -profile "Default" -format "json" -expectedExit 2 -outFile $highJson
Assert-JsonHasKeys -path $highJson -keys @("engineVersion","ruleSetVersion","severity","verdict","attention","riskDrivers")

# 4) SARIF (use High case) → exit 2 + SARIF structure
$highSarif = Join-Path $OutDir "high.sarif"
Run-Case -name "High/MalformedUnderspecified (sarif)" -path $Malformed -profile "Default" -format "sarif" -expectedExit 2 -outFile $highSarif
Assert-SarifLooksValid -path $highSarif

# 5) Determinism (CleanProcedure twice) — may fail if runId/timestamps are present
$detA = Join-Path $OutDir "determinism.a.json"
$detB = Join-Path $OutDir "determinism.b.json"
Run-Case -name "Determinism pass 1 (json)" -path $Clean -profile "Default" -format "json" -expectedExit 0 -outFile $detA
Run-Case -name "Determinism pass 2 (json)" -path $Clean -profile "Default" -format "json" -expectedExit 0 -outFile $detB
Assert-FilesIdentical -a $detA -b $detB

Write-Host ""
Write-Host "All CLI tests passed. Output artifacts are in: $OutDir"