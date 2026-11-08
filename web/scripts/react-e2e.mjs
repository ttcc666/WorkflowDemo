import { mkdir, readFile, writeFile, createWriteStream } from 'node:fs'
import { promisify } from 'node:util'
import { spawn } from 'node:child_process'
import { createServer } from 'node:net'
import { once } from 'node:events'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'

const web = fileURLToPath(new URL('../', import.meta.url))
const root = fileURLToPath(new URL('../../', import.meta.url))
const output = join(root, 'output/playwright/react-' + Date.now())
await promisify(mkdir)(output, { recursive: true })
async function port() { const server = createServer(); server.listen(0, '127.0.0.1'); await once(server, 'listening'); const value = server.address().port; await new Promise(resolve => server.close(resolve)); return value }
function start(command, args, cwd, name, env = {}) { const log = createWriteStream(join(output, name + '.log')); const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] }); child.stdout.pipe(log); child.stderr.pipe(log); return child }
async function ready(url, child) { const limit = Date.now() + 90000; while (Date.now() < limit) { if (child.exitCode !== null) throw new Error('Server failed: ' + url); try { if ((await fetch(url, { signal: AbortSignal.timeout(1500) })).ok) return } catch {} await new Promise(resolve => setTimeout(resolve, 250)) } throw new Error('Readiness failed: ' + url) }
async function stop(child) { if (!child || child.exitCode !== null) return; child.kill(); await Promise.race([once(child, 'exit'), new Promise(resolve => setTimeout(resolve, 5000))]); if (child.exitCode === null) child.kill('SIGKILL') }
let api, ui
try {
  const apiUrl = 'http://127.0.0.1:' + await port()
  const uiUrl = 'http://127.0.0.1:' + await port()
  const database = join(output, 'react-smoke.db')
  const apiRoot = join(root, 'samples/Certificate.Api')
  const host = join(root, 'eng/consumers/ReactBrowserHost')
  const build = start('dotnet', ['build', join(host, 'ReactBrowserHost.csproj'), '--configuration', 'Release'], root, 'host-build')
  const [buildCode] = await once(build, 'exit')
  if (buildCode !== 0) throw new Error('Browser host build failed; see ' + output)
  api = start('dotnet', [join(host, 'bin/Release/net10.0/ReactBrowserHost.dll'), '--Tests:SampleRoot', apiRoot, '--Tests:Output', output, '--urls', apiUrl, '--environment', 'Development', '--ConnectionStrings:Default', 'Data Source=' + database, '--Certificate:DemoMode', 'true', '--Approval:EnableBackgroundServices', 'true', '--Approval:Outbox:PollInterval', '00:00:00.250', '--Approval:BaseUrl', apiUrl, '--Approval:Actions:ErpEndpoint', 'https://erp.example.invalid/orders', '--Approval:Files:RootPath', join(output, 'files')], apiRoot, 'api')
  await ready(apiUrl + '/health/ready', api)
  const packed = process.argv.includes('--packed')
  const uiRoot = packed ? JSON.parse(await promisify(readFile)(join(root, 'output/engineering/react-consumer-latest.json'), 'utf8')).consumerRoot : join(web, 'apps/react-demo')
  if (packed && !uiRoot.startsWith(join(root, 'output/engineering/react-consumer-'))) throw new Error('Consumer must be owned by the package verifier')
  if (!packed) {
    const uiBuild = start(process.execPath, [join(uiRoot, 'node_modules/vite/bin/vite.js'), 'build'], uiRoot, 'react-build')
    const [uiBuildCode] = await once(uiBuild, 'exit')
    if (uiBuildCode !== 0) throw new Error('React production build failed; see ' + output)
  }
  ui = start(process.execPath, [join(uiRoot, 'node_modules/vite/bin/vite.js'), 'preview', '--host', '127.0.0.1', '--port', new URL(uiUrl).port, '--strictPort'], uiRoot, 'react', { APPROVAL_SMOKE_API: apiUrl })
  await ready(uiUrl, ui)
  await promisify(writeFile)(join(output, 'context.json'), JSON.stringify({ apiUrl, uiUrl, vueUrl: apiUrl, database }, null, 2))
  console.log('Isolated React / Vue test host ready; evidence: ' + output)
  if (process.argv.includes('--serve')) await new Promise(resolve => { process.once('SIGINT', resolve); process.once('SIGTERM', resolve) })
  else {
    const tests = spawn(process.execPath, [join(web, 'node_modules/@playwright/test/cli.js'), 'test', '--config', 'playwright.react.config.ts', ...process.argv.slice(2).filter(arg => arg !== '--packed')], { cwd: web, windowsHide: true, stdio: 'inherit', env: { ...process.env, APPROVAL_SMOKE_API: apiUrl, APPROVAL_REACT_URL: uiUrl, APPROVAL_VUE_URL: apiUrl, APPROVAL_SMOKE_OUTPUT: output, APPROVAL_TEST_ROOT: root } })
    const [code] = await once(tests, 'exit')
    if (code !== 0) throw new Error('React parity acceptance failed; see ' + output)
  }
} finally { await stop(ui); await stop(api) }
