import { expect, it } from 'vitest'
import { definitionFileName, maxDefinitionFileBytes, readDefinitionFile } from '@ui/features/definitions/definitionTransfer'

it('previews the source and encodes the exact original file bytes', async () => {
  const json = '\uFEFF{ "packageVersion": 1, "kind": "approval-definition", "source": {"key":"purchase","name":"采购","version":3} }\r\n'
  const file = new File([json], 'purchase-v3.approval.json')
  const preview = await readDefinitionFile(file)
  expect(preview.source).toEqual({ key: 'purchase', name: '采购', version: 3 })
  expect(Uint8Array.from(atob(preview.packageBase64), character => character.charCodeAt(0))).toEqual(new TextEncoder().encode(json))
})

it('rejects oversized files and invalid UTF-8 or package versions before submission', async () => {
  await expect(readDefinitionFile(new File([new Uint8Array(maxDefinitionFileBytes + 1)], 'big.json'))).rejects.toThrow('超过 1 MiB')
  await expect(readDefinitionFile(new File([Uint8Array.of(0xff)], 'invalid.json'))).rejects.toThrow('UTF-8 JSON')
  await expect(readDefinitionFile(new File(['{"packageVersion":2,"kind":"approval-definition"}'], 'future.json'))).rejects.toThrow('包类型或版本')
})

it('keeps a readable filename without path separators', () => {
  expect(definitionFileName('采购/华东', 3)).toBe('采购_华东-v3.approval.json')
  expect(definitionFileName('..', 1)).toBe('workflow-v1.approval.json')
})
