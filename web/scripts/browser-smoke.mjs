import { mkdir, readFile, writeFile, createWriteStream } from 'node:fs'
import { promisify } from 'node:util'
import { spawn } from 'node:child_process'
import { createServer } from 'node:net'
import { once } from 'node:events'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const read = promisify(readFile)
const write = promisify(writeFile)
const makeDirectory = promisify(mkdir)
const webRoot = fileURLToPath(new URL('../', import.meta.url))
const manifestIndex = process.argv.indexOf('--manifest')
if (manifestIndex < 0 || !process.argv[manifestIndex + 1]) throw new Error('Pass --manifest <package verification.json>')
const manifest = JSON.parse(await read(process.argv[manifestIndex + 1], 'utf8'))
const output = join(manifest.ConsumerRoot, 'browser')
await makeDirectory(output, { recursive: true })

async function port() {
  const server = createServer()
  server.listen(0, '127.0.0.1')
  await once(server, 'listening')
  const value = server.address().port
  await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()))
  return value
}

function launch(command, args, cwd, name, env) {
  const log = createWriteStream(join(output, name + '.log'))
  const child = spawn(command, args, { cwd, env: { ...process.env, ...env }, windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] })
  child.stdout.pipe(log)
  child.stderr.pipe(log)
  child.once('error', error => log.write(String(error)))
  return child
}

async function ready(url, process) {
  const deadline = Date.now() + 90000
  while (Date.now() < deadline) {
    if (process.exitCode !== null) throw new Error(`Server exited before readiness: ${url}`)
    try { if ((await fetch(url, { signal: AbortSignal.timeout(1000) })).ok) return } catch {}
    await new Promise(resolve => setTimeout(resolve, 250))
  }
  throw new Error(`Server readiness timed out: ${url}; logs: ${output}`)
}

async function stop(child) {
  if (!child || child.exitCode !== null) return
  child.kill()
  await Promise.race([once(child, 'exit'), new Promise(resolve => setTimeout(resolve, 5000))])
  if (child.exitCode === null) child.kill('SIGKILL')
}

let api
let ui
try {
  const apiUrl = 'http://127.0.0.1:' + await port()
  const uiUrl = 'http://127.0.0.1:' + await port()
  const database = join(output, 'smoke-' + Date.now() + '.db')
  api = launch('dotnet', [join(manifest.ApiRoot, 'bin/Release/net10.0/Certificate.Api.dll'),
    '--urls', apiUrl, '--environment', 'Development', '--ConnectionStrings:Default', 'Data Source=' + database,
    '--Certificate:DemoMode', 'true', '--Approval:EnableBackgroundServices', 'false', '--Approval:BaseUrl', uiUrl,
    '--Approval:Files:RootPath', join(output, 'files')], manifest.ApiRoot, 'api', {})
  await ready(apiUrl + '/health/ready', api)
  ui = launch(process.execPath, [join(manifest.UiRoot, 'node_modules/vite/bin/vite.js'), 'preview', '--host', '127.0.0.1', '--port', new URL(uiUrl).port, '--strictPort'], manifest.UiRoot, 'ui', { APPROVAL_SMOKE_API: apiUrl })
  await ready(uiUrl, ui)
  await write(join(output, 'context.json'), JSON.stringify({ apiUrl, uiUrl, database, manifest: resolve(process.argv[manifestIndex + 1]) }, null, 2))
  console.log(`Package consumer ready: ${uiUrl}; context: ${output}`)
  if (process.argv.includes('--serve')) {
    await new Promise(resolve => { process.once('SIGINT', resolve); process.once('SIGTERM', resolve) })
  } else {
    const tests = spawn(process.execPath, [join(webRoot, 'node_modules/@playwright/test/cli.js'), 'test', '--config', 'playwright.config.ts'], {
      cwd: webRoot, windowsHide: true, stdio: 'inherit',
      env: { ...process.env, APPROVAL_SMOKE_URL: uiUrl, APPROVAL_SMOKE_API: apiUrl, APPROVAL_SMOKE_OUTPUT: output },
    })
    const [code] = await once(tests, 'exit')
    if (code !== 0) throw new Error('Package browser acceptance failed; see ' + output)
    await write(join(output, 'passed.json'), JSON.stringify({ version: manifest.Version, packages: manifest.Packages, completedAt: new Date().toISOString() }, null, 2))
  }
} finally {
  await stop(ui)
  await stop(api)
}
