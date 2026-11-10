import tseslint from 'typescript-eslint'
import reactHooks from 'eslint-plugin-react-hooks'

export default tseslint.config(
  { ignores: ['**/node_modules/**', '**/dist/**', '**/contracts/http.generated.ts', '**/components/shadcn/**'] },
  ...tseslint.configs.recommended.map(config => ({ ...config, files: ['packages/approval-shared/src/**/*.ts', 'packages/approval-react-ui/src/**/*.{ts,tsx}', 'apps/react-demo/src/**/*.{ts,tsx}', 'demo-shared/**/*.ts'] })),
  {
    files: ['packages/approval-react-ui/src/**/*.{ts,tsx}', 'apps/react-demo/src/**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: { 'react-hooks/rules-of-hooks': 'error', 'react-hooks/exhaustive-deps': 'error' },
  },
)
