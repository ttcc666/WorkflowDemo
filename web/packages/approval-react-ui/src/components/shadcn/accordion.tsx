"use client";

import * as React from "react";
import { cn } from "cn";
import { Accordion as AccordionPrimitive } from "radix-ui";
import { ChevronDownIcon, ChevronUpIcon } from "lucide-react";

function Accordion({
  className,
  ...props
}: React.ComponentProps<typeof AccordionPrimitive.Root>) {
  return (
    <AccordionPrimitive.Root
      data-slot="accordion"
      className={cn(
        "approval:flex approval:w-full approval:flex-col",
        className,
      )}
      {...props}
    />
  );
}

function AccordionItem({
  className,
  ...props
}: React.ComponentProps<typeof AccordionPrimitive.Item>) {
  return (
    <AccordionPrimitive.Item
      data-slot="accordion-item"
      className={cn("approval:not-last:border-b", className)}
      {...props}
    />
  );
}

function AccordionTrigger({
  className,
  children,
  ...props
}: React.ComponentProps<typeof AccordionPrimitive.Trigger>) {
  return (
    <AccordionPrimitive.Header className="approval:flex">
      <AccordionPrimitive.Trigger
        data-slot="accordion-trigger"
        className={cn(
          "approval:group/accordion-trigger approval:relative approval:flex approval:flex-1 approval:items-start approval:justify-between approval:rounded-lg approval:border approval:border-transparent approval:py-2.5 approval:text-left approval:text-sm approval:font-medium approval:transition-all approval:outline-none approval:hover:underline approval:focus-visible:border-ring approval:focus-visible:ring-3 approval:focus-visible:ring-ring/50 approval:focus-visible:after:border-ring approval:disabled:pointer-events-none approval:disabled:opacity-50 approval:**:data-[slot=accordion-trigger-icon]:ml-auto approval:**:data-[slot=accordion-trigger-icon]:size-4 approval:**:data-[slot=accordion-trigger-icon]:text-muted-foreground",
          className,
        )}
        {...props}
      >
        {children}
        <ChevronDownIcon
          data-slot="accordion-trigger-icon"
          className="approval:pointer-events-none approval:shrink-0 approval:group-aria-expanded/accordion-trigger:hidden"
        />
        <ChevronUpIcon
          data-slot="accordion-trigger-icon"
          className="approval:pointer-events-none approval:hidden approval:shrink-0 approval:group-aria-expanded/accordion-trigger:inline"
        />
      </AccordionPrimitive.Trigger>
    </AccordionPrimitive.Header>
  );
}

function AccordionContent({
  className,
  children,
  ...props
}: React.ComponentProps<typeof AccordionPrimitive.Content>) {
  return (
    <AccordionPrimitive.Content
      data-slot="accordion-content"
      className="approval:overflow-hidden approval:text-sm approval:data-open:animate-accordion-down approval:data-closed:animate-accordion-up"
      {...props}
    >
      <div
        className={cn(
          "approval:h-(--radix-accordion-content-height) approval:pt-0 approval:pb-2.5 approval:[&_a]:underline approval:[&_a]:underline-offset-3 approval:[&_a]:hover:text-foreground approval:[&_p:not(:last-child)]:mb-4",
          className,
        )}
      >
        {children}
      </div>
    </AccordionPrimitive.Content>
  );
}

export { Accordion, AccordionItem, AccordionTrigger, AccordionContent };
