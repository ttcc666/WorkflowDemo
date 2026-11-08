import { DatePicker } from "../../components/ui/extended";
import { Search, RotateCcw } from "lucide-react";
import { instanceStatusLabels, type FlowSummary } from "@approval/shared";
import type { ListFilters } from "@approval/shared/features/lists/listModel";
import { Checkbox, Field, Select } from "../../components/ui/primitives";
import { useId, useState } from "react";
import { validateDateFilters } from "./dateFilters";
import { Button } from "../../components/shadcn/button";
import { FieldGroup } from "../../components/shadcn/field";
import {
  InputGroup,
  InputGroupInput,
  InputGroupAddon,
} from "../../components/shadcn/input-group";

export function ListFilterBar({
  filters,
  onChange,
  search,
  reset,
  flows = [],
  overdue = false,
  dates = true,
  status = true,
}: {
  filters: ListFilters;
  onChange: (filters: ListFilters) => void;
  search: () => void;
  reset: () => void;
  flows?: FlowSummary[];
  overdue?: boolean;
  dates?: boolean;
  status?: boolean;
}) {
  const keywordId = useId();
  const [checked, setChecked] = useState(false);
  const { fromError, toError } = validateDateFilters(filters.from, filters.to);
  return (
    <form
      className="ar-list-filters"
      onSubmit={(event) => {
        event.preventDefault();
        setChecked(true);
        if (fromError || toError) return;
        search();
      }}
    >
      <FieldGroup className="ar-list-filter-fields">
        <Field label="关键词" className="ar-filter-keyword">
          <InputGroup>
            <InputGroupAddon>
              <Search />
            </InputGroupAddon>
            <InputGroupInput
              id={keywordId}
              aria-label="关键词"
              placeholder="搜索标题"
              value={filters.keyword}
              onChange={(event) =>
                onChange({ ...filters, keyword: event.target.value })
              }
            />
          </InputGroup>
        </Field>
        <Field label="审批流程">
          <Select
            aria-label="流程筛选"
            placeholder="全部流程"
            value={filters.definitionKey}
            options={flows.map((flow) => ({
              value: flow.key,
              label: flow.name,
            }))}
            onValueChange={(nextValue) =>
              onChange({ ...filters, definitionKey: nextValue })
            }
          />
        </Field>
        {status && (
          <Field label="审批状态">
            <Select
              aria-label="状态筛选"
              placeholder="全部状态"
              value={filters.status}
              options={Object.entries(instanceStatusLabels).map(
                ([value, item]) => ({ value, label: item.text }),
              )}
              onValueChange={(nextValue) =>
                onChange({ ...filters, status: nextValue })
              }
            />
          </Field>
        )}
        {dates && (
          <>
            <Field
              label="开始日期"
              error={checked ? (fromError ?? undefined) : undefined}
            >
              <DatePicker
                label="开始日期"
                showInputError={!checked}
                value={filters.from}
                onChange={(nextValue) =>
                  onChange({ ...filters, from: nextValue })
                }
              />
            </Field>
            <Field
              label="结束日期"
              error={checked ? (toError ?? undefined) : undefined}
            >
              <DatePicker
                label="结束日期"
                showInputError={!checked}
                value={filters.to}
                onChange={(nextValue) =>
                  onChange({ ...filters, to: nextValue })
                }
              />
            </Field>
          </>
        )}
      </FieldGroup>
      <div className="ar-list-filter-actions">
        {overdue && (
          <Checkbox
            checked={filters.overdueOnly}
            onChange={(checked) =>
              onChange({ ...filters, overdueOnly: checked })
            }
          >
            只看超期
          </Checkbox>
        )}
        <Button type="submit">
          <Search data-icon="inline-start" />
          搜索
        </Button>
        <Button
          type="button"
          variant="outline"
          onClick={() => {
            setChecked(false);
            reset();
          }}
        >
          <RotateCcw data-icon="inline-start" />
          重置
        </Button>
      </div>
    </form>
  );
}
