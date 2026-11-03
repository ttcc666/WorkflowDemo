[CmdletBinding()]
param([switch]$SkipBuild, [string]$SamplePath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'commands.ps1')
$sample = if ($SamplePath) { (Resolve-Path -LiteralPath $SamplePath).Path } else { Join-Path $repositoryRoot 'samples/Approval.MultiTenant.Api' }
if (-not $SkipBuild) { Invoke-CheckedCommand 'dotnet' @('build', (Join-Path $sample 'Approval.MultiTenant.Api.csproj'), '--configuration', 'Release') }
$runId = [Guid]::NewGuid().ToString('N')
$report = Join-Path $repositoryRoot ('output/reference-host/' + $runId)
New-Item -ItemType Directory -Path $report | Out-Null
$saved = @{}
$process = $null
function Set-ReferenceEnvironment([string]$Name, [string]$Value) {
    if (-not $saved.ContainsKey($Name)) { $saved[$Name] = [Environment]::GetEnvironmentVariable($Name) }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}
try {
    Set-ReferenceEnvironment 'ASPNETCORE_ENVIRONMENT' 'Development'
    Set-ReferenceEnvironment 'Authentication__Mode' 'Demo'
    Set-ReferenceEnvironment 'Database__Provider' 'Sqlite'
    Set-ReferenceEnvironment 'ConnectionStrings__Approval' ('Data Source=' + (Join-Path $report 'reference.db'))
    Set-ReferenceEnvironment 'Approval__FileRoot' (Join-Path $report 'files')
    Set-ReferenceEnvironment 'Approval__EnableBackgroundServices' 'false'
    $index = 0
    foreach ($tenant in @('alpha', 'beta')) {
        foreach ($user in @('admin', 'alice', 'bob', 'carol')) {
            $token = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
            Set-ReferenceEnvironment ('Authentication__DemoIdentities__{0}__TenantId' -f $index) $tenant
            Set-ReferenceEnvironment ('Authentication__DemoIdentities__{0}__UserId' -f $index) $user
            Set-ReferenceEnvironment ('Authentication__DemoIdentities__{0}__Token' -f $index) $token
            Set-ReferenceEnvironment ('APPROVAL_DEMO_{0}_{1}' -f $tenant, $user).ToUpperInvariant() $token
            $index++
        }
    }
    $dll = Join-Path $sample 'bin/Release/net10.0/Approval.MultiTenant.Api.dll'
    Invoke-CheckedCommand 'dotnet' @($dll, '--migrate') $sample
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $url = 'http://127.0.0.1:' + $port
    Set-ReferenceEnvironment 'ASPNETCORE_URLS' $url
    $start = @{ FilePath = 'dotnet'; ArgumentList = @('"' + $dll + '"'); WorkingDirectory = $sample; PassThru = $true
        RedirectStandardOutput = (Join-Path $report 'host.log'); RedirectStandardError = (Join-Path $report 'host-error.log') }
    if ($IsWindows) { $start.WindowStyle = 'Hidden' }
    $process = Start-Process @start
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(60)
    $ready = $false
    do {
        if ($process.HasExited) { throw 'Reference host exited before readiness. Inspect host-error.log.' }
        try { $ready = (Invoke-RestMethod -Uri ($url + '/health/approval') -TimeoutSec 2).ready } catch { }
        if (-not $ready) { Start-Sleep -Milliseconds 200 }
    } while (-not $ready -and [DateTimeOffset]::UtcNow -lt $deadline)
    if (-not $ready) { throw 'Reference host readiness timed out.' }
    Invoke-CheckedCommand 'pwsh' @('-NoProfile', '-File', (Join-Path $sample 'smoke-test.ps1'), '-Base', $url, '-ReportPath', (Join-Path $report 'smoke.json')) $sample
    $evidence = Get-Content -LiteralPath (Join-Path $report 'smoke.json') -Raw | ConvertFrom-Json
    if (-not $evidence.passed -or $evidence.checks -lt 30) { throw 'Missing reference acceptance evidence.' }
    Write-Host ('Reference host verified: ' + $report)
}
finally {
    if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit(10000) | Out-Null }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
}
