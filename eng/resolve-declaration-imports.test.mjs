import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
import test from 'node:test'
import { resolveDeclarationImports } from '../web/packages/approval-ui/scripts/resolve-declaration-imports.mjs'

const root = resolve('declaration-test-workspace/dist/types')

test('published declarations resolve internal imports, exports and type queries without host aliases', () => {
  const source = `import type { User } from '@ui/contracts/api'
export { Client } from "@ui/client"
export type Props = import('@ui/features/forms/DynamicForm.vue').Props
import type { Ref } from 'vue'
export type Local = import('./local').Value
export type Text = '@ui/literal'
// @ui/comment`
  const expected = `import type { User } from '../contracts/api'
export { Client } from "../client"
export type Props = import('../features/forms/DynamicForm.vue').Props
import type { Ref } from 'vue'
export type Local = import('./local').Value
export type Text = '@ui/literal'
// @ui/comment`
  assert.equal(resolveDeclarationImports(source, join(root, 'views/Screen.vue.d.ts'), root), expected)
})

test('declarations at package root use explicit relative imports', () => {
  assert.equal(resolveDeclarationImports("export type { User } from '@ui/contracts/api'", join(root, 'index.d.ts'), root),
    "export type { User } from './contracts/api'")
})

test('declaration aliases cannot escape the published package', () => {
  assert.throws(() => resolveDeclarationImports("import type { Secret } from '@ui/../outside'", join(root, 'index.d.ts'), root), /escapes the package/)
})
