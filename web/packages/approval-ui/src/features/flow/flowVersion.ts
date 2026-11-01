import type { FlowDefinition } from '../../contracts/flow'

export const currentFlowSchemaVersion = 1

/** 未知版本不能在编辑器中被静默降级并重新发布。 */
export function assertSupportedFlowVersion(definition: Pick<FlowDefinition, 'schemaVersion'>) {
  const version = definition.schemaVersion === undefined ? 1 : definition.schemaVersion
  if (version !== currentFlowSchemaVersion) throw new Error(`不支持流程协议版本 ${version}，当前支持 v${currentFlowSchemaVersion}`)
}
