"use client"

import * as React from "react"
import { cn } from "cn"
import {
  DayPicker,
  getDefaultClassNames,
  type DayButton,
  type Locale,
} from "react-day-picker"

import { Button, buttonVariants } from "./button"
import { ChevronLeftIcon, ChevronRightIcon, ChevronDownIcon } from "lucide-react"

function Calendar({
  className,
  classNames,
  showOutsideDays = true,
  captionLayout = "label",
  buttonVariant = "ghost",
  locale,
  formatters,
  components,
  ...props
}: React.ComponentProps<typeof DayPicker> & {
  buttonVariant?: React.ComponentProps<typeof Button>["variant"]
}) {
  const defaultClassNames = getDefaultClassNames()

  return (
    <DayPicker
      showOutsideDays={showOutsideDays}
      className={cn(
        "approval:group/calendar approval:bg-background approval:p-2 approval:[--cell-radius:var(--radius-md)] approval:[--cell-size:--spacing(7)] approval:in-data-[slot=card-content]:bg-transparent approval:in-data-[slot=popover-content]:bg-transparent",
        String.raw`rtl:**:[.rdp-button\_next>svg]:rotate-180`,
        String.raw`rtl:**:[.rdp-button\_previous>svg]:rotate-180`,
        className
      )}
      captionLayout={captionLayout}
      locale={locale}
      formatters={{
        formatMonthDropdown: (date) =>
          date.toLocaleString(locale?.code, { month: "short" }),
        ...formatters,
      }}
      classNames={{
        root: cn("approval:w-fit", defaultClassNames.root),
        months: cn(
          "approval:relative approval:flex approval:flex-col approval:gap-4 approval:md:flex-row",
          defaultClassNames.months
        ),
        month: cn("approval:flex approval:w-full approval:flex-col approval:gap-4", defaultClassNames.month),
        nav: cn(
          "approval:absolute approval:inset-x-0 approval:top-0 approval:flex approval:w-full approval:items-center approval:justify-between approval:gap-1",
          defaultClassNames.nav
        ),
        button_previous: cn(
          buttonVariants({ variant: buttonVariant }),
          "approval:size-(--cell-size) approval:p-0 approval:select-none approval:aria-disabled:opacity-50",
          defaultClassNames.button_previous
        ),
        button_next: cn(
          buttonVariants({ variant: buttonVariant }),
          "approval:size-(--cell-size) approval:p-0 approval:select-none approval:aria-disabled:opacity-50",
          defaultClassNames.button_next
        ),
        month_caption: cn(
          "approval:flex approval:h-(--cell-size) approval:w-full approval:items-center approval:justify-center approval:px-(--cell-size)",
          defaultClassNames.month_caption
        ),
        dropdowns: cn(
          "approval:flex approval:h-(--cell-size) approval:w-full approval:items-center approval:justify-center approval:gap-1.5 approval:text-sm approval:font-medium",
          defaultClassNames.dropdowns
        ),
        dropdown_root: cn(
          "approval:relative approval:rounded-(--cell-radius)",
          defaultClassNames.dropdown_root
        ),
        dropdown: cn(
          "approval:absolute approval:inset-0 approval:bg-popover approval:opacity-0",
          defaultClassNames.dropdown
        ),
        caption_label: cn(
          "approval:font-medium approval:select-none",
          captionLayout === "label"
            ? "approval:text-sm"
            : "approval:flex approval:items-center approval:gap-1 approval:rounded-(--cell-radius) approval:text-sm approval:[&>svg]:size-3.5 approval:[&>svg]:text-muted-foreground",
          defaultClassNames.caption_label
        ),
        month_grid: cn("approval:w-full approval:border-collapse", defaultClassNames.month_grid),
        weekdays: cn("approval:flex", defaultClassNames.weekdays),
        weekday: cn(
          "approval:flex-1 approval:rounded-(--cell-radius) approval:text-[0.8rem] approval:font-normal approval:text-muted-foreground approval:select-none",
          defaultClassNames.weekday
        ),
        week: cn("approval:mt-2 approval:flex approval:w-full", defaultClassNames.week),
        week_number_header: cn(
          "approval:w-(--cell-size) approval:select-none",
          defaultClassNames.week_number_header
        ),
        week_number: cn(
          "approval:text-[0.8rem] approval:text-muted-foreground approval:select-none",
          defaultClassNames.week_number
        ),
        day: cn(
          "approval:group/day approval:relative approval:aspect-square approval:h-full approval:w-full approval:rounded-(--cell-radius) approval:p-0 approval:text-center approval:select-none approval:[&:last-child[data-selected=true]_button]:rounded-r-(--cell-radius)",
          props.showWeekNumber
            ? "approval:[&:nth-child(2)[data-selected=true]_button]:rounded-l-(--cell-radius)"
            : "approval:[&:first-child[data-selected=true]_button]:rounded-l-(--cell-radius)",
          defaultClassNames.day
        ),
        range_start: cn(
          "approval:relative approval:isolate approval:z-0 approval:rounded-l-(--cell-radius) approval:bg-muted approval:after:absolute approval:after:inset-y-0 approval:after:right-0 approval:after:w-4 approval:after:bg-muted",
          defaultClassNames.range_start
        ),
        range_middle: cn("approval:rounded-none", defaultClassNames.range_middle),
        range_end: cn(
          "approval:relative approval:isolate approval:z-0 approval:rounded-r-(--cell-radius) approval:bg-muted approval:after:absolute approval:after:inset-y-0 approval:after:left-0 approval:after:w-4 approval:after:bg-muted",
          defaultClassNames.range_end
        ),
        today: cn(
          "approval:rounded-(--cell-radius) approval:bg-muted approval:text-foreground approval:data-[selected=true]:rounded-none",
          defaultClassNames.today
        ),
        outside: cn(
          "approval:text-muted-foreground approval:aria-selected:text-muted-foreground",
          defaultClassNames.outside
        ),
        disabled: cn(
          "approval:text-muted-foreground approval:opacity-50",
          defaultClassNames.disabled
        ),
        hidden: cn("approval:invisible", defaultClassNames.hidden),
        ...classNames,
      }}
      components={{
        Root: ({ className, rootRef, ...props }) => {
          return (
            <div
              data-slot="calendar"
              ref={rootRef}
              className={cn(className)}
              {...props}
            />
          )
        },
        Chevron: ({ className, orientation, ...props }) => {
          if (orientation === "left") {
            return (
              <ChevronLeftIcon className={cn("approval:size-4", className)} {...props} />
            )
          }

          if (orientation === "right") {
            return (
              <ChevronRightIcon className={cn("approval:size-4", className)} {...props} />
            )
          }

          return (
            <ChevronDownIcon className={cn("approval:size-4", className)} {...props} />
          )
        },
        DayButton: ({ ...props }) => (
          <CalendarDayButton locale={locale} {...props} />
        ),
        WeekNumber: ({ children, ...props }) => {
          return (
            <td {...props}>
              <div className="approval:flex approval:size-(--cell-size) approval:items-center approval:justify-center approval:text-center">
                {children}
              </div>
            </td>
          )
        },
        ...components,
      }}
      {...props}
    />
  )
}

function CalendarDayButton({
  className,
  day,
  modifiers,
  locale,
  ...props
}: React.ComponentProps<typeof DayButton> & { locale?: Partial<Locale> }) {
  const defaultClassNames = getDefaultClassNames()

  const ref = React.useRef<HTMLButtonElement>(null)
  React.useEffect(() => {
    if (modifiers.focused) ref.current?.focus()
  }, [modifiers.focused])

  return (
    <Button
      ref={ref}
      variant="ghost"
      size="icon"
      data-day={day.date.toLocaleDateString(locale?.code)}
      data-selected-single={
        modifiers.selected &&
        !modifiers.range_start &&
        !modifiers.range_end &&
        !modifiers.range_middle
      }
      data-range-start={modifiers.range_start}
      data-range-end={modifiers.range_end}
      data-range-middle={modifiers.range_middle}
      className={cn(
        "approval:relative approval:isolate approval:z-10 approval:flex approval:aspect-square approval:size-auto approval:w-full approval:min-w-(--cell-size) approval:flex-col approval:gap-1 approval:border-0 approval:leading-none approval:font-normal approval:group-data-[focused=true]/day:relative approval:group-data-[focused=true]/day:z-10 approval:group-data-[focused=true]/day:border-ring approval:group-data-[focused=true]/day:ring-[3px] approval:group-data-[focused=true]/day:ring-ring/50 approval:data-[range-end=true]:rounded-(--cell-radius) approval:data-[range-end=true]:rounded-r-(--cell-radius) approval:data-[range-end=true]:bg-primary approval:data-[range-end=true]:text-primary-foreground approval:data-[range-middle=true]:rounded-none approval:data-[range-middle=true]:bg-muted approval:data-[range-middle=true]:text-foreground approval:data-[range-start=true]:rounded-(--cell-radius) approval:data-[range-start=true]:rounded-l-(--cell-radius) approval:data-[range-start=true]:bg-primary approval:data-[range-start=true]:text-primary-foreground approval:data-[selected-single=true]:bg-primary approval:data-[selected-single=true]:text-primary-foreground approval:dark:hover:text-foreground approval:[&>span]:text-xs approval:[&>span]:opacity-70",
        defaultClassNames.day,
        className
      )}
      {...props}
    />
  )
}

export { Calendar, CalendarDayButton }
