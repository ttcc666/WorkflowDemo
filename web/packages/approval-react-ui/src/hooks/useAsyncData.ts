import { useCallback, useEffect, useRef, useState } from "react";
import { useApprovalClient, useApprovalIdentity } from "../client";

export function useAsyncData<T>(
  loader: (signal: AbortSignal) => Promise<T>,
  dependencies: readonly unknown[] = [],
  enabled = true,
) {
  const client = useApprovalClient();
  const identity = useApprovalIdentity();
  const loaderRef = useRef(loader);
  loaderRef.current = loader;
  const revision = useRef(0);
  const controller = useRef<AbortController | null>(null);
  const [tick, setTick] = useState(0);
  const [state, setState] = useState<{
    data?: T;
    error: unknown;
    loading: boolean;
    owner: string;
    key: string;
  }>({ error: null, loading: enabled, owner: identity, key: "" });
  const key = JSON.stringify(dependencies);
  useEffect(() => {
    const current = ++revision.current;
    const owner = client.generation;
    const abort = (controller.current = new AbortController());
    setState((previous) =>
      enabled && previous.owner === identity && previous.key === key
        ? { ...previous, error: null, loading: true }
        : {
            data: undefined,
            error: null,
            loading: enabled,
            owner: identity,
            key,
          },
    );
    if (enabled)
      void Promise.resolve()
        .then(() => loaderRef.current(abort.signal))
        .then(
          (data) => {
            if (
              !abort.signal.aborted &&
              revision.current === current &&
              client.generation === owner
            )
              setState({
                data,
                error: null,
                loading: false,
                owner: identity,
                key,
              });
          },
          (error) => {
            if (
              !abort.signal.aborted &&
              revision.current === current &&
              client.generation === owner &&
              (error as Error)?.name !== "AbortError"
            )
              setState({ error, loading: false, owner: identity, key });
          },
        );
    return () => {
      abort.abort();
    };
  }, [client, identity, key, enabled, tick]);
  const current = enabled && state.owner === identity && state.key === key;
  return {
    data: current ? state.data : undefined,
    error: current ? state.error : null,
    loading: current ? state.loading : enabled,
    reload: useCallback(() => {
      controller.current?.abort();
      setTick((value) => value + 1);
    }, []),
  };
}
