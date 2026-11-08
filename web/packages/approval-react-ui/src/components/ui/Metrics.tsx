import type { ReactNode } from "react";
import { cn } from "cn";
import { Card, CardHeader, CardDescription, CardContent } from "../shadcn/card";
export function Metrics({
  items,
  className,
}: {
  items: { label: string; value: ReactNode }[];
  className?: string;
}) {
  return (
    <div className={cn("ar-metrics-grid", className)} role="list">
      {items.map((item) => (
        <Card key={item.label} size="sm" role="listitem">
          <CardHeader>
            <CardDescription>{item.label}</CardDescription>
          </CardHeader>
          <CardContent>
            <strong className="approval:text-2xl approval:tabular-nums">
              {item.value}
            </strong>
          </CardContent>
        </Card>
      ))}
    </div>
  );
}
