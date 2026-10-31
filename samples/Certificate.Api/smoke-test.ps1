# 证书审核端到端冒烟测试。先启动示例：dotnet run --project samples/Certificate.Api --urls http://localhost:5199
param([string]$Base = 'http://localhost:5199')
$ErrorActionPreference = 'Stop'

function Call($method, $path, $user, $body) {
    $a = @{ Method = $method; Uri = "$Base$path"; Headers = @{ 'X-User-Id' = $user }; ContentType = 'application/json; charset=utf-8'; NoProxy = $true }
    if ($null -ne $body) { $a.Body = [Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 5)) }
    try { Invoke-RestMethod @a } catch { "HTTP $($_.Exception.Response.StatusCode.value__): $((($_.ErrorDetails.Message) | ConvertFrom-Json).title)" }
}
# Invoke-RestMethod 把 JSON 数组作为单个对象输出，这里展开成元素
function Items($value) { @($value | ForEach-Object { $_ }) }
function Approve($user, $comment) {
    $t = (Items (Call GET '/api/approval/todo' $user))[0]
    "  $user 审批 [$($t.nodeName)]"
    Call POST "/api/approval/tasks/$($t.taskId)/approve" $user @{ comment = $comment } | Out-Null
}
function With($table, $changes) { $copy = $table.Clone(); foreach ($k in $changes.Keys) { $copy[$k] = $changes[$k] }; $copy }
$certData = @{ certNo = 'C-1001'; name = '压力容器操作证'; holder = '张三'; certType = 'Special'; issueDate = '2026-01-01'; expireDate = '2029-01-01' }

"== 已发布流程"
Items (Call GET '/api/approval/definitions' 'admin') | ForEach-Object { "  $($_.key) v$($_.version)" }

"== 1. alice 申请新增特种证书：部门 → 质量 → 安全 → 负责人"
$r1 = Call POST '/api/certificates' 'alice' $certData
"  审核前证书数: $((Items (Call GET '/api/certificates' 'alice')).Count)（应为 0）"
"  dan 待办数: $((Items (Call GET '/api/approval/todo' 'dan')).Count)，dave 待办数: $((Items (Call GET '/api/approval/todo' 'dave')).Count)，quinn 待办数: $((Items (Call GET '/api/approval/todo' 'quinn')).Count)（应为 1, 1, 0）"
$deptTask = (Items (Call GET '/api/approval/todo' 'dan'))[0]
"  quinn 越权审批部门节点: $(Call POST "/api/approval/tasks/$($deptTask.taskId)/approve" 'quinn' @{ comment = 'x' })"
"  alice 审批自己的申请: $(Call POST "/api/approval/tasks/$($deptTask.taskId)/approve" 'alice' @{ comment = 'x' })"
Approve 'dan' '部门审核通过'; Approve 'quinn' '资质齐全'; Approve 'sam' '安全审核通过'; Approve 'leo' '同意'
$cert = (Items (Call GET '/api/certificates' 'alice'))[0]
"  审核后证书: $($cert.certNo) $($cert.name) 持有人=$($cert.holder) 状态=$($cert.status)"

"== 2. alice 变更持有人（普通证书，跳过安全审核），quinn 驳回"
$r2 = Call PUT "/api/certificates/$($cert.id)" 'alice' (With $certData @{ holder = '李四'; certType = 'Normal' })
"  再次提交变更: $(Call PUT "/api/certificates/$($cert.id)" 'alice' $certData)"
$detail = Call GET "/api/certificate-requests/$($r2.requestId)" 'dan'
"  变更字段: " + ((Items $detail.changes | ForEach-Object { "$($_.field): $($_.before) -> $($_.after)" }) -join '；')
Approve 'dave' 'OK'
$t = (Items (Call GET '/api/approval/todo' 'quinn'))[0]
Call POST "/api/approval/tasks/$($t.taskId)/reject" 'quinn' @{ comment = '缺少变更证明材料' } | Out-Null
"  驳回后证书持有人: $((Call GET "/api/certificates/$($cert.id)" 'alice').holder)（应仍为 张三）"
"  申请单状态: $((Call GET "/api/certificate-requests/$($r2.requestId)" 'alice').request.status)"

"== 3. alice 注销证书：质量审核 → 负责人会签（leo + lily）"
$r3 = Call POST "/api/certificates/$($cert.id)/revoke" 'alice' @{ reason = '人员离职' }
Approve 'quinn' 'OK'; Approve 'leo' '同意'
"  一位负责人同意后证书状态: $((Call GET "/api/certificates/$($cert.id)" 'alice').status)（应仍为 Active）"
Approve 'lily' '同意'
"  两位负责人同意后证书状态: $((Call GET "/api/certificates/$($cert.id)" 'alice').status)（应为 Revoked）"

"== 注销申请的审批轨迹"
Items (Call GET "/api/certificate-requests/$($r3.requestId)" 'alice').approval.logs |
    ForEach-Object { "  [$($_.sequence)] $($_.action) $($_.nodeName) by $($_.operatorId) $($_.comment)" }
