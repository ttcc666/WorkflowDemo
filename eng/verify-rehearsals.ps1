[CmdletBinding()]
param([ValidateSet('All', 'MySql', 'SqlServer', 'PostgreSql')][string]$Provider = 'All', [switch]$SkipBuild)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'commands.ps1')
if (-not $IsWindows) { throw 'This rehearsal entry currently supports Windows x64 and Linux Docker containers.' }
if (-not $SkipBuild) { Invoke-CheckedCommand 'dotnet' @('build', 'eng/Approval.RehearsalTool/Approval.RehearsalTool.csproj', '--configuration', 'Release') $repositoryRoot }
$rehearsalSdk = (& dotnet --version).Trim()
if ($LASTEXITCODE -ne 0) { throw 'Cannot identify the rehearsal SDK.' }
$rehearsalToolHash = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'eng/Approval.RehearsalTool/bin/Release/net10.0/Approval.RehearsalTool.dll')).Hash
$rehearsalLockHash = (Get-FileHash -LiteralPath (Join-Path $repositoryRoot 'eng/Approval.RehearsalTool/packages.lock.json')).Hash
$runId = [Guid]::NewGuid().ToString('N')
$reportRoot = Join-Path $repositoryRoot ('output/rehearsals/' + $runId)
$scratchRoot = Join-Path $repositoryRoot ('.git/testagent/rehearsals/' + $runId)
New-Item -ItemType Directory -Path $reportRoot, $scratchRoot | Out-Null
$images = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'database-images.json') -Raw | ConvertFrom-Json
$services = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'rehearsal-images.json') -Raw | ConvertFrom-Json
$saved = @{}
$containers = [Collections.Generic.List[string]]::new()
$proxyProcess = $null
$passed = $false
$results = [Collections.Generic.List[object]]::new()
$keycloak = 'approval-rehearsal-identity-' + $runId
$realm = 'approval-' + $runId

function Set-RehearsalEnvironment([string]$Name, [string]$Value) {
    if (-not $saved.ContainsKey($Name)) { $saved[$Name] = [Environment]::GetEnvironmentVariable($Name) }
    [Environment]::SetEnvironmentVariable($Name, $Value)
}
function Invoke-RehearsalDocker([string[]]$Arguments) {
    $result = & docker @Arguments
    if ($LASTEXITCODE -ne 0) { throw ('Docker rehearsal failed: ' + $Arguments[0]) }
    return $result
}
function Free-RehearsalPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start(); $port = $listener.LocalEndpoint.Port; $listener.Stop(); return $port
}
function Invoke-RehearsalStage([string]$Stage, [string]$Directory) {
    Write-Host ('Rehearsal stage: {0} / {1}' -f $env:APPROVAL_REHEARSAL_PROVIDER, $Stage)
    $log = Join-Path $Directory ($Stage + '.log')
    & dotnet (Join-Path $repositoryRoot 'eng/Approval.RehearsalTool/bin/Release/net10.0/Approval.RehearsalTool.dll') $Stage *> $log
    if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath $log -Tail 8; throw ('Rehearsal stage failed: ' + $Stage) }
    $result = Get-Content -LiteralPath (Join-Path $Directory ($Stage + '.json')) -Raw | ConvertFrom-Json
    if (-not $result.passed -or $result.checks.Count -eq 0 -or @($result.checks | Where-Object { -not $_.passed }).Count -gt 0) { throw 'Incomplete stage evidence' }
    Get-Content -LiteralPath $log -Tail 1
}

try {
    $null = Invoke-RehearsalDocker @('info', '--format', '{{.ServerVersion}}')
    $exe = Join-Path $repositoryRoot '.git/testagent/rehearsals/tools/toxiproxy-server-windows-amd64.exe'
    if (-not (Test-Path -LiteralPath $exe)) {
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $exe) | Out-Null
        Invoke-WebRequest -Uri ('https://github.com/Shopify/toxiproxy/releases/download/v' + $services.ToxiproxyVersion + '/toxiproxy-server-windows-amd64.exe') -OutFile $exe
    }
    if ((Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant() -cne $services.ToxiproxyWindowsSha256) { throw 'Toxiproxy binary SHA256 mismatch.' }
    $identityPort = Free-RehearsalPort
    $realm = 'approval-' + $runId
    $authority = 'https://localhost:' + $identityPort + '/realms/' + $realm
    $password = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
    $rsa = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=localhost', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $san = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $san.AddDnsName('localhost'); $san.AddIpAddress([Net.IPAddress]::Loopback)
    $request.CertificateExtensions.Add($san.Build())
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true))
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5), [DateTimeOffset]::UtcNow.AddDays(1))
    $certificate.ExportCertificatePem() | Set-Content -LiteralPath (Join-Path $scratchRoot 'server.pem') -Encoding ascii
    $rsa.ExportPkcs8PrivateKeyPem() | Set-Content -LiteralPath (Join-Path $scratchRoot 'server.key') -Encoding ascii
    Copy-Item -LiteralPath (Join-Path $scratchRoot 'server.pem') -Destination (Join-Path $reportRoot 'identity-certificate.pem')
    $certificate.Dispose(); $rsa.Dispose()
    $config = Get-Content -LiteralPath (Join-Path $repositoryRoot 'samples/Approval.MultiTenant.Api/appsettings.json') -Raw | ConvertFrom-Json
    $config.PSObject.Properties.Remove('ConnectionStrings')
    $config.PSObject.Properties.Remove('Authentication')
    $users = @()
    foreach ($tenant in $config.Directory.Tenants) {
        foreach ($user in $tenant.Users) {
            $user.Id = $tenant.Id + '-' + $user.Id
            $users += @{ id = $user.Id; username = $user.Id; enabled = $true; email = $user.Id + '@example.invalid'; emailVerified = $true; firstName = 'Rehearsal'; lastName = 'User'; attributes = @{ tenant_id = @($tenant.Id) }; credentials = @(@{ type = 'password'; value = $password; temporary = $false }) }
        }
    }
    $users += @{ id = 'outsider'; username = 'outsider'; enabled = $true; email = 'outsider@example.invalid'; emailVerified = $true; firstName = 'Rehearsal'; lastName = 'Outsider'; attributes = @{ tenant_id = @('alpha') }; credentials = @(@{ type = 'password'; value = $password; temporary = $false }) }
    $configPath = Join-Path $reportRoot 'host-config.json'
    $config | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $configPath -Encoding utf8
    $tenantMapper = @{ name = 'tenant'; protocol = 'openid-connect'; protocolMapper = 'oidc-usermodel-attribute-mapper'; config = @{ 'user.attribute' = 'tenant_id'; 'claim.name' = 'tenant_id'; 'jsonType.label' = 'String'; 'access.token.claim' = 'true'; 'id.token.claim' = 'true'; 'multivalued' = 'false' } }
    $clients = foreach ($client in @('approval-api', 'other-api', 'short-lived')) {
        $audience = if ($client -eq 'other-api') { 'other-api' } else { 'approval-api' }
        @{ clientId = $client; enabled = $true; publicClient = $true; directAccessGrantsEnabled = $true; standardFlowEnabled = $false; defaultClientScopes = @('basic'); optionalClientScopes = @(); attributes = @{ 'access.token.lifespan' = $(if ($client -eq 'short-lived') { '2' } else { '3600' }) }; protocolMappers = @($tenantMapper, @{ name = 'audience'; protocol = 'openid-connect'; protocolMapper = 'oidc-audience-mapper'; config = @{ 'included.custom.audience' = $audience; 'access.token.claim' = 'true'; 'id.token.claim' = 'false' } }) }
    }
    $realmConfig = @{ realm = $realm; enabled = $true; sslRequired = 'all'; accessTokenLifespan = 3600; clients = @($clients); users = $users }
    $realmConfig | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $scratchRoot ($realm + '-realm.json')) -Encoding utf8
    $other = ($realmConfig | ConvertTo-Json -Depth 30 | ConvertFrom-Json)
    $other.realm = $realm + '-other'
    foreach ($user in $other.users) { $user.id = 'other-' + $user.id }
    $other | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $scratchRoot ($realm + '-other-realm.json')) -Encoding utf8
    $null = & docker image inspect $services.Keycloak 2>$null
    if ($LASTEXITCODE -ne 0) { $null = Invoke-RehearsalDocker @('pull', $services.Keycloak) }
    $null = Invoke-RehearsalDocker @('run', '--detach', '--name', $keycloak, '--label', ('approval.rehearsal=' + $runId), '--memory', '768m', '--publish', ('127.0.0.1:{0}:8443' -f $identityPort), '--mount', ('type=bind,source=' + $scratchRoot + ',target=/opt/keycloak/data/import,readonly'), $services.Keycloak, 'start', '--db=dev-file', '--http-enabled=false', '--https-certificate-file=/opt/keycloak/data/import/server.pem', '--https-certificate-key-file=/opt/keycloak/data/import/server.key', ('--hostname=https://localhost:' + $identityPort), '--import-realm')
    $containers.Add($keycloak)
    $identityReady = $false
    for ($attempt = 0; $attempt -lt 120; $attempt++) {
        try { $discovery = Invoke-RestMethod -Uri ($authority + '/.well-known/openid-configuration') -SkipCertificateCheck -TimeoutSec 3; if ($discovery.issuer -ceq $authority) { $identityReady = $true; break } } catch { }
        if ($attempt % 15 -eq 0) { Write-Host ('Waiting for HTTPS Keycloak ({0}/120)...' -f $attempt) }
        Start-Sleep -Seconds 1
    }
    if (-not $identityReady) { throw 'HTTPS Keycloak readiness failed.' }
    Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_AUTHORITY' $authority
    Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_CERTIFICATE' (Join-Path $reportRoot 'identity-certificate.pem')
    Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_PASSWORD' $password
    Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_CONFIG' $configPath
    $providers = if ($Provider -eq 'All') { @('MySql', 'SqlServer', 'PostgreSql') } else { @($Provider) }
    foreach ($current in $providers) {
        $dbId = [Guid]::NewGuid().ToString('N'); $databaseName = 'approval_test_' + $dbId; $restoreName = $databaseName + '_restore'
        $db = 'approval-rehearsal-db-' + $dbId
        $directory = Join-Path $reportRoot $current; New-Item -ItemType Directory -Path $directory | Out-Null
        $dbPassword = 'Approval_' + [Guid]::NewGuid().ToString('N') + '!9a'
        $dbArguments = @('run', '--detach', '--name', $db, '--label', ('approval.rehearsal=' + $runId))
        switch ($current) {
            'MySql' { $port = 3306; Set-RehearsalEnvironment 'MYSQL_ROOT_PASSWORD' $dbPassword; Set-RehearsalEnvironment 'MYSQL_DATABASE' $databaseName; $dbArguments += @('--env', 'MYSQL_ROOT_PASSWORD', '--env', 'MYSQL_DATABASE'); $probe = @('exec', $db, 'sh', '-c', 'MYSQL_PWD="$MYSQL_ROOT_PASSWORD" mysql -uroot --protocol=TCP --host=127.0.0.1 --ssl-mode=REQUIRED --execute="SELECT 1"') }
            'SqlServer' { $port = 1433; Set-RehearsalEnvironment 'MSSQL_SA_PASSWORD' $dbPassword; Set-RehearsalEnvironment 'SQLCMDPASSWORD' $dbPassword; $dbArguments += @('--env', 'ACCEPT_EULA=Y', '--env', 'MSSQL_PID=Developer', '--env', 'MSSQL_SA_PASSWORD', '--env', 'SQLCMDPASSWORD'); $probe = @('exec', $db, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-Q', 'SELECT 1', '-b') }
            'PostgreSql' { $port = 5432; Set-RehearsalEnvironment 'POSTGRES_PASSWORD' $dbPassword; Set-RehearsalEnvironment 'POSTGRES_DB' $databaseName; $dbArguments += @('--env', 'POSTGRES_PASSWORD', '--env', 'POSTGRES_DB'); $probe = @('exec', $db, 'sh', '-c', 'PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 -p 5432 -U postgres -d "$POSTGRES_DB" -c "SELECT 1"') }
        }
        $null = & docker image inspect $images.$current 2>$null
        if ($LASTEXITCODE -ne 0) { $null = Invoke-RehearsalDocker @('pull', $images.$current) }
        $dbArguments += @('--publish', ('127.0.0.1::{0}' -f $port), $images.$current)
        if ($current -eq 'MySql') { $dbArguments += @('--character-set-server=utf8mb4', '--collation-server=utf8mb4_0900_bin') }
        $null = Invoke-RehearsalDocker $dbArguments; $containers.Add($db)
        $ready = $false
        for ($attempt = 0; $attempt -lt 120; $attempt++) {
            $null = & docker @probe 2>$null
            if ($LASTEXITCODE -eq 0) { $ready = $true; break }
            if ($attempt % 15 -eq 0) { Write-Host ('Waiting for {0} ({1}/120)...' -f $current, $attempt) }
            Start-Sleep -Seconds 1
        }
        if (-not $ready) { throw ('Database readiness failed: ' + $current) }
        $binding = Invoke-RehearsalDocker @('port', $db, ('{0}/tcp' -f $port)); $hostPort = ([string]$binding).Trim().Split(':')[-1]
        $source = switch ($current) {
            'MySql' { 'Server=127.0.0.1;Port={0};Database={1};User ID=root;Password={2};SslMode=Required;Character Set=utf8mb4;Connection Timeout=3;Default Command Timeout=5' -f $hostPort, $databaseName, $dbPassword }
            'SqlServer' { 'Server=127.0.0.1,{0};Database={1};User ID=sa;Password={2};Encrypt=True;TrustServerCertificate=True;MultipleActiveResultSets=False;Connect Timeout=3;Command Timeout=5' -f $hostPort, $databaseName, $dbPassword }
            'PostgreSql' { 'Host=127.0.0.1;Port={0};Database={1};Username=postgres;Password={2};Timeout=3;Command Timeout=5' -f $hostPort, $databaseName, $dbPassword }
        }
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_PROVIDER' $current
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_SOURCE_CONNECTION' $source
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_RESTORE_CONNECTION' $source.Replace($databaseName, $restoreName)
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_REPORT' $directory
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_FILES' (Join-Path $directory 'source-files')
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_RESTORE_FILES' (Join-Path $directory 'restored-files')
        Invoke-RehearsalStage 'seed' $directory
        Invoke-RehearsalStage 'auth' $directory
        $nativeLog = Join-Path $directory 'native-backup-restore.log'
        Write-Host ('Native backup / new database restore: ' + $current)
        switch ($current) {
            'MySql' {
                $null = Invoke-RehearsalDocker @('exec', $db, 'sh', '-c', 'set -e; export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; mysqldump -uroot --single-transaction --routines --triggers --events --hex-blob --default-character-set=utf8mb4 --no-tablespaces --set-gtid-purged=OFF "$MYSQL_DATABASE" > /tmp/rehearsal.sql')
                Invoke-RehearsalDocker @('exec', $db, 'sh', '-c', ('set -e; export MYSQL_PWD="$MYSQL_ROOT_PASSWORD"; mysql -uroot --execute="CREATE DATABASE ' + $restoreName + ' CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_bin"; mysql -uroot --default-character-set=utf8mb4 ' + $restoreName + ' < /tmp/rehearsal.sql')) | Set-Content -LiteralPath $nativeLog
                $null = Invoke-RehearsalDocker @('cp', ($db + ':/tmp/rehearsal.sql'), (Join-Path $directory 'database.sql'))
            }
            'SqlServer' {
                $null = Invoke-RehearsalDocker @('exec', $db, 'mkdir', '-p', '/var/opt/mssql/backup')
                Invoke-RehearsalDocker @('exec', $db, '/opt/mssql-tools18/bin/sqlcmd', '-S', 'localhost', '-U', 'sa', '-C', '-b', '-Q', ('BACKUP DATABASE [' + $databaseName + '] TO DISK=N''/var/opt/mssql/backup/rehearsal.bak'' WITH CHECKSUM, INIT; RESTORE VERIFYONLY FROM DISK=N''/var/opt/mssql/backup/rehearsal.bak'' WITH CHECKSUM; RESTORE FILELISTONLY FROM DISK=N''/var/opt/mssql/backup/rehearsal.bak''; RESTORE DATABASE [' + $restoreName + '] FROM DISK=N''/var/opt/mssql/backup/rehearsal.bak'' WITH CHECKSUM, MOVE N''' + $databaseName + ''' TO N''/var/opt/mssql/data/' + $restoreName + '.mdf'', MOVE N''' + $databaseName + '_log'' TO N''/var/opt/mssql/data/' + $restoreName + '_log.ldf'';')) | Set-Content -LiteralPath $nativeLog
                $null = Invoke-RehearsalDocker @('cp', ($db + ':/var/opt/mssql/backup/rehearsal.bak'), (Join-Path $directory 'database.bak'))
            }
            'PostgreSql' {
                $null = Invoke-RehearsalDocker @('exec', $db, 'sh', '-c', 'set -e; export PGPASSWORD="$POSTGRES_PASSWORD"; pg_dump -U postgres --format=custom --file=/tmp/rehearsal.dump "$POSTGRES_DB"')
                Invoke-RehearsalDocker @('exec', $db, 'sh', '-c', ('set -e; export PGPASSWORD="$POSTGRES_PASSWORD"; createdb -U postgres ' + $restoreName + '; pg_restore -U postgres --exit-on-error --single-transaction --no-owner --no-acl --dbname=' + $restoreName + ' /tmp/rehearsal.dump')) | Set-Content -LiteralPath $nativeLog
                $null = Invoke-RehearsalDocker @('cp', ($db + ':/tmp/rehearsal.dump'), (Join-Path $directory 'database.dump'))
            }
        }
        Copy-Item -LiteralPath (Join-Path $directory 'source-files') -Destination (Join-Path $directory 'restored-files') -Recurse
        Invoke-RehearsalStage 'restore' $directory
        $controlPort = Free-RehearsalPort; $proxyPort = Free-RehearsalPort
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_PROXY_CONTROL' ('http://127.0.0.1:' + $controlPort)
        $proxyStart = @{ FilePath = $exe; ArgumentList = @('-host', '127.0.0.1', '-port', $controlPort); PassThru = $true; RedirectStandardOutput = (Join-Path $directory 'proxy.log'); RedirectStandardError = (Join-Path $directory 'proxy-error.log'); WindowStyle = 'Hidden' }
        $proxyProcess = Start-Process @proxyStart
        $proxyReady = $false
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            try { $null = Invoke-RestMethod -Uri ($env:APPROVAL_REHEARSAL_PROXY_CONTROL + '/version') -UserAgent 'Approval-Rehearsals' -TimeoutSec 1; $proxyReady = $true; break } catch { }
            Start-Sleep -Milliseconds 200
        }
        if (-not $proxyReady) { throw 'Toxiproxy readiness failed' }
        $proxyDefinition = @{ name = 'db'; listen = '127.0.0.1:' + $proxyPort; upstream = '127.0.0.1:' + $hostPort; enabled = $true }
        $null = Invoke-RestMethod -Uri ($env:APPROVAL_REHEARSAL_PROXY_CONTROL + '/proxies') -UserAgent 'Approval-Rehearsals' -Method Post -ContentType 'application/json' -Body ($proxyDefinition | ConvertTo-Json)
        $proxied = if ($current -eq 'SqlServer') { $source.Replace('127.0.0.1,' + $hostPort, '127.0.0.1,' + $proxyPort) } else { $source.Replace('Port=' + $hostPort + ';', 'Port=' + $proxyPort + ';') }
        Set-RehearsalEnvironment 'APPROVAL_REHEARSAL_PROXY_CONNECTION' $proxied
        Invoke-RehearsalStage 'network' $directory
        Stop-Process -Id $proxyProcess.Id; $proxyProcess.WaitForExit(10000) | Out-Null; $proxyProcess = $null
        $artifacts = @(Get-ChildItem -LiteralPath $directory -File -Recurse | Get-FileHash | Select-Object @{Name='name';Expression={[IO.Path]::GetRelativePath($directory, $_.Path)}}, Hash)
        $result = [ordered]@{ provider = $current; database = $databaseName; restoreDatabase = $restoreName; image = $images.$current; passed = $true; artifacts = $artifacts }
        $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $directory 'manifest.json') -Encoding utf8
        $results.Add($result)
        $owner = Invoke-RehearsalDocker @('inspect', '--format', '{{index .Config.Labels "approval.rehearsal"}}', $db)
        if ([string]$owner -cne $runId) { throw 'Database container owner mismatch.' }
        $null = Invoke-RehearsalDocker @('rm', '--force', '--volumes', $db); $containers.Remove($db) | Out-Null
    }
    $passed = $results.Count -eq $providers.Count
}
finally {
    if ($null -ne $proxyProcess -and -not $proxyProcess.HasExited) { Stop-Process -Id $proxyProcess.Id; $proxyProcess.WaitForExit(10000) | Out-Null }
    foreach ($container in $containers) {
        $owner = Invoke-RehearsalDocker @('inspect', '--format', '{{index .Config.Labels "approval.rehearsal"}}', $container)
        if ([string]$owner -cne $runId) { throw 'Refusing to clean a container owned by another run.' }
        if ($container -eq $keycloak) { & docker logs $container *> (Join-Path $reportRoot 'identity.log') }
        $null = Invoke-RehearsalDocker @('rm', '--force', '--volumes', $container)
    }
    # Only this run's explicit private files are removed. Reports/backups and cached pinned executable remain.
    foreach ($secretName in @('server.key', ($realm + '-realm.json'), ($realm + '-other-realm.json'))) {
        $secretPath = [IO.Path]::GetFullPath((Join-Path $scratchRoot $secretName))
        if (-not $secretPath.StartsWith([IO.Path]::GetFullPath($scratchRoot) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Secret cleanup target escaped run directory.' }
        if (Test-Path -LiteralPath $secretPath) { Remove-Item -LiteralPath $secretPath }
    }
    foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name]) }
    [ordered]@{ runId = $runId; passed = $passed; sdk = $rehearsalSdk; toolAssemblySha256 = $rehearsalToolHash; packageLockSha256 = $rehearsalLockHash; keycloakImage = $services.Keycloak; toxiproxyVersion = $services.ToxiproxyVersion; toxiproxySha256 = $services.ToxiproxyWindowsSha256; results = $results; realProductionEnvironment = $false } | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $reportRoot 'verification.json') -Encoding utf8
    Write-Host ('Rehearsal artifacts: ' + $reportRoot)
}
if (-not $passed) { throw 'Rehearsal did not complete.' }
Write-Host 'Authentication, native restore and real TCP network rehearsals passed.'



