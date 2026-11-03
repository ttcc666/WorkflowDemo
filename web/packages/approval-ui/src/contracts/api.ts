/** 从真实 HTTP 契约生成类型，保留现有前端名称。 */
import type { components } from './http.generated'
import type { FlowSettings, FormSchema } from './forms'
export type { FlowSettings, FormField, FormFieldOption, FormFieldType, FormSchema, FormValues } from './forms'

type Schemas = components['schemas']
/** Web 默认接受数字字符串，但响应写出数字；默认响应也保留 null 和有默认值的属性。 */
type ResponseShape<T> = number extends T ? Exclude<T, string | undefined>
  : T extends readonly (infer Item)[] ? ResponseShape<Item>[]
  : T extends object ? { [Key in keyof T]-?: ResponseShape<Exclude<T[Key], undefined>> } : T

export type InstanceStatus = ResponseShape<Schemas['InstanceStatus']>
export type TaskStatus = ResponseShape<Schemas['ApprovalTaskStatus']>
export type TaskKind = ResponseShape<Schemas['ApprovalTaskKind']>
export type AddSignMode = ResponseShape<Schemas['AddSignMode']>
export type AssigneeKind = ResponseShape<Schemas['AssigneeKind']>
export type ApprovalAction = ResponseShape<Schemas['ApprovalAction']>
export type ApprovalAccessLevel = ResponseShape<Schemas['ApprovalAccessLevel']>
export type ApprovalLog = ResponseShape<Schemas['ApprovalLogResponse']>
export type ValidationResult = ResponseShape<Schemas['ApprovalValidationResponse']>
export type TodoItem = ResponseShape<Schemas['TodoItem']>
export type InstanceListItem = ResponseShape<Schemas['InstanceListItem']>
export type ReturnTarget = ResponseShape<Schemas['ReturnTarget']>
export type FlowSummary = ResponseShape<Schemas['FlowSummary']>
export type DefinitionGovernance = ResponseShape<Schemas['DefinitionGovernance']>
export type DefinitionGovernanceSummary = ResponseShape<Schemas['DefinitionGovernanceSummary']>
export type DefinitionAudit = ResponseShape<Schemas['ApprovalDefinitionAudit']>
export type SynchronousActionScan = ResponseShape<Schemas['SynchronousActionScan']>
export type ApprovalDiagnosticReport = ResponseShape<Schemas['ApprovalDiagnosticReport']>
export type MaintenanceTarget = ResponseShape<Schemas['MaintenanceTarget']>
export type MaintenanceMode = ResponseShape<Schemas['MaintenanceMode']>
export type MaintenancePreview = ResponseShape<Schemas['MaintenancePreview']>
export type MaintenanceJobInfo = ResponseShape<Schemas['MaintenanceJobInfo']>
export type MaintenanceBatchInfo = ResponseShape<Schemas['MaintenanceBatchInfo']>
export type ArchiveSummary = ResponseShape<Schemas['ArchiveSummary']>
export type ReassignSkip = ResponseShape<Schemas['ReassignSkip']>
export type ReassignResult = ResponseShape<Schemas['ReassignResult']>
export type FlowStatistics = ResponseShape<Schemas['FlowStatistics']>
export type NodeStatistics = ResponseShape<Schemas['NodeStatistics']>
export type BacklogItem = ResponseShape<Schemas['BacklogItem']>
export type ApprovalStatistics = ResponseShape<Schemas['ApprovalStatistics']>
export type ApprovalDefinition = ResponseShape<Schemas['ApprovalDefinitionResponse']>
export type ActionParameterKind = ResponseShape<Schemas['ActionParameterKind']>
export type ActionParameterMeta = ResponseShape<Schemas['ActionParameter']>
export type ActionMeta = ResponseShape<Schemas['ApprovalActionDescriptor']>
export type ChannelMeta = ResponseShape<Schemas['ChannelInfo']>
export type TemplateVariableMeta = ResponseShape<Schemas['TemplateVariableInfo']>
export type ApproverValueKind = ResponseShape<Schemas['ApproverValueKind']>
export type ApproverTypeMeta = ResponseShape<Schemas['ApproverTypeDescriptor']>
export type ApprovalMeta = ResponseShape<Schemas['ApprovalMetadata']>
export type ApprovalMessage = ResponseShape<Schemas['ApprovalMessageResponse']>
export type OutboxStatus = ResponseShape<Schemas['OutboxStatus']>
export type OutboxMessage = ResponseShape<Schemas['ApprovalOutboxMessageResponse']>
export type ApprovalMe = ResponseShape<Schemas['ApprovalMeResponse']>
export type StartableFlow = ResponseShape<Schemas['StartableFlow']>
export type DraftSummary = ResponseShape<Schemas['DraftSummary']>
export type DraftDetail = ResponseShape<Schemas['DraftDetail']>
export type DirectoryUser = ResponseShape<Schemas['ApprovalUser']>

/** 保留旧调用方使用的任务投影；完整传输结构在 http.generated.ts 中。 */
export type ApprovalTask = Pick<ResponseShape<Schemas['ApprovalTaskResponse']>,
  'id' | 'instanceId' | 'nodeId' | 'nodeName' | 'assigneeKind' | 'assigneeValue' | 'kind' | 'parentTaskId' | 'round' | 'status' | 'order' | 'handledBy' | 'comment' | 'createdAt' | 'completedAt' | 'dueAt' | 'remindCount'> & {
    capabilities?: Record<string, boolean>
  }
export type ApprovalInstance = Omit<ResponseShape<Schemas['ApprovalInstanceResponse']>, 'tasks' | 'logs'> & { tasks: ApprovalTask[]; logs: ApprovalLog[] }
export type MyTaskResult = Omit<ResponseShape<Schemas['ApprovalMyTaskResponse']>, 'task'> & { task: ApprovalTask | null }
export type PagedResult<T> = Omit<ResponseShape<Schemas['PagedResultOfTodoItem']>, 'items'> & { items: T[] }
export type StartForm = Omit<ResponseShape<Schemas['StartForm']>, 'settings' | 'form'> & { settings: FlowSettings; form: FormSchema }

export interface FormFieldChange {
  field: string
  label: string
  before: unknown
  after: unknown
}

export interface ListQuery {
  page?: number
  pageSize?: number
  keyword?: string
  status?: string
  definitionKey?: string
  from?: string
  to?: string
  initiatorId?: string
  /** 仅待办：只看已超时 */
  overdueOnly?: boolean
  /** 仅投递记录 */
  instanceId?: string
}
