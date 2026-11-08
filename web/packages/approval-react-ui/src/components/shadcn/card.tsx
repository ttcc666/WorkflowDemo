import * as React from "react"
import { cn } from "cn"

function Card({
  className,
  size = "default",
  ...props
}: React.ComponentProps<"div"> & { size?: "default" | "sm" }) {
  return (
    <div
      data-slot="card"
      data-size={size}
      className={cn(
        "approval:group/card approval:flex approval:flex-col approval:gap-(--card-spacing) approval:overflow-hidden approval:rounded-xl approval:bg-card approval:py-(--card-spacing) approval:text-sm approval:text-card-foreground approval:ring-1 approval:ring-foreground/10 approval:[--card-spacing:--spacing(4)] approval:has-data-[slot=card-footer]:pb-0 approval:has-[>img:first-child]:pt-0 approval:data-[size=sm]:[--card-spacing:--spacing(3)] approval:data-[size=sm]:has-data-[slot=card-footer]:pb-0 approval:*:[img:first-child]:rounded-t-xl approval:*:[img:last-child]:rounded-b-xl",
        className
      )}
      {...props}
    />
  )
}

function CardHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-header"
      className={cn(
        "approval:group/card-header approval:@container/card-header approval:grid approval:auto-rows-min approval:items-start approval:gap-1 approval:rounded-t-xl approval:px-(--card-spacing) approval:has-data-[slot=card-action]:grid-cols-[1fr_auto] approval:has-data-[slot=card-description]:grid-rows-[auto_auto] approval:[.border-b]:pb-(--card-spacing)",
        className
      )}
      {...props}
    />
  )
}

function CardTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-title"
      className={cn(
        "approval:text-base approval:leading-snug approval:font-medium approval:group-data-[size=sm]/card:text-sm",
        className
      )}
      {...props}
    />
  )
}

function CardDescription({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-description"
      className={cn("approval:text-sm approval:text-muted-foreground", className)}
      {...props}
    />
  )
}

function CardAction({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-action"
      className={cn(
        "approval:col-start-2 approval:row-span-2 approval:row-start-1 approval:self-start approval:justify-self-end",
        className
      )}
      {...props}
    />
  )
}

function CardContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-content"
      className={cn("approval:px-(--card-spacing)", className)}
      {...props}
    />
  )
}

function CardFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="card-footer"
      className={cn(
        "approval:flex approval:items-center approval:rounded-b-xl approval:border-t approval:bg-muted/50 approval:p-(--card-spacing)",
        className
      )}
      {...props}
    />
  )
}

export {
  Card,
  CardHeader,
  CardFooter,
  CardTitle,
  CardAction,
  CardDescription,
  CardContent,
}
