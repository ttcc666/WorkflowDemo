import { readdir, readFile } from 'node:fs/promises'
import { createRequire } from 'node:module'
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'

const require = createRequire(new URL('../web/packages/approval-ui/package.json', import.meta.url))
const ts = require('typescript')
const repositoryRoot = fileURLToPath(new URL('../', import.meta.url))

function within(directory, target) {
  const path = relative(directory, target)
  return path === '' || (!isAbsolute(path) && path !== '..' && !path.startsWith(`..${sep}`))
}

/** Check actual TS import/export/type-query nodes, including imports in Vue scripts. */
export function boundaryViolations(file, source, { contractsRoot, appsRoot, appPackages }) {
  const contract = within(contractsRoot, file)
  const sourceRoot = dirname(contractsRoot)
  const uiCode = ['components', 'composables', 'features', 'views'].some(area => within(join(sourceRoot, area), file))
  const scripts = file.endsWith('.vue')
    ? [...source.matchAll(/<script\b[^>]*>([\s\S]*?)<\/script>/g)].map(match => match[1]).join('\n')
    : source
  const syntax = ts.createSourceFile(file, scripts, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS)
  const violations = []
  const check = (specifier, values = []) => {
    const target = specifier.startsWith('.') || isAbsolute(specifier) ? resolve(dirname(file), specifier) : null
    if (contract && (!target || !within(contractsRoot, target))) {
      violations.push({ specifier, reason: 'contracts may depend only on other contract files' })
    }
    if ((target && within(appsRoot, target)) || appPackages.some(name => specifier === name || specifier.startsWith(`${name}/`))) {
      violations.push({ specifier, reason: 'the UI library must not depend on a host application' })
    }
    const module = target ? relative(sourceRoot, target).replaceAll('\\', '/').replace(/\.ts$/, '') : specifier
    const ambient = {
      'api/approval': ['approvalApi'],
      'api/http': ['http'],
      config: ['approvalConfig', 'approvalUserKey'],
      '@approval/ui': ['approvalApi', 'http', 'approvalConfig', 'approvalUserKey'],
    }[module]
    if (uiCode && ambient && values.some(name => name === '*' || ambient.includes(name))) {
      violations.push({ specifier, reason: 'use the injected ApprovalClient in UI code' })
    }
  }
  const visit = (node) => {
    if ((ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) && node.moduleSpecifier && ts.isStringLiteralLike(node.moduleSpecifier)) {
      const clause = ts.isImportDeclaration(node) ? node.importClause : undefined
      const binding = clause?.namedBindings ?? (ts.isExportDeclaration(node) ? node.exportClause : undefined)
      const typeOnly = clause?.isTypeOnly || (ts.isExportDeclaration(node) && node.isTypeOnly)
      const values = typeOnly ? [] : binding && (ts.isNamedImports(binding) || ts.isNamedExports(binding))
        ? binding.elements.filter(item => !item.isTypeOnly).map(item => (item.propertyName ?? item.name).text)
        : binding || ts.isExportDeclaration(node) ? ['*'] : []
      check(node.moduleSpecifier.text, values)
    } else if (ts.isImportTypeNode(node) && ts.isLiteralTypeNode(node.argument) && ts.isStringLiteralLike(node.argument.literal)) {
      check(node.argument.literal.text)
    } else if (ts.isImportEqualsDeclaration(node) && ts.isExternalModuleReference(node.moduleReference) && node.moduleReference.expression) {
      if (ts.isStringLiteralLike(node.moduleReference.expression)) check(node.moduleReference.expression.text, node.isTypeOnly ? [] : ['*'])
    } else if (ts.isCallExpression(node) && (node.expression.kind === ts.SyntaxKind.ImportKeyword || (ts.isIdentifier(node.expression) && node.expression.text === 'require'))) {
      const argument = node.arguments[0]
      if (argument && ts.isStringLiteralLike(argument)) check(argument.text, ['*'])
      else if (contract) violations.push({ specifier: '<dynamic>', reason: 'contract imports must be statically resolvable' })
    }
    ts.forEachChild(node, visit)
  }
  visit(syntax)
  return violations
}

async function sourceFiles(directory) {
  const entries = await readdir(directory, { withFileTypes: true })
  const groups = await Promise.all(entries.map(async entry => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return /\.(ts|vue)$/.test(entry.name) && !/\.test\.ts$/.test(entry.name) ? [path] : []
  }))
  return groups.flat()
}

async function main() {
  const sourceRoot = join(repositoryRoot, 'web/packages/approval-ui/src')
  const appsRoot = join(repositoryRoot, 'web/apps')
  const apps = await readdir(appsRoot, { withFileTypes: true })
  const appPackages = await Promise.all(apps.filter(entry => entry.isDirectory()).map(async entry =>
    JSON.parse(await readFile(join(appsRoot, entry.name, 'package.json'), 'utf8')).name))
  const roots = { contractsRoot: join(sourceRoot, 'contracts'), appsRoot, appPackages }
  const files = await sourceFiles(sourceRoot)
  let failures = 0
  for (const file of files) {
    for (const violation of boundaryViolations(file, await readFile(file, 'utf8'), roots)) {
      console.error(`${relative(repositoryRoot, file)}: ${violation.specifier}: ${violation.reason}`)
      failures++
    }
  }
  if (failures) process.exitCode = 1
  else console.log(`Dependency boundaries passed: ${files.length} source files checked.`)
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main()
