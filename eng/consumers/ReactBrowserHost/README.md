# React 浏览器测试宿主

复用 `CertificateApplication.AddCertificateApplication/UseCertificateApplicationAsync` 的真实服务、迁移、身份、权限、Forms 和审批 HTTP 协议。仅在显式 Development + DemoMode 下运行，并拒绝打开已有数据库或 output 目录之外的数据库。

测试新增 `HostEcho`（configVersion=3）和显式允许同步 GET 的 `read-model` 目标。HTTP Action 使用可控 MessageHandler 模拟宿主登记的外部系统；记录请求正文、稳定幂等键和 503/200 状态。它不执行真实外部副作用，也不改变生产代码或审批规则。

`/api/test/http/*` 仅测试宿主存在，读取 / 配置须经过样例可信当前用户服务确认 `admin`。审批操作仍由原引擎和宿主权限服务授权。此工程不作为生产部署入口。

从 `web/` 运行 `pnpm test:e2e:react`；脚本创建并管理本次独立服务器、SQLite 文件、附件目录和 Playwright 证据。
