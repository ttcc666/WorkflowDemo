import type { ReactNode } from "react";
import { Drawer } from "./extended";

export function ResponsiveInspector({
  mobile,
  open,
  onOpenChange,
  title,
  className,
  children,
}: {
  mobile: boolean;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  className: string;
  children: ReactNode;
}) {
  return mobile ? (
    <Drawer open={open} onOpenChange={onOpenChange} title={title}>
      {children}
    </Drawer>
  ) : (
    <aside className={className}>{children}</aside>
  );
}
