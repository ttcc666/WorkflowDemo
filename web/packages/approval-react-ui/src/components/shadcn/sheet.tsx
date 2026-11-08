import * as React from "react"
import { cn } from "cn"
import { Dialog as SheetPrimitive } from "radix-ui"

import { Button } from "./button"
import { XIcon } from "lucide-react"

function Sheet({ ...props }: React.ComponentProps<typeof SheetPrimitive.Root>) {
  return <SheetPrimitive.Root data-slot="sheet" {...props} />
}

function SheetTrigger({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Trigger>) {
  return <SheetPrimitive.Trigger data-slot="sheet-trigger" {...props} />
}

function SheetClose({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Close>) {
  return <SheetPrimitive.Close data-slot="sheet-close" {...props} />
}

function SheetPortal({
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Portal>) {
  return <SheetPrimitive.Portal data-slot="sheet-portal" {...props} />
}

function SheetOverlay({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Overlay>) {
  return (
    <SheetPrimitive.Overlay
      data-slot="sheet-overlay"
      className={cn(
        "approval-react approval:fixed approval:inset-0 approval:z-50 approval:bg-black/10 approval:duration-100 approval:supports-backdrop-filter:backdrop-blur-xs approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-closed:animate-out approval:data-closed:fade-out-0",
        className
      )}
      {...props}
    />
  )
}

function SheetContent({
  className,
  children,
  side = "right",
  showCloseButton = true,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Content> & {
  side?: "top" | "right" | "bottom" | "left"
  showCloseButton?: boolean
}) {
  return (
    <SheetPortal>
      <SheetOverlay />
      <SheetPrimitive.Content
        data-slot="sheet-content"
        data-side={side}
        className={cn(
          "approval-react approval:fixed approval:z-50 approval:flex approval:flex-col approval:gap-4 approval:bg-popover approval:bg-clip-padding approval:text-sm approval:text-popover-foreground approval:shadow-lg approval:transition approval:duration-200 approval:ease-in-out approval:data-[side=bottom]:inset-x-0 approval:data-[side=bottom]:bottom-0 approval:data-[side=bottom]:h-auto approval:data-[side=bottom]:border-t approval:data-[side=left]:inset-y-0 approval:data-[side=left]:left-0 approval:data-[side=left]:h-full approval:data-[side=left]:w-3/4 approval:data-[side=left]:border-r approval:data-[side=right]:inset-y-0 approval:data-[side=right]:right-0 approval:data-[side=right]:h-full approval:data-[side=right]:w-3/4 approval:data-[side=right]:border-l approval:data-[side=top]:inset-x-0 approval:data-[side=top]:top-0 approval:data-[side=top]:h-auto approval:data-[side=top]:border-b approval:data-[side=left]:sm:max-w-sm approval:data-[side=right]:sm:max-w-sm approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-[side=bottom]:data-open:slide-in-from-bottom-10 approval:data-[side=left]:data-open:slide-in-from-left-10 approval:data-[side=right]:data-open:slide-in-from-right-10 approval:data-[side=top]:data-open:slide-in-from-top-10 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-[side=bottom]:data-closed:slide-out-to-bottom-10 approval:data-[side=left]:data-closed:slide-out-to-left-10 approval:data-[side=right]:data-closed:slide-out-to-right-10 approval:data-[side=top]:data-closed:slide-out-to-top-10",
          className
        )}
        {...props}
      >
        {children}
        {showCloseButton && (
          <SheetPrimitive.Close data-slot="sheet-close" asChild>
            <Button
              variant="ghost"
              className="approval:absolute approval:top-3 approval:right-3"
              size="icon-sm"
            >
              <XIcon
              />
              <span className="approval:sr-only">Close</span>
            </Button>
          </SheetPrimitive.Close>
        )}
      </SheetPrimitive.Content>
    </SheetPortal>
  )
}

function SheetHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sheet-header"
      className={cn("approval:flex approval:flex-col approval:gap-0.5 approval:p-4", className)}
      {...props}
    />
  )
}

function SheetFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="sheet-footer"
      className={cn("approval:mt-auto approval:flex approval:flex-col approval:gap-2 approval:p-4", className)}
      {...props}
    />
  )
}

function SheetTitle({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Title>) {
  return (
    <SheetPrimitive.Title
      data-slot="sheet-title"
      className={cn(
        "approval:text-base approval:font-medium approval:text-foreground",
        className
      )}
      {...props}
    />
  )
}

function SheetDescription({
  className,
  ...props
}: React.ComponentProps<typeof SheetPrimitive.Description>) {
  return (
    <SheetPrimitive.Description
      data-slot="sheet-description"
      className={cn("approval:text-sm approval:text-muted-foreground", className)}
      {...props}
    />
  )
}

export {
  Sheet,
  SheetTrigger,
  SheetClose,
  SheetContent,
  SheetHeader,
  SheetFooter,
  SheetTitle,
  SheetDescription,
}
