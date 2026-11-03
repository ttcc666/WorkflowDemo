# Uses explicit loopback host and credentials from the runner's environment.
[CmdletBinding()]
param([Parameter(Mandatory)][Uri]$Base, [string]$ReportPath)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $Base.IsAbsoluteUri -or -not $Base.IsLoopback -or $Base.Scheme -notin @('http', 'https')) { throw 'Use an explicit loopback HTTP/HTTPS host.' }
$baseUrl = $Base.AbsoluteUri.TrimEnd('/')
$script:checks = 0
$runId = [Guid]::NewGuid().ToString('N')
$flow = @'
{"schemaVersion":1,"startNodeId":"start","settings":{"startable":true,"titleTemplate":"参考申请"},"form":{"fields":[{"key":"amount","label":"金额","type":"Number","required":true}]},"nodes":{"start":{"name":"提交","type":"Start","next":"review"},"review":{"name":"初审","type":"Approval","approvers":[{"type":"Role","value":"Reviewer"}],"next":"final"},"final":{"name":"终审","type":"Approval","approvers":[{"type":"Role","value":"FinalReviewer"}],"next":"end"},"end":{"name":"完成","type":"End"}}}
'@
function Invoke-Reference {
    param([string]$Method, [string]$Path, [string]$Tenant = 'alpha', [string]$User = 'alice', [string]$Key, $Body, [int]$Status = 200, [switch]$Raw)
    $token = [Environment]::GetEnvironmentVariable(('APPROVAL_DEMO_{0}_{1}' -f $Tenant, $User).ToUpperInvariant())
    if ([string]::IsNullOrEmpty($token)) { throw 'Missing demo credential environment variable.' }
    $headers = @{ Authorization = 'Bearer ' + $token }
    if ($Key) { $headers['Idempotency-Key'] = $Key }
    $arguments = @{ Uri = $baseUrl + $Path; Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($null -ne $Body) { $arguments.ContentType = 'application/json'; $arguments.Body = if ($Raw) { $Body } else { $Body | ConvertTo-Json -Depth 30 -Compress } }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -ne $Status) { throw ('{0} {1}: expected {2}, got {3}; {4}' -f $Method, $Path, $Status, [int]$response.StatusCode, $response.Content) }
    $script:checks++
    if ($response.Content) { return ($response.Content | ConvertFrom-Json) }
}
function Assert-Reference($Expected, $Actual) {
    if ($Expected -cne $Actual) { throw ('Expected {0}, got {1}' -f $Expected, $Actual) }
    $script:checks++
}
$instances = @{}
$intent = @{ formData = @{ amount = 12 }; comment = 'kestrel-acceptance' }
foreach ($tenant in @('alpha')) {
    $null = Invoke-Reference 'POST' ('/api/approval/definitions/shared?name=Reference') $tenant 'admin' ('publish-' + $runId) $flow -Raw
    $null = Invoke-Reference 'POST' '/api/approval/forms/shared/start' $tenant 'alice' $null $intent -Status 400
    $first = Invoke-Reference 'POST' '/api/approval/forms/shared/start' $tenant 'alice' $runId $intent
    $again = Invoke-Reference 'POST' '/api/approval/forms/shared/start' $tenant 'alice' $runId $intent
    Assert-Reference $first.instanceId $again.instanceId
    $changed = @{ formData = @{ amount = 13 }; comment = 'changed-content' }
    $null = Invoke-Reference 'POST' '/api/approval/forms/shared/start' $tenant 'alice' $runId $changed -Status 409
    Assert-Reference 1 (Invoke-Reference 'GET' '/api/approval/started' $tenant 'alice').total
    Assert-Reference 1 (Invoke-Reference 'GET' '/api/approval/todo/count' $tenant 'bob').count
    $instances[$tenant] = $first.instanceId
}
$null = Invoke-Reference 'PUT' '/demo/grants/bob/approval.task.approve' 'alpha' 'alice' -Body @{ allowed = $false } -Status 403
$null = Invoke-Reference 'PUT' '/demo/grants/bob/approval.task.approve' 'alpha' 'admin' -Body @{ allowed = $false } -Status 204
$me = Invoke-Reference 'GET' '/api/approval/me' 'alpha' 'bob'
Assert-Reference $false $me.capabilities.'approval.task.approve'
$task = (Invoke-Reference 'GET' ('/api/approval/instances/{0}/my-task' -f $instances.alpha) 'alpha' 'bob').task
$null = Invoke-Reference 'POST' ('/api/approval/tasks/{0}/approve' -f $task.id) 'alpha' 'bob' ('review-' + $runId) @{ comment = 'ok' } -Status 403
Assert-Reference 'Running' (Invoke-Reference 'GET' ('/api/approval/instances/' + $instances.alpha)).status
$null = Invoke-Reference 'PUT' '/demo/grants/bob/approval.task.approve' 'alpha' 'admin' -Body @{ allowed = $true } -Status 204
foreach ($tenant in @('alpha')) {
    foreach ($user in @('bob', 'carol')) {
        $task = (Invoke-Reference 'GET' ('/api/approval/instances/{0}/my-task' -f $instances[$tenant]) $tenant $user).task
        $null = Invoke-Reference 'POST' ('/api/approval/tasks/{0}/approve' -f $task.id) $tenant $user ($user + '-' + $runId) @{ comment = 'ok' } -Status 204
        if ($user -eq 'bob') { Assert-Reference 'Running' (Invoke-Reference 'GET' ('/api/approval/instances/' + $instances[$tenant]) $tenant).status }
    }
    Assert-Reference 'Approved' (Invoke-Reference 'GET' ('/api/approval/instances/' + $instances[$tenant]) $tenant).status
}
$result = [ordered]@{ runId = $runId; checks = $script:checks; instances = $instances; passed = $true }
if ($ReportPath) { $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $ReportPath -Encoding utf8 }
Write-Host ('Reference host smoke passed: {0} checks' -f $script:checks)
