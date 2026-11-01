# 注册证试点冒烟验收。仅针对显式指定的本机演示服务，使用独立证号和本次实例的待办。
param([Parameter(Mandatory)][Uri]$Base)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $Base.IsAbsoluteUri -or -not $Base.IsLoopback -or $Base.Scheme -notin @('http', 'https')) {
    throw '只允许显式指定的本机 HTTP/HTTPS 试点地址。'
}
$baseUrl = $Base.AbsoluteUri.TrimEnd('/')
$script:checks = 0

function Assert-Equal($expected, $actual, [string]$because) {
    if ($expected -cne $actual) { throw ('{0}：期望 {1}，实际 {2}' -f $because, $expected, $actual) }
    $script:checks++
}

function Invoke-Api {
    param([string]$Method, [string]$Path, [AllowNull()][string]$User = 'alice',
        $Body = $null, [int[]]$Expected = @(200), [string]$Key = '')
    $headers = @{}
    if ($User) { $headers['X-User-Id'] = $User }
    if ($Method -ne 'GET') { $headers['Idempotency-Key'] = if ($Key) { $Key } else { [Guid]::NewGuid().ToString() } }
    $arguments = @{ Method = $Method; Uri = $baseUrl + $Path; Headers = $headers; NoProxy = $true
        SkipHttpErrorCheck = $true; MaximumRedirection = 0; ContentType = 'application/json; charset=utf-8' }
    if ($null -ne $Body) { $arguments.Body = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 20)) }
    $response = Invoke-WebRequest @arguments
    if ([int]$response.StatusCode -notin $Expected) {
        throw ('HTTP {0} {1} {2}：{3}' -f $response.StatusCode, $Method, $Path, $response.Content)
    }
    $script:checks++
    $json = if ($response.Content) { $response.Content | ConvertFrom-Json -Depth 30 } else { $null }
    [PSCustomObject]@{ Status = [int]$response.StatusCode; Json = $json }
}

function Certificates([string]$number) {
    $path = '/api/certificates?page=1&pageSize=100&keyword=' + [Uri]::EscapeDataString($number)
    @((Invoke-Api -Method 'GET' -Path $path).Json.items | Where-Object { $_.certNo -ceq $number })
}

function Approve-Stage([string]$instance, [string]$user, [string]$node) {
    $task = (Invoke-Api -Method 'GET' -Path ('/api/approval/instances/{0}/my-task' -f $instance) -User $user).Json.task
    if ($null -eq $task) { throw ('{0} 没有本次申请的待办' -f $user) }
    Assert-Equal $node $task.nodeId '审核顺序'
    $null = Invoke-Api -Method 'POST' -Path ('/api/approval/tasks/{0}/approve' -f $task.id) -User $user -Body @{ comment = '本地试点验收通过' } -Expected @(204)
}

$runId = [Guid]::NewGuid().ToString('N')
$number = 'REG-PILOT-' + $runId
$zone = [TimeZoneInfo]::FindSystemTimeZoneById('Asia/Shanghai')
$today = [TimeZoneInfo]::ConvertTime([DateTimeOffset]::UtcNow, $zone).Date
$data = @{ certNo = $number; name = '注册证试点资料'; holder = '本地模拟企业'; certType = 'Normal'
    issueDate = $today.ToString('yyyy-MM-dd'); expireDate = $today.AddYears(3).ToString('yyyy-MM-dd') }
$null = Invoke-Api -Method 'GET' -Path '/api/demo/users'
$null = Invoke-Api -Method 'GET' -Path '/api/certificates' -User $null -Expected @(401)

$key = [Guid]::NewGuid().ToString()
$request = (Invoke-Api -Method 'POST' -Path '/api/certificates' -Body $data -Expected @(202) -Key $key).Json.requestId
$replay = (Invoke-Api -Method 'POST' -Path '/api/certificates' -Body $data -Expected @(202) -Key $key).Json.requestId
Assert-Equal $request $replay '重复提交必须返回同一申请'
$detail = (Invoke-Api -Method 'GET' -Path ('/api/certificate-requests/{0}' -f $request)).Json
$instance = $detail.approval.id
$null = Invoke-Api -Method 'GET' -Path ('/api/certificate-requests/{0}' -f $request) -User 'bob' -Expected @(404)
Assert-Equal 0 @(Certificates $number).Count '审核前不能生成正式证书'

Approve-Stage $instance 'dave' 'dept'
Approve-Stage $instance 'quinn' 'quality'
Assert-Equal 0 @(Certificates $number).Count '复审后不能提前生效'
Approve-Stage $instance 'leo' 'leader'
$certificates = @(Certificates $number)
Assert-Equal 1 $certificates.Count '终审后恰好生成一份正式证书'
$certificate = $certificates[0]
Assert-Equal $data.holder $certificate.holder '正式证书必须使用提交快照'
Assert-Equal 'Active' $certificate.status '正式证书状态'
Assert-Equal 'Valid' $certificate.effectiveStatus '注册证有效期状态'

$updated = $data.Clone()
$updated.name = '注册证试点资料-版本二'
$updateRequest = (Invoke-Api -Method 'PUT' -Path ('/api/certificates/{0}' -f $certificate.id) -Body $updated -Expected @(202)).Json.requestId
$updateDetail = (Invoke-Api -Method 'GET' -Path ('/api/certificate-requests/{0}' -f $updateRequest)).Json
$updateInstance = $updateDetail.approval.id
Assert-Equal $data.name $updateDetail.request.beforeSnapshot.name '提交前快照'
Approve-Stage $updateInstance 'dave' 'dept'
Approve-Stage $updateInstance 'quinn' 'quality'
Approve-Stage $updateInstance 'leo' 'leader'
$history = (Invoke-Api -Method 'GET' -Path ('/api/certificate-requests/{0}' -f $updateRequest)).Json
$nameChange = @($history.changes | Where-Object field -eq 'Name')
Assert-Equal 1 $nameChange.Count '生效后仍需保留历史差异'
Assert-Equal $data.name $nameChange[0].before '历史提交前值'
Assert-Equal $updated.name $nameChange[0].after '历史提交后值'

[PSCustomObject]@{ runId = $runId; requestId = $request; certificateId = $certificate.id; updateRequestId = $updateRequest
    checks = $script:checks; result = 'Passed' } | ConvertTo-Json
