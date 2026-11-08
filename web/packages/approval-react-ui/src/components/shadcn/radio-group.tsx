import * as React from "react"
import { cn } from "cn"
import { RadioGroup as RadioGroupPrimitive } from "radix-ui"

function RadioGroup({
  className,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Root>) {
  return (
    <RadioGroupPrimitive.Root
      data-slot="radio-group"
      className={cn("approval:grid approval:w-full approval:gap-2", className)}
      {...props}
    />
  )
}

function RadioGroupItem({
  className,
  ...props
}: React.ComponentProps<typeof RadioGroupPrimitive.Item>) {
  return (
    <RadioGroupPrimitive.Item
      data-slot="radio-group-item"
      className={cn(
        "approval:group/radio-group-item approval:peer approval:relative approval:flex approval:aspect-square approval:size-4 approval:shrink-0 approval:rounded-full approval:border approval:border-input approval:outline-none approval:group-has-[:focus-visible]/field-label:ring-0 approval:group-has-[:focus-visible]/field-label:not-data-checked:border-input approval:after:absolute approval:after:-inset-x-3 approval:after:-inset-y-2 approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:disabled:cursor-not-allowed approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:aria-invalid:aria-checked:border-primary approval:dark:bg-input/30 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40 approval:data-checked:border-primary approval:data-checked:bg-primary approval:data-checked:text-primary-foreground approval:group-has-[:focus-visible]/field-label:data-checked:border-primary approval:dark:data-checked:bg-primary",
        className
      )}
      {...props}
    >
      <RadioGroupPrimitive.Indicator
        data-slot="radio-group-indicator"
        className="approval:flex approval:size-4 approval:items-center approval:justify-center"
      >
        <span className="approval:absolute approval:top-1/2 approval:left-1/2 approval:size-2 approval:-translate-x-1/2 approval:-translate-y-1/2 approval:rounded-full approval:bg-primary-foreground" />
      </RadioGroupPrimitive.Indicator>
    </RadioGroupPrimitive.Item>
  )
}

export { RadioGroup, RadioGroupItem }
