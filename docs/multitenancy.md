# 单宿主边界

当前项目尚未正式使用，仅支持单实例嵌入式审批。Core 不提供 TenantId、租户目录、Claim 路由或租约轮转。

数据库使用各 Provider 的 `InitialApproval` 创建当前模型。开发期多租户迁移、回填工具及旧归属升级代码已删除，没有旧环境迁移责任。组织、身份和授权由宿主提供，接入见 [宿主指南](embedded-host-integration.md)。
