import * as React from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"

const alertVariants = cva(
  "approval:group/alert approval:relative approval:grid approval:w-full approval:gap-0.5 approval:rounded-lg approval:border approval:px-2.5 approval:py-2 approval:text-left approval:text-sm approval:has-data-[slot=alert-action]:relative approval:has-data-[slot=alert-action]:pr-18 approval:has-[>svg]:grid-cols-[auto_1fr] approval:has-[>svg]:gap-x-2 approval:*:[svg]:row-span-2 approval:*:[svg]:translate-y-0.5 approval:*:[svg]:text-current approval:*:[svg:not([class*=size-])]:size-4",
  {
    variants: {
      variant: {
        default: "approval:bg-card approval:text-card-foreground",
        destructive:
          "approval:bg-card approval:text-destructive approval:*:data-[slot=alert-description]:text-destructive/90 approval:*:[svg]:text-current",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  }
)

function Alert({
  className,
  variant,
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof alertVariants>) {
  return (
    <div
      data-slot="alert"
      role="alert"
      className={cn(alertVariants({ variant }), className)}
      {...props}
    />
  )
}

function AlertTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-title"
      className={cn(
        "approval:font-medium approval:group-has-[>svg]/alert:col-start-2 approval:[&_a]:underline approval:[&_a]:underline-offset-3 approval:[&_a]:hover:text-foreground",
        className
      )}
      {...props}
    />
  )
}

function AlertDescription({
  className,
  ...props
}: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-description"
      className={cn(
        "approval:text-sm approval:text-balance approval:text-muted-foreground approval:md:text-pretty approval:[&_a]:underline approval:[&_a]:underline-offset-3 approval:[&_a]:hover:text-foreground approval:[&_p:not(:last-child)]:mb-4",
        className
      )}
      {...props}
    />
  )
}

function AlertAction({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="alert-action"
      className={cn("approval:absolute approval:top-2 approval:right-2", className)}
      {...props}
    />
  )
}

export { Alert, AlertTitle, AlertDescription, AlertAction }
