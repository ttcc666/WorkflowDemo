export type MappingRule = {
  source: string
  target: string
  transform?: string
  default?: unknown
  omitNull?: boolean
  precision?: number
  items?: MappingRule[]
  filterPath?: string
  filterEquals?: unknown
}

export const mappingTransforms = ['Copy', 'String', 'Number', 'Boolean', 'Date', 'Array', 'Object']
export const newMappingRule = (): MappingRule => ({ source: '', target: '', transform: 'Copy' })
