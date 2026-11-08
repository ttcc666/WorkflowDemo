/** 从当前 HTTP 契约生成前端响应类型。 */
import type { components } from './http.generated.js'
import type { FlowSettings, FormSchema } from './forms.js'
export type { FlowSettings, FormField, FormFieldOption, FormFieldType, FormSchema, FormValues } from './forms.js'

type Schemas = components['schemas']
/** Web 默认接受数字字符串，但响应写出数字；默认响应也保留 null 和有默认值的属性。 */
type ResponseShape<T> = number extends T ? Exclude<T, string | undefined>
  : T extends readonly (infer Item)[] ? ResponseShape<Item>[]
  : T extends object ? { [Key in keyof T]-?: ResponseShape<Exclude<T[Key], undefined>> } : T

export type InstanceStatus = NonNullable<ResponseShape<Schemas['InstanceStatus']>>
export type TaskStatus = ResponseShape<Schemas['ApprovalTaskStatus']>
export type TaskKind = ResponseShape<Schemas['ApprovalTaskKind']>
export type AddSignMode = ResponseShape<Schemas['AddSignMode']>
export type AssigneeKind = ResponseShape<Schemas['AssigneeKind']>
export type ApprovalAction = ResponseShape<Schemas['ApprovalAction']>
export type ApprovalAccessLevel = ResponseShape<Schemas['ApprovalAccessLevel']>
export type ApprovalLog = ResponseShape<Schemas['ApprovalLogResponse']>
export type ValidationResult = ResponseShape<Schemas['ApprovalValidationResponse']>
export type TodoItem = ResponseShape<Schemas['TodoItem']>
export type InstanceListItem = Omit<ResponseShape<Schemas['InstanceListItem']>, 'status'> & { status: InstanceStatus }
export type ReturnTarget = ResponseShape<Schemas['ReturnTarget']>
export type FlowSummary = ResponseShape<Schemas['FlowSummary']>
export type DefinitionGovernance = ResponseShape<Schemas['DefinitionGovernance']>
export type DefinitionGovernanceSummary = ResponseShape<Schemas['DefinitionGovernanceSummary']>
export type DefinitionAudit = ResponseShape<Schemas['ApprovalDefinitionAudit']>
export type DefinitionTransferSource = ResponseShape<Schemas['DefinitionTransferSource']>
export type DefinitionImportReceipt = ResponseShape<Schemas['DefinitionImportReceipt']>
export type ApprovalDiagnosticReport = ResponseShape<Schemas['ApprovalDiagnosticReport']>
export type ReassignSkip = ResponseShape<Schemas['ReassignSkip']>
export type ReassignResult = ResponseShape<Schemas['ReassignResult']>
export type FlowStatistics = ResponseShape<Schemas['FlowStatistics']>
export type NodeStatistics = ResponseShape<Schemas['NodeStatistics']>
export type BacklogItem = ResponseShape<Schemas['BacklogItem']>
export type ApprovalStatistics = ResponseShape<Schemas['ApprovalStatistics']>
export type ApprovalDefinition = ResponseShape<Schemas['ApprovalDefinitionResponse']>
export type ChannelMeta = ResponseShape<Schemas['ChannelInfo']>
export type TemplateVariableMeta = ResponseShape<Schemas['TemplateVariableInfo']>
export type ApproverValueKind = ResponseShape<Schemas['ApproverValueKind']>
export type ApproverTypeMeta = ResponseShape<Schemas['ApproverTypeDescriptor']>
export type ApprovalActionMeta = ResponseShape<Schemas['ApprovalActionDescriptor']>
export type ApprovalMeta = Omit<ResponseShape<Schemas['ApprovalMetadata']>, 'actions'> & { actions?: ApprovalActionMeta[] | null }
export type ApprovalMessage = ResponseShape<Schemas['ApprovalMessageResponse']>
export type OutboxStatus = ResponseShape<Schemas['OutboxStatus']>
export type OutboxMessage = Omit<ResponseShape<Schemas['ApprovalOutboxMessageResponse']>, 'payloadJson'> & { payloadJson?: string | null }
export type OutboxPayload = ResponseShape<Schemas['ApprovalOutboxPayloadResponse']>
export type ApprovalMe = ResponseShape<Schemas['ApprovalMeResponse']>
export type StartableFlow = ResponseShape<Schemas['StartableFlow']>
export type DraftSummary = ResponseShape<Schemas['DraftSummary']>
export type DraftDetail = ResponseShape<Schemas['DraftDetail']>
export type DirectoryUser = ResponseShape<Schemas['ApprovalUser']>

export type ApprovalTask = ResponseShape<Schemas['ApprovalTaskResponse']>
export type ApprovalInstance = Omit<ResponseShape<Schemas['ApprovalInstanceResponse']>, 'tasks' | 'logs' | 'status'> & { status: InstanceStatus; tasks: ApprovalTask[]; logs: ApprovalLog[] }
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
  businessType?: string
  businessKey?: string
  approverId?: string
  sort?: string
  unreadOnly?: boolean
  page?: number
  pageSize?: number
  keyword?: string
  status?: string
  definitionKey?: string
  from?: string
  to?: string
  initiatorId?: string
  /** 待办或实例列表：只看存在超期待办的运行实例 */
  overdueOnly?: boolean
  /** 仅投递记录 */
  instanceId?: string
}

export type CarbonCopyItem = Omit<ResponseShape<Schemas['CarbonCopyItem']>, 'status'> & { status: InstanceStatus }

export type CarbonCopyRecipients = ResponseShape<Schemas['CarbonCopyRecipients']>

type ApprovalSimulationStep = Omit<ResponseShape<Schemas['ApprovalSimulationStep']>, 'actions'> & {
  actions?: ResponseShape<Schemas['ApprovalActionPreview']>[] | null
}
export type ApprovalSimulationResult = Omit<ResponseShape<Schemas['ApprovalSimulationResult']>, 'steps'> & { steps: ApprovalSimulationStep[] }
