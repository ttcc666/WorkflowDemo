import * as React from "react"
import { cn } from "cn"

function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        "approval:h-8 approval:w-full approval:min-w-0 approval:rounded-lg approval:border approval:border-input approval:bg-transparent approval:px-2.5 approval:py-1 approval:text-base approval:transition-colors approval:outline-none approval:file:inline-flex approval:file:h-6 approval:file:border-0 approval:file:bg-transparent approval:file:text-sm approval:file:font-medium approval:file:text-foreground approval:placeholder:text-muted-foreground approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:disabled:pointer-events-none approval:disabled:cursor-not-allowed approval:disabled:bg-input/50 approval:disabled:opacity-50 approval:aria-invalid:border-destructive approval:aria-invalid:ring-3 approval:aria-invalid:ring-destructive/20 approval:md:text-sm approval:dark:bg-input/30 approval:dark:disabled:bg-input/80 approval:dark:aria-invalid:border-destructive/50 approval:dark:aria-invalid:ring-destructive/40",
        className
      )}
      {...props}
    />
  )
}

export { Input }
