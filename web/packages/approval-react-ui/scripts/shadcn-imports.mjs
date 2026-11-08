import { readFile, readdir, writeFile } from 'node:fs/promises'
import { dirname, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
const source = fileURLToPath(new URL('../src/', import.meta.url))
async function walk(root) {
  for (const item of await readdir(root, { withFileTypes: true })) {
    const file = resolve(root, item.name)
    if (item.isDirectory()) await walk(file)
    else if (/\.(ts|tsx)$/.test(file)) {
      const text = await readFile(file, 'utf8')
      const normalized = text.replace(/(from\s+["']|import\s*["'])(@\/[^"']+)(["'])/g, (_, before, module, after) => {
        const target = resolve(source, module.slice(2))
        let path = relative(dirname(file), target).replaceAll('\\', '/')
        if (!path.startsWith('.')) path = './' + path
        return before + path + after
      }).replaceAll('approval: approval:', 'approval:').replaceAll('"approval: ', '"')
      if (normalized !== text) await writeFile(file, normalized)
    }
  }
}
await walk(resolve(source, 'components/shadcn'))
console.log('Normalized official component imports to package-relative paths')
