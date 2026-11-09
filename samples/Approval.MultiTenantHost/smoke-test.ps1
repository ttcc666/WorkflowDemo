[CmdletBinding()]
param([string]$BaseUrl = 'http://localhost:5186')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Sample {
    param([string]$Tenant, [string]$User, [string]$Method, [string]$Path, [string]$Key = '', [string]$Body = '', [int]$Expected = 200)
    $headers = @{ Authorization = ('Bearer multitenant-demo-{0}-{1}-0000000000000000' -f $Tenant, $User) }
    if ($Key) { $headers['Idempotency-Key'] = $Key }
    $arguments = @{ Uri = ($BaseUrl.TrimEnd('/') + $Path); Method = $Method; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($Body) { $arguments.Body = $Body; $arguments.ContentType = 'application/json' }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -ne $Expected) { throw ('{0} {1}: expected {2}, received {3}: {4}' -f $Method, $Path, $Expected, $response.StatusCode, $response.Content) }
    if ($response.Content) { return ($response.Content | ConvertFrom-Json) }
}

$flow = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'expense-flow.json')
$intent = [Guid]::NewGuid().ToString('N')
$body = @{ businessKey = ('SMOKE-' + $intent); amount = 12 } | ConvertTo-Json -Compress
$instances = @{}
$tasks = @{}
foreach ($tenant in @('alpha', 'bravo')) {
    $null = Invoke-Sample $tenant 'admin' 'POST' '/api/approval/definitions/expense?name=Expense' 'sample-publish-v1' $flow
    $first = Invoke-Sample $tenant 'alice' 'POST' '/demo/expenses' $intent $body
    $replay = Invoke-Sample $tenant 'alice' 'POST' '/demo/expenses' $intent $body
    if ($first.instanceId -ne $replay.instanceId -or $first.tenantId -ne $tenant) { throw 'Tenant or idempotent replay mismatch.' }
    $instances[$tenant] = $first.instanceId
    $todo = Invoke-Sample $tenant 'bob' 'GET' '/api/approval/todo'
    $current = @($todo.items | Where-Object { $_.instanceId -eq $first.instanceId })
    if ($current.Count -ne 1) { throw 'Expected one pending task for this intent.' }
    $tasks[$tenant] = $current[0].taskId
}
if ($instances['alpha'] -eq $instances['bravo']) { throw 'Tenant resource IDs collided.' }
$null = Invoke-Sample 'bravo' 'admin' 'GET' ('/api/approval/instances/' + $instances['alpha']) -Expected 404
foreach ($tenant in @('alpha', 'bravo')) {
    $null = Invoke-Sample $tenant 'bob' 'POST' ('/api/approval/tasks/{0}/approve' -f $tasks[$tenant]) $intent '{"comment":"同意"}' -Expected 204
    $expenses = @(Invoke-Sample $tenant 'alice' 'GET' '/demo/expenses')
    $expense = @($expenses | Where-Object { $_.instanceId -eq $instances[$tenant] })
    if ($expense.Count -ne 1 -or $expense[0].status -ne 'Approved') { throw 'Host expense lifecycle did not commit with approval.' }
    $deadline = (Get-Date).AddSeconds(15)
    do {
        $deliveries = @(Invoke-Sample $tenant 'admin' 'GET' '/demo/deliveries')
        $received = @($deliveries | Where-Object { $_.instanceId -eq $instances[$tenant] })
        if ($received.Count -gt 0) { break }
        Start-Sleep -Milliseconds 200
    } while ((Get-Date) -lt $deadline)
    if ($received.Count -eq 0) { throw 'No tenant-scoped Outbox receipt observed.' }
}
[pscustomobject]@{ AlphaInstance = $instances['alpha']; BravoInstance = $instances['bravo']; Result = 'Passed' }
