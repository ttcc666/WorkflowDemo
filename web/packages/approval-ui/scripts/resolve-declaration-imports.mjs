import { readdir, readFile, writeFile } from 'node:fs/promises'
import { dirname, isAbsolute, join, relative, resolve, sep } from 'node:path'
import { fileURLToPath } from 'node:url'
import ts from 'typescript'

// 私有源码别名不能泄漏给安装 npm 包的宿主；声明文件继续使用包内相对路径。
export function resolveDeclarationImports(source, file, typesRoot) {
  const syntax = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true)
  const edits = []
  function rewrite(literal) {
    if (!literal || !ts.isStringLiteralLike(literal) || !literal.text.startsWith('@ui/')) return
    const target = resolve(typesRoot, literal.text.slice('@ui/'.length))
    const within = relative(typesRoot, target)
    if (isAbsolute(within) || within === '..' || within.startsWith(`..${sep}`)) throw new Error('Declaration alias escapes the package')
    const path = relative(dirname(file), target).replaceAll('\\', '/')
    edits.push({ start: literal.getStart(syntax) + 1, end: literal.end - 1, text: path.startsWith('.') ? path : `./${path}` })
  }
  function visit(node) {
    if (ts.isImportDeclaration(node) || ts.isExportDeclaration(node)) rewrite(node.moduleSpecifier)
    else if (ts.isImportTypeNode(node) && ts.isLiteralTypeNode(node.argument)) rewrite(node.argument.literal)
    ts.forEachChild(node, visit)
  }
  visit(syntax)
  for (const edit of edits.sort((a, b) => b.start - a.start)) source = source.slice(0, edit.start) + edit.text + source.slice(edit.end)
  return source
}

async function main() {
  const root = fileURLToPath(new URL('../dist/types', import.meta.url))
  async function walk(directory) {
    for (const entry of await readdir(directory, { withFileTypes: true })) {
      const file = join(directory, entry.name)
      if (entry.isDirectory()) await walk(file)
      else if (entry.name.endsWith('.d.ts')) {
        const source = await readFile(file, 'utf8')
        const output = resolveDeclarationImports(source, file, root)
        if (source !== output) await writeFile(file, output)
      }
    }
  }
  await walk(root)
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main()
