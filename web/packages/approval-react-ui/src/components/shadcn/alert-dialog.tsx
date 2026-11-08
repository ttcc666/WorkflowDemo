"use client"

import * as React from "react"
import { cn } from "cn"
import { AlertDialog as AlertDialogPrimitive } from "radix-ui"

import { Button } from "./button"

function AlertDialog({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Root>) {
  return <AlertDialogPrimitive.Root data-slot="alert-dialog" {...props} />
}

function AlertDialogTrigger({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Trigger>) {
  return (
    <AlertDialogPrimitive.Trigger data-slot="alert-dialog-trigger" {...props} />
  )
}

function AlertDialogPortal({
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Portal>) {
  return (
    <AlertDialogPrimitive.Portal data-slot="alert-dialog-portal" {...props} />
  )
}

function AlertDialogOverlay({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Overlay>) {
  return (
    <AlertDialogPrimitive.Overlay
      data-slot="alert-dialog-overlay"
      className={cn(
        "approval-react approval:fixed approval:inset-0 approval:z-50 approval:bg-black/10 approval:duration-100 approval:supports-backdrop-filter:backdrop-blur-xs approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-closed:animate-out approval:data-closed:fade-out-0",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogContent({
  className,
  size = "default",
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Content> & {
  size?: "default" | "sm"
}) {
  return (
    <AlertDialogPortal>
      <AlertDialogOverlay />
      <AlertDialogPrimitive.Content
        data-slot="alert-dialog-content"
        data-size={size}
        className={cn(
          "approval:group/alert-dialog-content approval:fixed approval:top-1/2 approval:left-1/2 approval:z-50 approval:grid approval:w-full approval:-translate-x-1/2 approval:-translate-y-1/2 approval:gap-4 approval:rounded-xl approval:bg-popover approval:p-4 approval:text-popover-foreground approval:ring-1 approval:ring-foreground/10 approval:duration-100 approval:outline-none approval:data-[size=default]:max-w-xs approval:data-[size=sm]:max-w-xs approval:data-[size=default]:sm:max-w-sm approval:data-open:animate-in approval:data-open:fade-in-0 approval:data-open:zoom-in-95 approval:data-closed:animate-out approval:data-closed:fade-out-0 approval:data-closed:zoom-out-95",
          className
        )}
        {...props}
      />
    </AlertDialogPortal>
  )
}

function AlertDialogHeader({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-dialog-header"
      className={cn(
        "approval:grid approval:grid-rows-[auto_1fr] approval:place-items-center approval:gap-1.5 approval:text-center approval:has-data-[slot=alert-dialog-media]:grid-rows-[auto_auto_1fr] approval:has-data-[slot=alert-dialog-media]:gap-x-4 approval:sm:group-data-[size=default]/alert-dialog-content:place-items-start approval:sm:group-data-[size=default]/alert-dialog-content:text-left approval:sm:group-data-[size=default]/alert-dialog-content:has-data-[slot=alert-dialog-media]:grid-rows-[auto_1fr]",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogFooter({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-dialog-footer"
      className={cn(
        "approval:-mx-4 approval:-mb-4 approval:flex approval:flex-col-reverse approval:gap-2 approval:rounded-b-xl approval:border-t approval:bg-muted/50 approval:p-4 approval:group-data-[size=sm]/alert-dialog-content:grid approval:group-data-[size=sm]/alert-dialog-content:grid-cols-2 approval:sm:flex-row approval:sm:justify-end",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogMedia({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-dialog-media"
      className={cn(
        "approval:mb-2 approval:inline-flex approval:size-10 approval:items-center approval:justify-center approval:rounded-md approval:bg-muted approval:sm:group-data-[size=default]/alert-dialog-content:row-span-2 approval:*:[svg:not([class*=size-])]:size-6",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogTitle({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Title>) {
  return (
    <AlertDialogPrimitive.Title
      data-slot="alert-dialog-title"
      className={cn(
        "approval:text-base approval:font-medium approval:sm:group-data-[size=default]/alert-dialog-content:group-has-data-[slot=alert-dialog-media]/alert-dialog-content:col-start-2",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogDescription({
  className,
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Description>) {
  return (
    <AlertDialogPrimitive.Description
      data-slot="alert-dialog-description"
      className={cn(
        "approval:text-sm approval:text-balance approval:text-muted-foreground approval:md:text-pretty approval:*:[a]:underline approval:*:[a]:underline-offset-3 approval:*:[a]:hover:text-foreground",
        className
      )}
      {...props}
    />
  )
}

function AlertDialogAction({
  className,
  variant = "default",
  size = "default",
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Action> &
  Pick<React.ComponentProps<typeof Button>, "variant" | "size">) {
  return (
    <Button variant={variant} size={size} asChild>
      <AlertDialogPrimitive.Action
        data-slot="alert-dialog-action"
        className={cn(className)}
        {...props}
      />
    </Button>
  )
}

function AlertDialogCancel({
  className,
  variant = "outline",
  size = "default",
  ...props
}: React.ComponentProps<typeof AlertDialogPrimitive.Cancel> &
  Pick<React.ComponentProps<typeof Button>, "variant" | "size">) {
  return (
    <Button variant={variant} size={size} asChild>
      <AlertDialogPrimitive.Cancel
        data-slot="alert-dialog-cancel"
        className={cn(className)}
        {...props}
      />
    </Button>
  )
}

export {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogMedia,
  AlertDialogOverlay,
  AlertDialogPortal,
  AlertDialogTitle,
  AlertDialogTrigger,
}
