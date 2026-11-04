# Approval.Certificate 业务模块

示例证照业务类库，包含业务实体、回调、模型配置、默认流程及宿主迁移。审批引擎包不依赖此模块。

AppDbContext 直接继承 DbContext；ConfigureCertificates、ConfigureApproval、可选 ConfigureApprovalForms 共用宿主 EF 模型和本地事务。运行宿主 samples/Certificate.Api，或通过 eng/verify-packages.ps1 验证复制后的独立 NuGet 消费者。

当前开发版本 0.3.0，尚未正式发布。InitialApproval 由当前模型生成，不包含旧维护表或附件死状态字段。接入说明见 docs/upgrade-0.3.md；当前实际验证见 docs/rc-report.md。
