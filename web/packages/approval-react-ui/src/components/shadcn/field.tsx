"use client"

import { useMemo } from "react"
import { cva, type VariantProps } from "class-variance-authority"
import { cn } from "cn"

import { Label } from "./label"
import { Separator } from "./separator"

function FieldSet({ className, ...props }: React.ComponentProps<"fieldset">) {
  return (
    <fieldset
      data-slot="field-set"
      className={cn(
        "approval:flex approval:flex-col approval:gap-4 approval:has-[>[data-slot=checkbox-group]]:gap-3 approval:has-[>[data-slot=radio-group]]:gap-3",
        className
      )}
      {...props}
    />
  )
}

function FieldLegend({
  className,
  variant = "legend",
  ...props
}: React.ComponentProps<"legend"> & { variant?: "legend" | "label" }) {
  return (
    <legend
      data-slot="field-legend"
      data-variant={variant}
      className={cn(
        "approval:mb-1.5 approval:font-medium approval:data-[variant=label]:text-sm approval:data-[variant=legend]:text-base",
        className
      )}
      {...props}
    />
  )
}

function FieldGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="field-group"
      className={cn(
        "approval:group/field-group approval:@container/field-group approval:flex approval:w-full approval:flex-col approval:gap-5 approval:data-[slot=checkbox-group]:gap-3 approval:*:data-[slot=field-group]:gap-4",
        className
      )}
      {...props}
    />
  )
}

const fieldVariants = cva(
  "approval:group/field approval:flex approval:w-full approval:gap-2 approval:data-[invalid=true]:text-destructive",
  {
    variants: {
      orientation: {
        vertical: "approval:flex-col approval:*:w-full approval:[&>.sr-only]:w-auto",
        horizontal:
          "approval:flex-row approval:items-center approval:has-[>[data-slot=field-content]]:items-start approval:*:data-[slot=field-label]:flex-auto approval:has-[>[data-slot=field-content]]:[&>[role=checkbox],[role=radio]]:mt-px",
        responsive:
          "approval:flex-col approval:*:w-full approval:@md/field-group:flex-row approval:@md/field-group:items-center approval:@md/field-group:*:w-auto approval:@md/field-group:has-[>[data-slot=field-content]]:items-start approval:@md/field-group:*:data-[slot=field-label]:flex-auto approval:[&>.sr-only]:w-auto approval:@md/field-group:has-[>[data-slot=field-content]]:[&>[role=checkbox],[role=radio]]:mt-px",
      },
    },
    defaultVariants: {
      orientation: "vertical",
    },
  }
)

function Field({
  className,
  orientation = "vertical",
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof fieldVariants>) {
  return (
    <div
      role="group"
      data-slot="field"
      data-orientation={orientation}
      className={cn(fieldVariants({ orientation }), className)}
      {...props}
    />
  )
}

function FieldContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="field-content"
      className={cn(
        "approval:group/field-content approval:flex approval:flex-1 approval:flex-col approval:gap-0.5 approval:leading-snug",
        className
      )}
      {...props}
    />
  )
}

function FieldLabel({
  className,
  ...props
}: React.ComponentProps<typeof Label>) {
  return (
    <Label
      data-slot="field-label"
      className={cn(
        "approval:group/field-label approval:peer/field-label approval:flex approval:w-fit approval:gap-2 approval:leading-snug approval:group-data-[disabled=true]/field:opacity-50 approval:has-data-checked:border-primary/30 approval:has-data-checked:bg-primary/5 approval:has-[>[data-slot=field]]:rounded-lg approval:has-[>[data-slot=field]]:border approval:has-[>[data-slot=field]]:not-has-[:disabled,[data-disabled]]:hover:bg-muted/50 approval:has-[>[data-slot=field]]:has-[:focus-visible]:border-ring approval:has-[>[data-slot=field]]:has-[:focus-visible]:ring-3 approval:has-[>[data-slot=field]]:has-[:focus-visible]:ring-ring/50 approval:*:data-[slot=field]:p-2.5 approval:dark:has-data-checked:border-primary/20 approval:dark:has-data-checked:bg-primary/10",
        "approval:has-[>[data-slot=field]]:w-full approval:has-[>[data-slot=field]]:flex-col",
        className
      )}
      {...props}
    />
  )
}

function FieldTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="field-label"
      className={cn(
        "approval:flex approval:w-fit approval:items-center approval:gap-2 approval:text-sm approval:font-medium approval:group-data-[disabled=true]/field:opacity-50",
        className
      )}
      {...props}
    />
  )
}

function FieldDescription({ className, ...props }: React.ComponentProps<"p">) {
  return (
    <p
      data-slot="field-description"
      className={cn(
        "approval:text-left approval:text-sm approval:leading-normal approval:font-normal approval:text-muted-foreground approval:group-has-data-horizontal/field:text-balance approval:[[data-variant=legend]+&]:-mt-1.5",
        "approval:last:mt-0 approval:nth-last-2:-mt-1",
        "approval:[&>a]:underline approval:[&>a]:underline-offset-4 approval:[&>a:hover]:text-primary",
        className
      )}
      {...props}
    />
  )
}

function FieldSeparator({
  children,
  className,
  ...props
}: React.ComponentProps<"div"> & {
  children?: React.ReactNode
}) {
  return (
    <div
      data-slot="field-separator"
      data-content={!!children}
      className={cn(
        "approval:relative approval:-my-2 approval:h-5 approval:text-sm approval:group-data-[variant=outline]/field-group:-mb-2",
        className
      )}
      {...props}
    >
      <Separator className="approval:absolute approval:inset-0 approval:top-1/2" />
      {children && (
        <span
          className="approval:relative approval:mx-auto approval:block approval:w-fit approval:bg-background approval:px-2 approval:text-muted-foreground"
          data-slot="field-separator-content"
        >
          {children}
        </span>
      )}
    </div>
  )
}

function FieldError({
  className,
  children,
  errors,
  ...props
}: React.ComponentProps<"div"> & {
  errors?: Array<{ message?: string } | undefined>
}) {
  const content = useMemo(() => {
    if (children) {
      return children
    }

    if (!errors?.length) {
      return null
    }

    const uniqueErrors = [
      ...new Map(errors.map((error) => [error?.message, error])).values(),
    ]

    if (uniqueErrors?.length == 1) {
      return uniqueErrors[0]?.message
    }

    return (
      <ul className="approval:ml-4 approval:flex approval:list-disc approval:flex-col approval:gap-1">
        {uniqueErrors.map(
          (error, index) =>
            error?.message && <li key={index}>{error.message}</li>
        )}
      </ul>
    )
  }, [children, errors])

  if (!content) {
    return null
  }

  return (
    <div
      role="alert"
      data-slot="field-error"
      className={cn("approval:text-sm approval:font-normal approval:text-destructive", className)}
      {...props}
    >
      {content}
    </div>
  )
}

export {
  Field,
  FieldLabel,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLegend,
  FieldSeparator,
  FieldSet,
  FieldContent,
  FieldTitle,
}
