import { useCallback, useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import {
  emptyFilters,
  readListState,
  toListQuery,
  type ListFilters,
} from "@approval/shared/features/lists/listModel";
import type { ListQuery, PagedResult } from "@approval/shared";
import {
  useApprovalClient,
  useApprovalIdentity,
  type ApprovalClient,
} from "../client";
import { useAsyncData } from "./useAsyncData";

// Remember the last list owner across Provider's identity-keyed remounts.
const listOwners = new WeakMap<ApprovalClient, Map<string, string>>();

export function usePagedList<T>(
  loader: (query: ListQuery, signal: AbortSignal) => Promise<PagedResult<T>>,
  prefix = "",
  enabled = true,
) {
  useApprovalIdentity();
  const client = useApprovalClient();
  const owner = JSON.stringify([
    client.config().apiBase,
    client.identity.value,
  ]);
  const [params, setParams] = useSearchParams();
  const key = JSON.stringify(readListState(Object.fromEntries(params), prefix, 20));
  const applied = useMemo(() => JSON.parse(key) as ReturnType<typeof readListState>, [key]);
  const [filters, setFilters] = useState(applied.filters);
  useEffect(() => {
    setFilters(applied.filters);
  }, [applied.filters]);
  const patch = useCallback((next: ListFilters, page: number, size: number) => {
    setFilters(next);
    setParams(
      (previous) => {
        const result = new URLSearchParams(previous);
        for (const name of Object.keys(
          emptyFilters(),
        ) as (keyof ListFilters)[]) {
          const value = next[name];
          if (value === "" || value === undefined || value === false)
            result.delete(prefix + name);
          else
            result.set(
              prefix + name,
              typeof value === "boolean" ? "1" : String(value),
            );
        }
        if (page > 1) result.set(prefix + "page", String(page));
        else result.delete(prefix + "page");
        if (size !== 20) result.set(prefix + "pageSize", String(size));
        else result.delete(prefix + "pageSize");
        return result;
      },
      { replace: true },
    );
  }, [prefix, setParams]);
  useEffect(() => {
    let owners = listOwners.get(client);
    if (!owners) listOwners.set(client, (owners = new Map()));
    const previous = owners.get(prefix);
    owners.set(prefix, owner);
    if (previous !== undefined && previous !== owner && applied.page !== 1)
      patch(applied.filters, 1, applied.pageSize);
  }, [client, prefix, owner, applied.page, applied.pageSize, applied.filters, patch]);
  const data = useAsyncData(
    (signal) =>
      loader(
        toListQuery(applied.filters, applied.page, applied.pageSize),
        signal,
      ),
    [key],
    enabled,
  );
  return {
    ...data,
    items: data.data?.items ?? [],
    total: data.data?.total,
    filters,
    setFilters,
    page: applied.page,
    pageSize: applied.pageSize,
    search: (next = filters) => {
      if (
        applied.page === 1 &&
        JSON.stringify(next) === JSON.stringify(applied.filters)
      )
        data.reload();
      else patch(next, 1, applied.pageSize);
    },
    reset: () => patch(emptyFilters(), 1, applied.pageSize),
    paginate: (page: number, size: number) =>
      patch(applied.filters, page, size),
  };
}
