# 注册证审批示例（Approval.Certificate）

注册证是接入审批框架的业务示例，包含业务服务、快照与幂等规则、审批回调、HTTP 接口、运行诊断、EF Core DbContext/迁移和默认流程。本项目位于 `samples/`，设置了 `IsPackable=false`，不作为框架的 NuGet 包发布。

独立类库用于让 `Certificate.Api` 和已有 Admin 集成复用这份示例实现。其他业务应拥有自己的实体、DbContext、业务回调与迁移，通过 `Approval.Core` / `Approval.AspNetCore` 的扩展点接入。

## 宿主注册

宿主提供身份实现、认证、连接字符串和生命周期。使用模块的 `AppDbContext`，让注册证数据与审批状态共用事务：

```csharp
using Approval.Certificate;
using Approval.Core;
using Approval.AspNetCore;
using Certificate.Api.Certificates;
using Certificate.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Approval")));
builder.Services.AddApprovalWorkflow<AppDbContext>(o => o.FixedTenantId = "certificate")
    .AddCertificateModule()
    .AddAccessPolicy<CertificateAccessPolicy>();
builder.Services.AddApprovalAspNetCore();

// 在宿主原有认证/授权管线中映射。
app.MapApprovalEndpoints().RequireAuthorization();
app.MapGroup("").RequireAuthorization().MapCertificateEndpoints();
```

`AddCertificateModule()` 注册业务、回调、动作与诊断服务；不配置认证、HTTP 代理、数据库连接，不自动迁移数据库、发布流程或启动后台监控。宿主在受控初始化阶段调用 `CertificateDatabase.InitializeAsync()`；需要监控时由宿主注册已提供的 `CertificateMonitor` 为 HostedService。

## 流程与迁移

- `CertificateFlowTemplates.ReadDefaults()` 返回三个内嵌默认定义，示例程序集离开源码目录后仍可读取。
- 构建和发布输出同时提供 `Flows/certificate.*.json`，便于宿主配置。示例优先采用宿主的同名文件；Admin 继续采用自己的 `ApprovalFlows`。
- 流程发布时机、首次初始化和更新策略由宿主决定。示例的 `CertificateApplication.PublishFlowsAsync` 提供“仅补缺失 / 显式更新”的实现。
- DbContext、迁移及设计时工厂位于本程序集，沿用 `CertificateBaseline` 和 `CertificatePilotHardening` 的原迁移 ID。旧库升级仍按[试点运维手册](../../docs/registration-certificate-pilot.md)执行。
- 本版本保持具体 `AppDbContext` 的注入方式。`ConfigureCertificates()` 提供独立模型配置；其他 DbContext 的业务服务适配应另行明确，不能只调用模型配置就替换现有注入。

为保持现有示例集成的兼容性，程序集名称仍为 `Approval.Certificate`，原有类型继续使用 `Certificate.Api.*` 命名空间。示例与 Admin 的旧程序集入口包含类型转发。示例的 HTTP、公开 API 和类型转发基线位于本目录的 `contracts/`，不混入框架契约。

阶段 2 权限接入见[框架权限指南](../../docs/permissions.md)：发起预检按发起权限执行；后台监控仅获得诊断权限，诊断接口与投递重试分别授权。启动发布和离线投递需要明确系统身份，不再隐式放行。
# 阶段 3 升级补充

新增 `20261102014813_AtomicOperations` 迁移。升级前停止写入及 worker，备份后应用迁移，并对固定租户 `certificate` 预览/回填进行中业务占用。不能仅升级 schema 就恢复旧 writer。完整步骤、维护命令与回退边界见 [事务与通用幂等](../../docs/transactions-and-idempotency.md)。

证照创建/变更/注销现在把业务准备、编号占用、编辑锁、审批和 Outbox 放入同一个命令委托。沿用原非空 UUID `Idempotency-Key`；同键同内容返回原结果，内容变化 409，网络失败或 `CommitOutcomeUnknown` / 503 必须保留原键重试。可以配置 `Approval:RequireOperationId=true` 强制其他框架写入口提供键；默认保留过渡模式。物理文件与外部通知不具备数据库跨介质原子提交保证。
