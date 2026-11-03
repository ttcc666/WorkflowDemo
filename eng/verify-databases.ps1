[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('MySql', 'SqlServer', 'PostgreSql')][string]$Provider,
    [switch]$UseExistingDatabase,
    [switch]$SkipRestore,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'commands.ps1')
$runId = [Guid]::NewGuid().ToString('N')
$databaseName = 'approval_test_' + $runId
$containerName = 'approval-test-' + $runId
$reportDirectory = Join-Path $repositoryRoot ('output/database-tests/{0}/{1}' -f $Provider, $runId)
New-Item -ItemType Directory -Force -Path $reportDirectory | Out-Null
$variableNames = @('APPROVAL_TEST_PROVIDER', 'APPROVAL_TEST_CONNECTION', 'APPROVAL_TEST_REPORT_DIRECTORY',
    'MYSQL_ROOT_PASSWORD', 'MYSQL_DATABASE', 'MSSQL_SA_PASSWORD', 'SQLCMDPASSWORD', 'POSTGRES_PASSWORD', 'POSTGRES_DB')
$savedEnvironment = @{}
foreach ($variableName in $variableNames) { $savedEnvironment[$variableName] = [Environment]::GetEnvironmentVariable($variableName) }
$imageReference = $null
$containerCreated = $false
$testExitCode = $null
$validated = $false

function Invoke-Docker {
    param([string[]]$Arguments)
    $result = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Docker operation failed: {0}' -f $Arguments[0]) }
    return $result
}

try {
    $env:APPROVAL_TEST_PROVIDER = $Provider
    $env:APPROVAL_TEST_REPORT_DIRECTORY = $reportDirectory
    if ($UseExistingDatabase) {
        if ([string]::IsNullOrWhiteSpace($env:APPROVAL_TEST_CONNECTION)) { throw 'Set APPROVAL_TEST_CONNECTION for a dedicated disposable database.' }
        $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
        $builder.ConnectionString = $env:APPROVAL_TEST_CONNECTION
        $targetDatabase = if ($builder.ContainsKey('Database')) { [string]$builder['Database'] } elseif ($builder.ContainsKey('Initial Catalog')) { [string]$builder['Initial Catalog'] } else { '' }
        if ($targetDatabase -notmatch '^approval_test_[a-zA-Z0-9_]+$') { throw 'Existing test database must have an explicit approval_test_ name. Migration roundtrip tests erase its approval tables.' }
    }
    else {
        $null = Invoke-Docker @('info', '--format', '{{.ServerVersion}}')
        $images = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'database-images.json') -Raw | ConvertFrom-Json
        $imageReference = $images.$Provider
        $null = & docker image inspect $imageReference 2>$null
        if ($LASTEXITCODE -ne 0) { $null = Invoke-Docker @('pull', $imageReference) }
        $password = 'Approval_' + [Guid]::NewGuid().ToString('N') + '!9a'
        $dockerArguments = @('run', '--detach', '--name', $containerName, '--label', ('approval.test-run=' + $runId))
        switch ($Provider) {
            'MySql' {
                $env:MYSQL_ROOT_PASSWORD = $password; $env:MYSQL_DATABASE = $databaseName
                $port = 3306
                $dockerArguments += @('--env', 'MYSQL_ROOT_PASSWORD', '--env', 'MYSQL_DATABASE')
                $probe = @('exec', $containerName, 'sh', '-c', 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot --protocol=TCP --host=127.0.0.1 --ssl-mode=REQUIRED --execute="SELECT 1"')
            }
            'SqlServer' {
                $env:MSSQL_SA_PASSWORD = $password; $env:SQLCMDPASSWORD = $password
                $port = 1433
                $dockerArguments += @('--env', 'ACCEPT_EULA=Y', '--env', 'MSSQL_PID=Developer', '--env', 'MSSQL_SA_PASSWORD', '--env', 'SQLCMDPASSWORD')
                $probe = @('exec', $containerName, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-Q', 'SELECT 1', '-b')
            }
            'PostgreSql' {
                $env:POSTGRES_PASSWORD = $password; $env:POSTGRES_DB = $databaseName
                $port = 5432
                $dockerArguments += @('--env', 'POSTGRES_PASSWORD', '--env', 'POSTGRES_DB')
                $probe = @('exec', $containerName, 'pg_isready', '-h', '127.0.0.1', '-p', '5432', '-U', 'postgres', '-d', $databaseName)
            }
        }
        $dockerArguments += @('--publish', ('127.0.0.1::{0}' -f $port), $imageReference)
        if ($Provider -eq 'MySql') { $dockerArguments += @('--character-set-server=utf8mb4', '--collation-server=utf8mb4_0900_bin') }
        $null = Invoke-Docker $dockerArguments
        $containerCreated = $true
        $ready = $false
        for ($attempt = 0; $attempt -lt 120; $attempt++) {
            $null = & docker @probe 2>$null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            if ($attempt % 15 -eq 0) { Write-Host ('Waiting for {0} test database ({1}/120)...' -f $Provider, $attempt) }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw ('Test database readiness failed: ' + $Provider) }
        $binding = Invoke-Docker @('port', $containerName, ('{0}/tcp' -f $port))
        $hostPort = ([string]$binding).Trim().Split(':')[-1]
        $env:APPROVAL_TEST_CONNECTION = switch ($Provider) {
            'MySql' { 'Server=127.0.0.1;Port={0};Database={1};User ID=root;Password={2};SslMode=Required;Character Set=utf8mb4;Connection Timeout=10' -f $hostPort, $databaseName, $password }
            'SqlServer' { 'Server=127.0.0.1,{0};Database={1};User ID=sa;Password={2};Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=10' -f $hostPort, $databaseName, $password }
            'PostgreSql' { 'Host=127.0.0.1;Port={0};Database={1};Username=postgres;Password={2};Timeout=10' -f $hostPort, $databaseName, $password }
        }
    }
    $testProject = 'tests/Approval.Database.Tests/Approval.Database.Tests.csproj'
    if (-not $SkipRestore) { Invoke-CheckedCommand 'dotnet' @('restore', $testProject, '--locked-mode') $repositoryRoot }
    if (-not $SkipBuild) { Invoke-CheckedCommand 'dotnet' @('build', $testProject, '--configuration', 'Release', '--no-restore') $repositoryRoot }
    Push-Location -LiteralPath $repositoryRoot
    try {
        & dotnet test $testProject '--configuration' 'Release' '--no-build' '--no-restore' '--verbosity' 'minimal' '--logger' 'trx;LogFileName=database.trx' '--results-directory' $reportDirectory
        $testExitCode = $LASTEXITCODE
    }
    finally { Pop-Location }
    if ($testExitCode -ne 0) { throw ('Database test command failed with exit code ' + $testExitCode) }
    [xml]$trx = Get-Content -LiteralPath (Join-Path $reportDirectory 'database.trx') -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 33 -or [int]$counters.passed -ne [int]$counters.total) { throw 'Tests were absent, skipped or unsuccessful.' }
    $validated = $true
    Write-Host ('Database verification succeeded: {0}, Passed: {1}, Failed: {2}, Skipped: {3}' -f $Provider, $counters.passed, $counters.failed, $counters.notExecuted)
}
finally {
    try {
        $manifest = [ordered]@{
            runId = $runId; provider = $Provider; image = $imageReference; testExitCode = $testExitCode; validated = $validated
            head = (& git -C $repositoryRoot rev-parse HEAD)
            artifacts = @(Get-ChildItem -LiteralPath $reportDirectory -File | Get-FileHash -Algorithm SHA256 | Select-Object @{Name='name';Expression={Split-Path -Leaf $_.Path}}, Hash)
            packageLockHash = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'tests/Approval.Database.Tests/packages.lock.json') -Algorithm SHA256).Hash
        }
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $reportDirectory 'manifest.json')
    }
    finally {
        try {
            if ($containerCreated) {
                $owner = Invoke-Docker @('inspect', '--format', '{{index .Config.Labels "approval.test-run"}}', $containerName)
                if ([string]$owner -ne $runId) { throw 'Refusing to remove a container not owned by this test run.' }
                $null = Invoke-Docker @('rm', '--force', '--volumes', $containerName)
            }
        }
        finally {
            foreach ($variableName in $variableNames) { [Environment]::SetEnvironmentVariable($variableName, $savedEnvironment[$variableName]) }
            Write-Host ('Database artifacts: ' + $reportDirectory)
        }
    }
}
