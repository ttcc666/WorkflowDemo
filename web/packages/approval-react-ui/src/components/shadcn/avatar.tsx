"use client"

import * as React from "react"
import { cn } from "cn"
import { Avatar as AvatarPrimitive } from "radix-ui"

function Avatar({
  className,
  size = "default",
  ...props
}: React.ComponentProps<typeof AvatarPrimitive.Root> & {
  size?: "default" | "sm" | "lg"
}) {
  return (
    <AvatarPrimitive.Root
      data-slot="avatar"
      data-size={size}
      className={cn(
        "approval:group/avatar approval:relative approval:flex approval:size-8 approval:shrink-0 approval:rounded-full approval:select-none approval:after:absolute approval:after:inset-0 approval:after:rounded-full approval:after:border approval:after:border-border approval:after:mix-blend-darken approval:data-[size=lg]:size-10 approval:data-[size=sm]:size-6 approval:dark:after:mix-blend-lighten",
        className
      )}
      {...props}
    />
  )
}

function AvatarImage({
  className,
  ...props
}: React.ComponentProps<typeof AvatarPrimitive.Image>) {
  return (
    <AvatarPrimitive.Image
      data-slot="avatar-image"
      className={cn(
        "approval:aspect-square approval:size-full approval:rounded-full approval:object-cover",
        className
      )}
      {...props}
    />
  )
}

function AvatarFallback({
  className,
  ...props
}: React.ComponentProps<typeof AvatarPrimitive.Fallback>) {
  return (
    <AvatarPrimitive.Fallback
      data-slot="avatar-fallback"
      className={cn(
        "approval:flex approval:size-full approval:items-center approval:justify-center approval:rounded-full approval:bg-muted approval:text-sm approval:text-muted-foreground approval:group-data-[size=sm]/avatar:text-xs",
        className
      )}
      {...props}
    />
  )
}

function AvatarBadge({ className, ...props }: React.ComponentProps<"span">) {
  return (
    <span
      data-slot="avatar-badge"
      className={cn(
        "approval:absolute approval:right-0 approval:bottom-0 approval:z-10 approval:inline-flex approval:items-center approval:justify-center approval:rounded-full approval:bg-primary approval:text-primary-foreground approval:bg-blend-color approval:ring-2 approval:ring-background approval:select-none",
        "approval:group-data-[size=sm]/avatar:size-2 approval:group-data-[size=sm]/avatar:[&>svg]:hidden",
        "approval:group-data-[size=default]/avatar:size-2.5 approval:group-data-[size=default]/avatar:[&>svg]:size-2",
        "approval:group-data-[size=lg]/avatar:size-3 approval:group-data-[size=lg]/avatar:[&>svg]:size-2",
        className
      )}
      {...props}
    />
  )
}

function AvatarGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="avatar-group"
      className={cn(
        "approval:group/avatar-group approval:flex approval:-space-x-2 approval:*:data-[slot=avatar]:ring-2 approval:*:data-[slot=avatar]:ring-background",
        className
      )}
      {...props}
    />
  )
}

function AvatarGroupCount({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="avatar-group-count"
      className={cn(
        "approval:relative approval:flex approval:size-8 approval:shrink-0 approval:items-center approval:justify-center approval:rounded-full approval:bg-muted approval:text-sm approval:text-muted-foreground approval:ring-2 approval:ring-background approval:group-has-data-[size=lg]/avatar-group:size-10 approval:group-has-data-[size=sm]/avatar-group:size-6 approval:[&>svg]:size-4 approval:group-has-data-[size=lg]/avatar-group:[&>svg]:size-5 approval:group-has-data-[size=sm]/avatar-group:[&>svg]:size-3",
        className
      )}
      {...props}
    />
  )
}

export {
  Avatar,
  AvatarImage,
  AvatarFallback,
  AvatarGroup,
  AvatarGroupCount,
  AvatarBadge,
}
