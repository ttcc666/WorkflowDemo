import * as React from "react"
import { cn } from "cn"

function Textarea({ className, ...props }: React.ComponentProps<"textarea">) {
  return (
    <textarea
      data-slot="textarea"
      className={cn(
        "approval:flex approval:field-sizing-content approval:min-h-16 approval:w-full approval:rounded-lg approval:border approval:border-input approval:bg-transparent approval:px-2.5 approval:py-2 approval:text-base approval:transition-colors approval:outline-none approval:placeholder:text-muted-foreground approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:disabled:cursor-not-allowed approval:disabled:bg-input/50 approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:md:text-sm approval:dark:bg-input/30 approval:dark:disabled:bg-input/80 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40",
        className
      )}
      {...props}
    />
  )
}

export { Textarea }
