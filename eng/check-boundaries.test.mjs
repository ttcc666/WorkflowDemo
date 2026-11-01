import assert from 'node:assert/strict'
import { join, resolve } from 'node:path'
import test from 'node:test'
import { boundaryViolations } from './check-boundaries.mjs'

const workspace = resolve('boundary-test-workspace')
const sourceRoot = join(workspace, 'web/packages/approval-ui/src')
const roots = { contractsRoot: join(sourceRoot, 'contracts'), appsRoot: join(workspace, 'web/apps'), appPackages: ['approval-demo'] }

test('allows contracts to compose neighboring protocol types', () => {
  const source = "import type { Form } from './forms'; export type { Flow } from './flow'; export type Response = import('./api').Response"
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'index.ts'), source, roots), [])
})

test('rejects UI and feature imports from contracts including type queries', () => {
  const source = "import type { Ref } from 'vue'; export type Form = import('../features/forms/formModel').FormSchema"
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'flow.ts'), source, roots).map(v => v.specifier), ['vue', '../features/forms/formModel'])
})

test('rejects host paths and host package imports in Vue scripts', () => {
  const source = `<script setup lang="ts">import { session } from '../../../apps/demo/src/session'; const host = () => import('approval-demo/feature')</script>`
  assert.deepEqual(boundaryViolations(join(sourceRoot, 'Host.vue'), source, roots).map(v => v.specifier), ['../../../apps/demo/src/session', 'approval-demo/feature'])
})

test('does not treat a directory with a shared prefix as a contract directory', () => {
  const source = "export type { Flow } from '../contracts-legacy/flow'"
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'flow.ts'), source, roots).map(v => v.specifier), ['../contracts-legacy/flow'])
})

test('rejects unresolved dynamic imports in contracts', () => {
  const source = 'export const load = (moduleName: string) => import(moduleName)'
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'loader.ts'), source, roots).map(v => v.specifier), ['<dynamic>'])
})

test('rejects ambient client imports and re-exports in UI code even when aliased', () => {
  const source = "import { approvalApi as backend } from '../api/approval'; import { approvalUserKey } from '../config'; export { http as transport } from '../api/http'"
  assert.deepEqual(boundaryViolations(join(sourceRoot, 'views/Screen.ts'), source, roots).map(v => v.specifier), ['../api/approval', '../config', '../api/http'])
})

test('allows stateless HTTP helpers and client types in UI code', () => {
  const source = "import { ApiError, errorMessage } from '../api/http'; import { queryString } from '../api/approval'; import type { ApprovalUIOptions } from '../config'"
  assert.deepEqual(boundaryViolations(join(sourceRoot, 'views/Screen.ts'), source, roots), [])
})
