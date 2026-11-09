import { readFile, writeFile, mkdir, cp, readdir } from 'node:fs/promises'
import { spawnSync } from 'node:child_process'
import { createHash, randomUUID } from 'node:crypto'
import { basename, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('../', import.meta.url))
const web = join(root, 'web')
const releaseVersion = JSON.parse(await readFile(join(web, 'packages/approval-ui/package.json'), 'utf8')).version
for (const name of ['approval-shared', 'approval-react-ui']) {
  if (JSON.parse(await readFile(join(web, 'packages', name, 'package.json'), 'utf8')).version !== releaseVersion)
    throw new Error('Vue, React and shared package release versions must match')
}
const output = join(root, 'output/engineering/react-consumer-' + randomUUID())
const pnpm = process.env.npm_execpath
if (!pnpm) throw new Error('Run via pnpm verify:react-package')
function run(args, cwd = web) { const result = spawnSync(process.execPath, [pnpm, ...args], { cwd, stdio: 'inherit', windowsHide: true }); if (result.error) throw result.error; if (result.status !== 0) throw new Error('Package verification failed: ' + args.join(' ')) }
await mkdir(output, { recursive: true })
if (!process.argv.includes('--skip-build')) run(['build:react-lib'])
const sharedTar = join(output, 'approval-shared.tgz')
const reactTar = join(output, 'approval-react-ui.tgz')
run(['pack', '--out', sharedTar], join(web, 'packages/approval-shared'))
run(['pack', '--out', reactTar], join(web, 'packages/approval-react-ui'))
await cp(join(web, 'apps/react-demo/src'), join(output, 'src'), { recursive: true })
await cp(join(web, 'demo-shared'), join(output, 'demo-shared'), { recursive: true })
await cp(join(web, 'apps/react-demo/index.html'), join(output, 'index.html'))
await cp(join(root, 'eng/consumers/react-standalone/host.html'), join(output, 'host.html'))
await cp(join(root, 'eng/consumers/react-standalone/StandaloneHost.tsx'), join(output, 'src/StandaloneHost.tsx'))
const packageJson = {
  name: 'approval-react-package-consumer', private: true, type: 'module', packageManager: 'pnpm@10.20.0',
  pnpm: { overrides: { '@approval/shared': 'file:./approval-shared.tgz' } },
  scripts: { build: 'tsc --noEmit && vite build' },
  dependencies: { '@approval/shared': 'file:./approval-shared.tgz', '@approval/react-ui': 'file:./approval-react-ui.tgz', react: '19.3.1', 'react-dom': '19.3.1', 'react-router': '7.18.5', 'lucide-react': '0.468.0' },
  devDependencies: { '@types/node': '^26.6.6', '@types/react': '^19.2.0', '@types/react-dom': '^19.2.0', '@vitejs/plugin-react': '6.2.1', typescript: '^5.9.3', vite: '^8.3.5' },
}
await writeFile(join(output, 'package.json'), JSON.stringify(packageJson, null, 2))
await writeFile(join(output, 'tsconfig.json'), JSON.stringify({ compilerOptions: { target: 'ES2022', lib: ['ES2022', 'DOM', 'DOM.Iterable'], jsx: 'react-jsx', module: 'ESNext', moduleResolution: 'Bundler', strict: true, skipLibCheck: true, noEmit: true, allowSyntheticDefaultImports: true, types: ['node', 'vite/client'], paths: { '@demo-shared/*': ['./demo-shared/*'] } }, include: ['src', 'demo-shared', 'vite.config.ts'] }, null, 2))
await writeFile(join(output, 'vite.config.ts'), `import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'
const proxy = { '/api': { target: process.env.APPROVAL_SMOKE_API ?? 'http://127.0.0.1:1', changeOrigin: true } }
export default defineConfig({ plugins: [react()], resolve: { alias: { '@demo-shared': fileURLToPath(new URL('./demo-shared', import.meta.url)) } }, server: { proxy }, preview: { proxy }, build: { outDir: 'dist', emptyOutDir: true, rollupOptions: { input: ['index.html', 'host.html'] } } })
`)
await writeFile(join(output, 'src/contractsSmoke.tsx'), `import { FormField, createApprovalClient, type FlowDefinition, type FormSchema, type FormField as FieldModel } from '@approval/react-ui'
const field: FieldModel = { key: 'name', label: '名称', type: 'Text' }
export const form: FormSchema = { fields: [field] }
export const flow: FlowDefinition = { schemaVersion: 1, startNodeId: 'start', nodes: {}, form }
export const fieldComponent = <FormField label="名称">text</FormField>
export const client = createApprovalClient({ apiBase: '/api/approval' })
`)
run(['install', '--ignore-workspace', '--no-frozen-lockfile'], output)
for (const name of ['approval-shared', 'approval-react-ui']) {
  const installed = JSON.parse(await readFile(join(output, 'node_modules/@approval', name.replace('approval-', ''), 'package.json'), 'utf8'))
  if (installed.exports['.'].import !== './dist/index.js') throw new Error('Source exports leaked into packed package')
}
const publicTypes = await readFile(join(output, 'node_modules/@approval/react-ui/dist/types/index.d.ts'), 'utf8')
const packedManifest = JSON.parse(await readFile(join(output, 'node_modules/@approval/react-ui/package.json'), 'utf8'))
if (packedManifest.exports['./pages/*']?.import !== './dist/pages/*.js') throw new Error('Standalone page exports are missing')
for (const page of ['WorkbenchPage', 'ApprovalDetailPage', 'StartCenterPage', 'StartFormPage', 'DefinitionsPage', 'DesignerPage', 'MonitorPage', 'OutboxPage', 'DiagnosticsPage']) {
  await readFile(join(output, 'node_modules/@approval/react-ui/dist/pages', page + '.js'))
  await readFile(join(output, 'node_modules/@approval/react-ui/dist/types/pages', page + '.d.ts'))
}
if (/import ['"][^'"]+\.css['"]/.test(publicTypes)) throw new Error('Public declarations must not contain a build-only stylesheet import')
const publishedCss = await readFile(join(output, 'node_modules/@approval/react-ui/dist/approval-react-ui.css'), 'utf8')
if (!publishedCss.includes('.approval-react') || !publishedCss.includes('--approval-')) throw new Error('Packed CSS is missing component styles or prefixed theme tokens')
const forbidden = ['vue', 'vue-router', 'element-plus', '@approval/ui', '@vue-flow/core']
const lock = await readFile(join(output, 'pnpm-lock.yaml'), 'utf8')
if (forbidden.some(name => new RegExp('(?:^|\n) {2,4}[\x27"]?' + name.replaceAll('/', '\/') + '(?:@|:|\x27)').test(lock))) throw new Error('React consumer installed a Vue dependency')
run(['build'], output)
const native = spawnSync(process.execPath, ['--input-type=module', '-e', `import { createHttpClient } from '@approval/shared'; const http = createHttpClient(() => ({ fetcher: async () => new Response('{"id":"native-client"}') })); const value = await http.get('/me'); if (value.id !== 'native-client') throw new Error('Native ESM client failed');`], { cwd: output, stdio: 'inherit', windowsHide: true })
if (native.status !== 0) throw new Error('The shared package cannot be consumed by native Node ESM')
async function declarations(directory) { const result = []; for (const entry of await readdir(directory, { withFileTypes: true })) { const file = join(directory, entry.name); if (entry.isDirectory()) result.push(...await declarations(file)); else if (file.endsWith('.d.ts')) result.push(file) } return result }
for (const file of await declarations(join(output, 'node_modules/@approval/react-ui/dist/types'))) { const text = await readFile(file, 'utf8'); if (/from ['"](?:@ui\/|@\/|vue|@vue-flow|element-plus)/.test(text)) throw new Error('Declaration leaked an internal or Vue dependency: ' + file) }
const hashes = Object.fromEntries(await Promise.all([sharedTar, reactTar].map(async path => [basename(path), createHash('sha256').update(await readFile(path)).digest('hex')])))
await writeFile(join(output, 'verification.json'), JSON.stringify({ consumerRoot: resolve(output), packages: hashes, typecheck: 'passed', build: 'passed', nativeEsm: 'passed', vueDependency: 'absent' }, null, 2))
await writeFile(join(web, '../output/engineering/react-consumer-latest.json'), JSON.stringify({ consumerRoot: resolve(output) }))
console.log('Independent React package consumer passed: ' + output)
