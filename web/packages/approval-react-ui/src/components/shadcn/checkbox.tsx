import * as React from "react"
import { cn } from "cn"
import { Checkbox as CheckboxPrimitive } from "radix-ui"
import { CheckIcon } from "lucide-react"

function Checkbox({
  className,
  ...props
}: React.ComponentProps<typeof CheckboxPrimitive.Root>) {
  return (
    <CheckboxPrimitive.Root
      data-slot="checkbox"
      className={cn(
        "approval:peer approval:relative approval:flex approval:size-4 approval:shrink-0 approval:items-center approval:justify-center approval:rounded-[4px] approval:border approval:border-input approval:transition-colors approval:outline-none approval:group-has-disabled/field:opacity-50 approval:group-has-[:focus-visible]/field-label:ring-0 approval:group-has-[:focus-visible]/field-label:not-data-checked:border-input approval:after:absolute approval:after:-inset-x-3 approval:after:-inset-y-2 approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:disabled:cursor-not-allowed approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:aria-invalid:aria-checked:border-primary approval:dark:bg-input/30 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40 approval:data-checked:border-primary approval:data-checked:bg-primary approval:data-checked:text-primary-foreground approval:group-has-[:focus-visible]/field-label:data-checked:border-primary approval:dark:data-checked:bg-primary",
        className
      )}
      {...props}
    >
      <CheckboxPrimitive.Indicator
        data-slot="checkbox-indicator"
        className="approval:grid approval:place-content-center approval:text-current approval:transition-none approval:[&>svg]:size-3.5"
      >
        <CheckIcon
        />
      </CheckboxPrimitive.Indicator>
    </CheckboxPrimitive.Root>
  )
}

export { Checkbox }
