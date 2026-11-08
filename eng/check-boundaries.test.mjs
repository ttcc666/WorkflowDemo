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

test('React rejects Vue runtime and type dependencies while allowing the shared public package', () => {
  const react = { ...roots, kind: 'react' }
  const file = join(sourceRoot, 'features/Screen.tsx')
  assert.deepEqual(boundaryViolations(file, "import type { Ref } from 'vue'; import { Item } from '@approval/ui'; import { Data } from '@approval/shared'; export const view = <div />", react).map(item => item.reason), [
    'React must not depend on Vue or Element Plus, including type imports',
    'React must not depend on Vue or Element Plus, including type imports',
  ])
})

test('shared rejects both framework implementations and Demo support modules', () => {
  const shared = { ...roots, kind: 'shared' }
  assert.deepEqual(boundaryViolations(join(sourceRoot, 'model.ts'), "import { useState } from 'react'; import { session } from '@demo-shared/demo'", shared).map(item => item.reason), [
    'shared must not depend on UI frameworks or components',
    'the UI library must not depend on a host application',
  ])
})

test('Vue contract facades may re-export only the shared contracts subpath', () => {
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'api.ts'), "export type * from '@approval/shared/contracts/api'", roots), [])
  assert.deepEqual(boundaryViolations(join(roots.contractsRoot, 'api.ts'), "export * from '@approval/shared/api/approval'", roots).map(item => item.reason), ['contracts may depend only on other contract files'])
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

test('source aliases preserve contract and host dependency boundaries', () => {
  const file = join(roots.contractsRoot, 'flow.ts')
  assert.deepEqual(boundaryViolations(file, "export type { Flow } from '@ui/contracts/flow'", roots), [])
  const source = "export type A = import('@ui/features/forms/formModel').FormSchema; export type B = import('@ui/../../../apps/demo/src/session').Session"
  assert.deepEqual(boundaryViolations(file, source, roots).map(v => v.reason), [
    'contracts may depend only on other contract files',
    'contracts may depend only on other contract files',
    'the UI library must not depend on a host application',
  ])
  assert.deepEqual(boundaryViolations(join(sourceRoot, 'views/Screen.vue'),
    `<script setup lang="ts">import { session } from '@/stores/session'</script>`, roots).map(v => v.reason),
  ['the UI library must not depend on a host application'])
})

test('source aliases cannot bypass injected client rules', () => {
  const file = join(sourceRoot, 'views/Screen.vue')
  const source = `<script setup lang="ts">import { approvalApi } from '@ui/api/approval'; export { http } from '@ui/api/http'; import { errorMessage } from '@ui/api/http'</script>`
  assert.deepEqual(boundaryViolations(file, source, roots).map(v => v.specifier), ['@ui/api/approval', '@ui/api/http'])
})
