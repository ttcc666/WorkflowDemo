import type { FormField } from '@ui/contracts/forms'
import { textTypes, numberTypes } from './formFields'
import { validateValue } from '@approval/shared/features/forms/formValidation'
export * from '@approval/shared/features/forms/formValidation'
export interface FieldRule {
  required?: boolean
  message?: string
  trigger?: string | string[]
  validator?: (rule: unknown, value: unknown, callback: (error?: Error) => void) => void
}
export function fieldRules(field: FormField): FieldRule[] {
  if (field.type === 'Description' || field.type === 'Formula') return []
  return [
    {
      // required 只用于显示星号；实际校验在 validator 中完成，与服务端提示一致
      required: field.type === 'Table' ? false : !!field.required,
      trigger: textTypes.has(field.type) || numberTypes.has(field.type) ? 'blur' : 'change',
      validator: (_rule, value, callback) => {
        const error = validateValue(field, value)
        if (error) callback(new Error(error))
        else callback()
      },
    },
  ]
}
