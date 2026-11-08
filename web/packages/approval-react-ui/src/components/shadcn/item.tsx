import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { Slot } from "radix-ui";

import { Separator } from "./separator";

function ItemGroup({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      role="list"
      data-slot="item-group"
      className={cn(
        "approval:group/item-group approval:flex approval:w-full approval:flex-col approval:gap-4 approval:has-data-[size=sm]:gap-2.5 approval:has-data-[size=xs]:gap-2",
        className,
      )}
      {...props}
    />
  );
}

function ItemSeparator({
  className,
  ...props
}: React.ComponentProps<typeof Separator>) {
  return (
    <Separator
      data-slot="item-separator"
      orientation="horizontal"
      className={cn("approval:my-2", className)}
      {...props}
    />
  );
}

const itemVariants = cva(
  "approval:group/item approval:flex approval:w-full approval:flex-wrap approval:items-center approval:rounded-lg approval:border approval:text-sm approval:transition-colors approval:duration-100 approval:outline-none approval:focus-visible:border-ring approval:focus-visible:ring-[3px] approval:focus-visible:ring-ring/50 approval:[a]:transition-colors approval:[a]:hover:bg-muted",
  {
    variants: {
      variant: {
        default: "approval:border-transparent",
        outline: "approval:border-border",
        muted: "approval:border-transparent approval:bg-muted/50",
      },
      size: {
        default: "approval:gap-2.5 approval:px-3 approval:py-2.5",
        sm: "approval:gap-2.5 approval:px-3 approval:py-2.5",
        xs: "approval:gap-2 approval:px-2.5 approval:py-2 approval:in-data-[slot=dropdown-menu-content]:p-0",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

function Item({
  className,
  variant = "default",
  size = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"div"> &
  VariantProps<typeof itemVariants> & { asChild?: boolean }) {
  const Comp = asChild ? Slot.Root : "div";
  return (
    <Comp
      data-slot="item"
      data-variant={variant}
      data-size={size}
      className={cn(itemVariants({ variant, size, className }))}
      {...props}
    />
  );
}

const itemMediaVariants = cva(
  "approval:flex approval:shrink-0 approval:items-center approval:justify-center approval:gap-2 approval:group-has-data-[slot=item-description]/item:translate-y-0.5 approval:group-has-data-[slot=item-description]/item:self-start approval:[&_svg]:pointer-events-none",
  {
    variants: {
      variant: {
        default: "approval:bg-transparent",
        icon: "approval:[&_svg:not([class*=size-])]:size-4",
        image:
          "approval:size-10 approval:overflow-hidden approval:rounded-sm approval:group-data-[size=sm]/item:size-8 approval:group-data-[size=xs]/item:size-6 approval:[&_img]:size-full approval:[&_img]:object-cover",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function ItemMedia({
  className,
  variant = "default",
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof itemMediaVariants>) {
  return (
    <div
      data-slot="item-media"
      data-variant={variant}
      className={cn(itemMediaVariants({ variant, className }))}
      {...props}
    />
  );
}

function ItemContent({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="item-content"
      className={cn(
        "approval:flex approval:flex-1 approval:flex-col approval:gap-1 approval:group-data-[size=xs]/item:gap-0 approval:[&+[data-slot=item-content]]:flex-none",
        className,
      )}
      {...props}
    />
  );
}

function ItemTitle({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="item-title"
      className={cn(
        "approval:line-clamp-1 approval:flex approval:w-fit approval:items-center approval:gap-2 approval:text-sm approval:leading-snug approval:font-medium approval:underline-offset-4",
        className,
      )}
      {...props}
    />
  );
}

function ItemDescription({ className, ...props }: React.ComponentProps<"p">) {
  return (
    <p
      data-slot="item-description"
      className={cn(
        "approval:line-clamp-2 approval:text-left approval:text-sm approval:leading-normal approval:font-normal approval:text-muted-foreground approval:group-data-[size=xs]/item:text-xs approval:[&>a]:underline approval:[&>a]:underline-offset-4 approval:[&>a:hover]:text-primary",
        className,
      )}
      {...props}
    />
  );
}

function ItemActions({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="item-actions"
      className={cn(
        "approval:flex approval:items-center approval:gap-2",
        className,
      )}
      {...props}
    />
  );
}

function ItemHeader({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="item-header"
      className={cn(
        "approval:flex approval:basis-full approval:items-center approval:justify-between approval:gap-2",
        className,
      )}
      {...props}
    />
  );
}

function ItemFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="item-footer"
      className={cn(
        "approval:flex approval:basis-full approval:items-center approval:justify-between approval:gap-2",
        className,
      )}
      {...props}
    />
  );
}

export {
  Item,
  ItemMedia,
  ItemContent,
  ItemActions,
  ItemGroup,
  ItemSeparator,
  ItemTitle,
  ItemDescription,
  ItemHeader,
  ItemFooter,
};
