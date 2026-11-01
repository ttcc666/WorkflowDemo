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
builder.Services.AddApprovalWorkflow<AppDbContext>()
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
