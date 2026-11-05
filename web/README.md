# 前端开发约定

## 源码与测试目录

```text
web/
├─ packages/approval-ui/
│  ├─ src/                     UI 库源码
│  ├─ tests/                   Vitest 单元测试，镜像 src 的模块结构
│  │  ├─ api/
│  │  ├─ composables/
│  │  ├─ features/
│  │  └─ client.test.ts
│  └─ tsconfig.test.json       测试专用类型检查
├─ apps/demo/
│  ├─ src/                     示例应用源码
│  ├─ tests/                   示例应用单元测试
│  └─ tsconfig.test.json
└─ tests/browser/              跨包浏览器验收测试
```

例如 `src/composables/useCommandIntent.ts` 的单元测试放在 `tests/composables/useCommandIntent.test.ts`。测试通过源码别名引用被测模块；测试之间的辅助代码可用同目录相对导入。共享协议用例继续复用仓库根目录 `tests/shared/` 的 JSON 数据。

Vitest 只发现各包的 `tests/**/*.test.ts`。`pnpm typecheck` 同时检查源码和测试；发布构建只处理源码，不生成测试声明。独立包消费者复制时跳过 `tests/`。

在 `web/` 下运行：

```powershell
pnpm test
pnpm typecheck
# 只运行某个模块：
pnpm --filter '@approval/ui' exec vitest run 'tests/composables/useCommandIntent.test.ts'
```

## 导入路径

| 使用位置 | 路径别名 | 指向 |
| --- | --- | --- |
| UI 库源码与测试 | `@ui/` | `web/packages/approval-ui/src/` |
| Demo 源码与测试 | `@/` | `web/apps/demo/src/` |
| Demo 或外部宿主引用 UI 库 | `@approval/ui`、`@approval/ui/contracts` | 包的公开导出 |

引用上级目录的模块时使用所属项目的别名，替代 `../` 和 `../../`。同目录依赖及入口文件已有的 `./` 导入/导出保留：

```ts
import { useApprovalClient } from '@ui/client'
import { errorMessage } from '@ui/api/http'
import CommandRecovery from '@ui/components/CommandRecovery.vue'
import type { ApprovalInstance } from '@ui/contracts/api'
import ApprovalActions from './ApprovalActions.vue'
```

不要通过源码别名跨包引用宿主，也不要绕过客户端注入规则。`eng/check-boundaries.mjs` 对别名和相对路径执行相同的边界检查。共享 JSON 测试数据的 `new URL(..., import.meta.url)` 是文件 URL，保留原有相对定位。

TypeScript、Vite、Vitest 均配置了相应映射。Demo 从 workspace 直接消费 UI 源码，因此它也需要识别 `@ui/`；发布包的使用方不需要配置这个内部别名。

UI 构建先由 Vite 打包 JavaScript、vue-tsc 生成类型声明，再通过 `packages/approval-ui/scripts/resolve-declaration-imports.mjs` 将声明中的内部别名改为包内相对路径。此步骤只处理模块导入、导出和 import 类型查询，不修改普通字符串或注释。[TypeScript 官方说明](https://www.typescriptlang.org/tsconfig/paths.html)：`paths` 本身不改写输出的导入路径。

验证（仓库根目录）：

```powershell
pwsh -NoProfile -File './eng/verify.ps1' -Scope 'Frontend' -SkipRestore
pwsh -NoProfile -File './eng/verify-packages.ps1' -SkipBuild -RunBrowser
```

独立包消费者使用自己的构建配置，验证已发布 JS 与类型声明可在没有 UI 源码别名的情况下使用。
