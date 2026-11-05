import { readFileSync } from 'node:fs'
import { expect, it } from 'vitest'
import type { FlowDefinition } from '@ui/contracts/flow'
import { toDefinition, toFlowElements } from '@ui/features/flow/flowSerialization'
import { cleanSchema } from '@ui/features/forms/formSerialization'

const cases = JSON.parse(readFileSync(new URL('../../../../../../tests/shared/definition-cases.json', import.meta.url), 'utf8')) as { name: string; json: string; valid: boolean }[]

it.each(cases.map(item => item.name))('shared definition contract: %s', name => {
  const item = cases.find(item => item.name === name)!
  const definition = JSON.parse(item.json) as FlowDefinition
  if (!item.valid) {
    expect(() => toFlowElements(definition)).toThrow('协议版本')
    return
  }
  const elements = toFlowElements(definition)
  const saved = { ...toDefinition(elements.nodes, elements.edges), form: cleanSchema(definition.form!) }
  expect(saved.schemaVersion).toBe(1)
  expect(Object.keys(saved.nodes).sort()).toEqual(['end', 'start'])
  expect(saved.nodes.start?.next).toBe('end')
  expect(saved.form.fields.map(field => field.type)).toEqual(['Text', 'Money'])
  expect(saved.form.fields[0]?.required).toBe(true)
  expect(saved.form.fields[1]?.precision).toBe(2)
})
