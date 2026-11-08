[CmdletBinding()]
param(
    [ValidateSet('All', 'Backend', 'Frontend', 'Contracts', 'Packages')]
    [string]$Scope = 'All',
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot 'commands.ps1')

if ($Scope -in @('All', 'Backend')) {
    if (-not $SkipRestore) {
        Invoke-CheckedCommand 'dotnet' @('tool', 'restore')
        Invoke-CheckedCommand 'dotnet' @('restore', 'Workflow.slnx')
    }
    Invoke-CheckedCommand 'dotnet' @('build', 'Workflow.slnx', '--no-restore', '--configuration', 'Release')
    # Each project keeps its own concurrent tests; avoid cross-project contention of their bounded watchdogs.
    Invoke-CheckedCommand 'dotnet' @('test', 'Workflow.slnx', '--no-build', '--no-restore', '--configuration', 'Release', '--verbosity', 'minimal', '-m:1')
    Invoke-CheckedCommand 'dotnet' @('ef', 'migrations', 'has-pending-model-changes', '--project', 'samples/Approval.Certificate', '--startup-project', 'samples/Certificate.Api', '--configuration', 'Release', '--no-build')
    foreach ($provider in @('Sqlite', 'MySql', 'SqlServer', 'PostgreSql')) {
        Invoke-CheckedCommand 'dotnet' @('ef', 'migrations', 'has-pending-model-changes', '--project', 'samples/Approval.ReferenceHost', '--context', ($provider + 'ReferenceContext'), '--configuration', 'Release', '--no-build')
        Invoke-CheckedCommand 'dotnet' @('ef', 'migrations', 'has-pending-model-changes', '--project', 'tests/Approval.Database.Tests', '--context', ($provider + 'ContractContext'), '--configuration', 'Release', '--no-build')
    }
    Invoke-CheckedCommand 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-reference-host.ps1'), '-SkipBuild') $repositoryRoot

    $parseTokens = $null
    $parseErrors = $null
    $smokeScript = Join-Path $repositoryRoot 'samples/Certificate.Api/smoke-test.ps1'
    [System.Management.Automation.Language.Parser]::ParseFile($smokeScript, [ref]$parseTokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) {
        throw ($parseErrors -join [Environment]::NewLine)
    }
}

if ($Scope -in @('All', 'Frontend')) {
    $webRoot = Join-Path $repositoryRoot 'web'
    if (-not $SkipRestore) {
        Invoke-CheckedCommand 'pnpm' @('install', '--frozen-lockfile') $webRoot
    }
    Invoke-CheckedCommand 'pnpm' @('check:boundaries') $webRoot
    Invoke-CheckedCommand 'pnpm' @('check:ui:react') $webRoot
    Invoke-CheckedCommand 'pnpm' @('test:ui:react') $webRoot
    Invoke-CheckedCommand 'pnpm' @('test:boundaries') $webRoot
    Invoke-CheckedCommand 'pnpm' @('typecheck') $webRoot
    Invoke-CheckedCommand 'pnpm' @('test') $webRoot
    Invoke-CheckedCommand 'pnpm' @('build:lib') $webRoot
    Invoke-CheckedCommand 'pnpm' @('build:react-lib') $webRoot
    Invoke-CheckedCommand 'pnpm' @('build') $webRoot
    Invoke-CheckedCommand 'pnpm' @('build:react') $webRoot
}

if ($Scope -eq 'Contracts') {
    if (-not $SkipRestore) { Invoke-CheckedCommand 'pnpm' @('install', '--frozen-lockfile') (Join-Path $repositoryRoot 'web') }
    Invoke-CheckedCommand 'pnpm' @('contracts:check') (Join-Path $repositoryRoot 'web')
}
if ($Scope -eq 'All') {
    Invoke-CheckedCommand 'pnpm' @('contracts:check', '--no-build') (Join-Path $repositoryRoot 'web')
    Invoke-CheckedCommand 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-packages.ps1'), '-SkipBuild', '-RunBrowser') $repositoryRoot
    Invoke-CheckedCommand 'pnpm' @('verify:react-package', '--skip-build') (Join-Path $repositoryRoot 'web')
    Invoke-CheckedCommand 'pnpm' @('test:e2e:packed') (Join-Path $repositoryRoot 'web')
}
if ($Scope -eq 'Packages') {
    if (-not $SkipRestore) { Invoke-CheckedCommand 'pnpm' @('install', '--frozen-lockfile') (Join-Path $repositoryRoot 'web') }
    Invoke-CheckedCommand 'pwsh' @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'verify-packages.ps1'), '-RunBrowser') $repositoryRoot
    Invoke-CheckedCommand 'pnpm' @('verify:react-package') (Join-Path $repositoryRoot 'web')
}

Write-Host ('Verification succeeded: {0}' -f $Scope)
