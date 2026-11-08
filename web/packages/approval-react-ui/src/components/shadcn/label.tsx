"use client"

import * as React from "react"
import { cn } from "cn"
import { Label as LabelPrimitive } from "radix-ui"

function Label({
  className,
  ...props
}: React.ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      data-slot="label"
      className={cn(
        "approval:flex approval:items-center approval:gap-2 approval:text-sm approval:leading-none approval:font-medium approval:select-none approval:group-data-[disabled=true]:pointer-events-none approval:group-data-[disabled=true]:opacity-50 approval:peer-disabled:cursor-not-allowed approval:peer-disabled:opacity-50",
        className
      )}
      {...props}
    />
  )
}

export { Label }
