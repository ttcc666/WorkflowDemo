import { createContext, useContext, useId, type AriaAttributes } from "react";

export const FieldState = createContext<{
  invalid: boolean;
  required?: boolean;
  describedBy?: string;
  labelledBy?: string;
}>({ invalid: false });

export function useFieldAria(props: AriaAttributes & { id?: string }) {
  const field = useContext(FieldState);
  const id = useId();
  return {
    id: props.id ?? id,
    "aria-invalid": props["aria-invalid"] ?? (field.invalid || undefined),
    "aria-required": props["aria-required"] ?? (field.required || undefined),
    "aria-labelledby":
      props["aria-labelledby"] ??
      (props["aria-label"] ? undefined : field.labelledBy),
    "aria-describedby":
      [field.describedBy, props["aria-describedby"]]
        .filter(Boolean)
        .join(" ") || undefined,
  };
}
