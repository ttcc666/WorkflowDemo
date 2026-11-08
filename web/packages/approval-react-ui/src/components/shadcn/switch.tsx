"use client"

import * as React from "react"
import { cn } from "cn"
import { Switch as SwitchPrimitive } from "radix-ui"

function Switch({
  className,
  size = "default",
  ...props
}: React.ComponentProps<typeof SwitchPrimitive.Root> & {
  size?: "sm" | "default"
}) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      data-size={size}
      className={cn(
        "approval:peer approval:group/switch approval:relative approval:inline-flex approval:shrink-0 approval:items-center approval:rounded-full approval:border approval:border-transparent approval:transition-all approval:outline-none approval:group-has-[:focus-visible]/field-label:border-transparent approval:group-has-[:focus-visible]/field-label:ring-0 approval:after:absolute approval:after:-inset-x-3 approval:after:-inset-y-2 approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:data-[size=default]:h-[18.4px] approval:data-[size=default]:w-[32px] approval:data-[size=sm]:h-[14px] approval:data-[size=sm]:w-[24px] approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40 approval:data-checked:bg-primary approval:data-unchecked:bg-input approval:dark:data-unchecked:bg-input/80 approval:data-disabled:cursor-not-allowed approval:data-disabled:opacity-50",
        className
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className="approval:pointer-events-none approval:block approval:rounded-full approval:bg-background approval:ring-0 approval:transition-transform approval:group-data-[size=default]/switch:size-4 approval:group-data-[size=sm]/switch:size-3 approval:group-data-[size=default]/switch:data-checked:translate-x-[calc(100%-2px)] approval:group-data-[size=sm]/switch:data-checked:translate-x-[calc(100%-2px)] approval:dark:data-checked:bg-primary-foreground approval:group-data-[size=default]/switch:data-unchecked:translate-x-0 approval:group-data-[size=sm]/switch:data-unchecked:translate-x-0 approval:dark:data-unchecked:bg-foreground"
      />
    </SwitchPrimitive.Root>
  )
}

export { Switch }
