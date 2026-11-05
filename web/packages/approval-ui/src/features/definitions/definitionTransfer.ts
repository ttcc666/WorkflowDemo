export const maxDefinitionFileBytes = 1024 * 1024

export function definitionFileName(key: string, version: number) {
  const safe = key.replace(/[^\p{L}\p{N}._-]/gu, '_').replace(/^\.+/, '') || 'workflow'
  return `${safe}-v${version}.approval.json`
}

export async function readDefinitionFile(file: File) {
  if (file.size > maxDefinitionFileBytes) throw new Error('导入文件超过 1 MiB')
  const bytes = new Uint8Array(await file.arrayBuffer())
  let value: unknown
  try { value = JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)) }
  catch { throw new Error('请选择有效的 UTF-8 JSON 文件') }
  if (!value || typeof value !== 'object' || !('kind' in value) || value.kind !== 'approval-definition'
    || !('packageVersion' in value) || value.packageVersion !== 1 || !('source' in value)) {
    throw new Error('不支持的流程定义包类型或版本')
  }
  const source = value.source
  if (!source || typeof source !== 'object' || !('key' in source) || typeof source.key !== 'string'
    || !('name' in source) || typeof source.name !== 'string' || !('version' in source) || typeof source.version !== 'number') {
    throw new Error('文件缺少有效的来源流程信息')
  }
  let binary = ''
  for (let offset = 0; offset < bytes.length; offset += 8192) binary += String.fromCharCode(...bytes.subarray(offset, offset + 8192))
  return { packageBase64: btoa(binary), source: { key: source.key, name: source.name, version: source.version } }
}
