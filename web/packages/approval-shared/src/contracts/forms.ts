export type FormFieldType =
  | 'Text'
  | 'TextArea'
  | 'Number'
  | 'Money'
  | 'Date'
  | 'DateRange'
  | 'Select'
  | 'MultiSelect'
  | 'Radio'
  | 'Switch'
  | 'User'
  | 'Users'
  | 'Table'
  | 'Description'
  | 'Formula'
  | 'File'

export interface FormFieldOption {
  label: string
  value: string
}

export interface FormField {
  key: string
  label: string
  type: FormFieldType
  required?: boolean
  placeholder?: string | null
  help?: string | null
  defaultValue?: unknown
  /** 栅格宽度（共 24），默认整行 */
  span?: number
  options?: FormFieldOption[]
  min?: number | null
  max?: number | null
  precision?: number | null
  maxLength?: number | null
  maxRows?: number | null
  /** 明细表的列 */
  columns?: FormField[]
  /** 明细表数值列：合计写入顶层字段 {表格}_{列}_sum */
  sum?: boolean
  /** 计算字段的公式，如 `qty * price`、`items_amount_sum - ifnull(advance, 0)` */
  formula?: string | null
  /** 附件字段：最多文件数（默认 10） */
  maxFiles?: number | null
  /** 附件字段：允许的扩展名（不含点），为空时按系统允许的类型 */
  accept?: string[]
}

/** 附件字段的值：上传后由服务端返回 */
export interface FileValue {
  id: string
  name: string
  size: number
  contentType: string
}

export interface FormSchema {
  fields: FormField[]
}

/** 发起设置 */
export interface FlowSettings {
  startable?: boolean
  category?: string | null
  description?: string | null
  initiatorRoles?: string[]
  titleTemplate?: string | null
}

export type FormValues = Record<string, unknown>
