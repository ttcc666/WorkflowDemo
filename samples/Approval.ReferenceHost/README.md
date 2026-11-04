# Approval.ReferenceHost

单宿主参考消费者：普通 DbContext、已有目录/JWT、宿主能力授权、可选 Forms、显式文件存储和 IApprovalEventHandler。开发认证由 Development + DemoMode 明确开启。

ReferenceDbContext 直接继承 DbContext，调用 ConfigureApproval 和 ConfigureApprovalForms。审批 DI 自动安装 UseApproval，设计工厂显式安装。四种 provider 各使用当前模型生成的 InitialApproval；未发布旧维护体系已从迁移基线删除。已有旧基线的可丢弃开发库由开发者重建。

根目录运行 pwsh -NoProfile -File ./eng/verify-reference-host.ps1。独立包验证复制本宿主，将框架源码引用替换为 NuGet 包，运行 Kestrel smoke。无 Forms 的真实 Identity 消费者另见 eng/consumers/EmbeddedHost。
