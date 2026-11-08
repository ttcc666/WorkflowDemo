import * as React from "react"
import { cn } from "cn"
import { Separator as SeparatorPrimitive } from "radix-ui"

function Separator({
  className,
  orientation = "horizontal",
  decorative = true,
  ...props
}: React.ComponentProps<typeof SeparatorPrimitive.Root>) {
  return (
    <SeparatorPrimitive.Root
      data-slot="separator"
      decorative={decorative}
      orientation={orientation}
      className={cn(
        "approval:shrink-0 approval:bg-border approval:data-horizontal:h-px approval:data-horizontal:w-full approval:data-vertical:w-px approval:data-vertical:self-stretch",
        className
      )}
      {...props}
    />
  )
}

export { Separator }
