param(
    [ValidateRange(0, 100)]
    [double]$MinimumLineCoverage = 95
)

$repoRoot = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $repoRoot 'tests/AccessControl.Tests/AccessControl.Tests.csproj'
$settings = Join-Path $repoRoot 'coverage.runsettings'
$results = Join-Path $repoRoot ('artifacts/coverage/' + [guid]::NewGuid().ToString('N'))

dotnet test $testProject -c Release --collect:'XPlat Code Coverage' --settings $settings --results-directory $results --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$report = Get-ChildItem -LiteralPath $results -Recurse -File -Filter 'coverage.cobertura.xml' |
    Select-Object -First 1
if ($null -eq $report) { throw 'Cobertura coverage report was not generated.' }

[xml]$coverage = Get-Content -LiteralPath $report.FullName
$covered = [int]$coverage.coverage.GetAttribute('lines-covered')
$valid = [int]$coverage.coverage.GetAttribute('lines-valid')
if ($valid -eq 0) { throw 'Coverage report contains no instrumented lines.' }

$percent = [math]::Round(100.0 * $covered / $valid, 2)
Write-Host "Handwritten production line coverage: $covered / $valid ($percent%)"
foreach ($package in $coverage.coverage.packages.package) {
    $packagePercent = [math]::Round(100.0 * [double]::Parse(
        $package.GetAttribute('line-rate'), [System.Globalization.CultureInfo]::InvariantCulture), 2)
    Write-Host "  $($package.GetAttribute('name')): $packagePercent%"
}
Write-Host "Report: $($report.FullName)"
if ($percent -lt $MinimumLineCoverage) {
    throw "Line coverage $percent% is below the required $MinimumLineCoverage%."
}
