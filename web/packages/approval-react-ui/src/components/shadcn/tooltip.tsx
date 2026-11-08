import * as React from "react"
import { cn } from "cn"
import { Tooltip as TooltipPrimitive } from "radix-ui"

function TooltipProvider({
  delayDuration = 0,
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Provider>) {
  return (
    <TooltipPrimitive.Provider
      data-slot="tooltip-provider"
      delayDuration={delayDuration}
      {...props}
    />
  )
}

function Tooltip({
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Root>) {
  return <TooltipPrimitive.Root data-slot="tooltip" {...props} />
}

function TooltipTrigger({
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Trigger>) {
  return <TooltipPrimitive.Trigger data-slot="tooltip-trigger" {...props} />
}

function TooltipContent({
  className,
  sideOffset = 0,
  children,
  ...props
}: React.ComponentProps<typeof TooltipPrimitive.Content>) {
  return (
    <TooltipPrimitive.Portal>
      <TooltipPrimitive.Content
        data-slot="tooltip-content"
        sideOffset={sideOffset}
        className={cn(
          "approval-react approval:z-50 approval:inline-flex approval:w-fit approval:max-w-xs approval:origin-(--radix-tooltip-content-transform-origin) approval:items-center approval:gap-1.5 approval:rounded-md approval:bg-foreground approval:px-3 approval:py-1.5 approval:text-xs approval:text-background approval:has-data-[slot=kbd]:pr-1.5 approval:data-[side=bottom]:slide-in-from-top-2 approval:data-[side=left]:slide-in-from-right-2 approval:data-[side=right]:slide-in-from-left-2 approval:data-[side=top]:slide-in-from-bottom-2 approval:**:data-[slot=kbd]:relative approval:**:data-[slot=kbd]:isolate approval:**:data-[slot=kbd]:z-50 approval:**:data-[slot=kbd]:rounded-sm approval:data-[state=delayed-open]:animate-in approval:data-[state=delayed-open]:fade-in-0 approval:data-[state=delayed-open]:zoom-in-95 approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-open:zoom-in-95 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-closed:zoom-out-95",
          className
        )}
        {...props}
      >
        {children}
        <TooltipPrimitive.Arrow className="approval-react approval:z-50 approval:size-2.5 approval:translate-y-[calc(-50%_-_2px)] approval:rotate-45 approval:rounded-[2px] approval:bg-foreground approval:fill-foreground" />
      </TooltipPrimitive.Content>
    </TooltipPrimitive.Portal>
  )
}

export { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger }
