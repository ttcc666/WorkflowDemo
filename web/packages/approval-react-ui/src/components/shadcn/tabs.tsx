"use client"

import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"
import { Tabs as TabsPrimitive } from "radix-ui"

function Tabs({
  className,
  orientation = "horizontal",
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Root>) {
  return (
    <TabsPrimitive.Root
      data-slot="tabs"
      data-orientation={orientation}
      className={cn(
        "approval:group/tabs approval:flex approval:gap-2 approval:data-horizontal:flex-col",
        className
      )}
      {...props}
    />
  )
}

const tabsListVariants = cva(
  "approval:group/tabs-list approval:inline-flex approval:w-fit approval:items-center approval:justify-center approval:rounded-lg approval:p-[3px] approval:text-muted-foreground approval:group-data-horizontal/tabs:h-8 approval:group-data-vertical/tabs:h-fit approval:group-data-vertical/tabs:flex-col approval:data-[variant=line]:rounded-none",
  {
    variants: {
      variant: {
        default: "approval:bg-muted",
        line: "approval:gap-1 approval:bg-transparent",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function TabsList({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<typeof TabsPrimitive.List> &
  VariantProps<typeof tabsListVariants>) {
  return (
    <TabsPrimitive.List
      data-slot="tabs-list"
      data-variant={variant}
      className={cn(tabsListVariants({ variant }), className)}
      {...props}
    />
  )
}

function TabsTrigger({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      data-slot="tabs-trigger"
      className={cn(
        "approval:relative approval:inline-flex approval:h-[calc(100%-1px)] approval:flex-1 approval:items-center approval:justify-center approval:gap-1.5 approval:rounded-md approval:border approval:border-transparent approval:px-1.5 approval:py-0.5 approval:text-sm approval:font-medium approval:whitespace-nowrap approval:text-foreground/60 approval:transition-all approval:group-data-vertical/tabs:w-full approval:group-data-vertical/tabs:justify-start approval:hover:text-foreground approval:focus-visible:border-ring approval:focus-visible:ring-[3px] approval:focus-visible:ring-ring/50 approval:focus-visible:outline-1 approval:focus-visible:outline-ring approval:disabled:pointer-events-none approval:disabled:opacity-50 approval:has-data-[icon=inline-end]:pr-1 approval:has-data-[icon=inline-start]:pl-1 approval:dark:text-muted-foreground approval:dark:hover:text-foreground approval:group-data-[variant=default]/tabs-list:data-active:shadow-sm approval:group-data-[variant=line]/tabs-list:data-active:shadow-none approval:[&_svg]:pointer-events-none approval:[&_svg]:shrink-0 approval:[&_svg:not([class*=size-])]:size-4",
        "approval:group-data-[variant=line]/tabs-list:bg-transparent approval:group-data-[variant=line]/tabs-list:data-active:bg-transparent approval:dark:group-data-[variant=line]/tabs-list:data-active:border-transparent approval:dark:group-data-[variant=line]/tabs-list:data-active:bg-transparent",
        "approval:data-active:bg-background approval:data-active:text-foreground approval:dark:data-active:border-input approval:dark:data-active:bg-input/30 approval:dark:data-active:text-foreground",
        "approval:after:absolute approval:after:bg-foreground approval:after:opacity-0 approval:after:transition-opacity approval:group-data-horizontal/tabs:after:inset-x-0 approval:group-data-horizontal/tabs:after:bottom-[-5px] approval:group-data-horizontal/tabs:after:h-0.5 approval:group-data-vertical/tabs:after:inset-y-0 approval:group-data-vertical/tabs:after:-right-1 approval:group-data-vertical/tabs:after:w-0.5 approval:group-data-[variant=line]/tabs-list:data-active:after:opacity-100",
        className
      )}
      {...props}
    />
  )
}

function TabsContent({
  className,
  ...props
}: React.ComponentProps<typeof TabsPrimitive.Content>) {
  return (
    <TabsPrimitive.Content
      data-slot="tabs-content"
      className={cn("approval:flex-1 approval:text-sm approval:outline-none", className)}
      {...props}
    />
  )
}

export { Tabs, TabsList, TabsTrigger, TabsContent, tabsListVariants }
