# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 diazneoones82
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot ('test-results\' + [Guid]::NewGuid().ToString('N'))
$previousProfile = $env:EVACOPY_PROFILE
try {
    $env:EVACOPY_PROFILE = Join-Path $testRoot 'jobs'
    $test = Start-Process -FilePath (Join-Path $PSScriptRoot 'dist\release\Evacopy.exe') -ArgumentList '--integration-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) {
        $errorFile = Join-Path $testRoot 'ERROR.txt'
        if (Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
        throw "Integration test failed; inspect $testRoot"
    }
    Get-Content -LiteralPath (Join-Path $testRoot 'RESULT.txt')
    Write-Output "Test evidence: $testRoot"
} finally {
    if ($null -eq $previousProfile) { Remove-Item Env:\EVACOPY_PROFILE -ErrorAction SilentlyContinue }
    else { $env:EVACOPY_PROFILE = $previousProfile }
}
