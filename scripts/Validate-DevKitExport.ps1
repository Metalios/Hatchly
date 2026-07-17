[CmdletBinding()]
param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$dataDirectory = Join-Path $RepositoryRoot 'src/Hatchly.App/wwwroot/data'
$toolProject = Join-Path $RepositoryRoot 'tools/Hatchly.Tools'
$solution = Join-Path $RepositoryRoot 'Hatchly.slnx'
$reportPath = Join-Path $dataDirectory 'devkit-export-report.json'

function Invoke-Checked {
    param([scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE."
    }
}

Push-Location $RepositoryRoot
try {
    Invoke-Checked { python -m unittest discover -s devkit/tests -v }
    Invoke-Checked { dotnet run --project $toolProject -- validate-data --data-dir $dataDirectory }
    Invoke-Checked {
        dotnet run --project $toolProject -- merge-data --data-dir $dataDirectory --output (Join-Path $dataDirectory 'catalog.json')
    }
    Invoke-Checked { dotnet test $solution -c Release }

    if (Test-Path $reportPath) {
        $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
        if ($report.blockingErrors.Count -gt 0) {
            throw "The DevKit report contains blocking errors: $($report.blockingErrors -join '; ')"
        }

        Write-Host "Semantic export summary: $($report.newCreatures.Count) new, $($report.changedCreatures.Count) changed, $($report.missingCreatures.Count) missing creatures; $($report.newFoods.Count) new and $($report.changedFoods.Count) changed foods."
    }
    else {
        Write-Warning 'No devkit-export-report.json exists; run the DevKit exporter before final acceptance.'
    }

    Invoke-Checked {
        git diff --check -- src/Hatchly.App/wwwroot/data devkit tests/Hatchly.Core.Tests
    }
}
finally {
    Pop-Location
}
