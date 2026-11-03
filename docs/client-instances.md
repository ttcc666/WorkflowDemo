# Vue 客户端

每个安装实例创建自己的 ApprovalClient，组件使用注入的 client，不依赖宿主全局 API 或认证状态。createApprovalRoutes 提供工作台、详情、定义、设计器、投递、监控、运行诊断和可选表单页面；宿主根据能力和 FormsEnabled 展示菜单。

从 @approval/ui/style.css 引入发布 CSS，从 @approval/ui/contracts 引入类型。@approval/ui 的当前 tgz 包含表单组件；不使用后端 Forms 时隐藏发起表单入口。组件遵循 Vue 3 Composition API，异步身份切换取消旧请求并清空敏感数据。

命令意图保存稳定 Idempotency-Key；未知结果与网络失败保留原键，禁止重试时自动生成新键。不可恢复的旧结果仍永久防重。实际 npm 安装/typecheck/build/browser 证据由 verify-packages 生成。
