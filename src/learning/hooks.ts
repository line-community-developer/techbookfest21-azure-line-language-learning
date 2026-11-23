import { useCallback, useEffect, useRef, useState, type DependencyList, type Dispatch, type SetStateAction } from 'react';
import { toError } from './api';

export interface AsyncState<T> {
  data: T | undefined;
  error: Error | undefined;
  loading: boolean;
  reload: () => void;
  setData: Dispatch<SetStateAction<T | undefined>>;
}

// Loads data when deps change; `reload` re-fetches while keeping the current data visible.
export function useApi<T>(fetcher: (signal: AbortSignal) => Promise<T>, deps: DependencyList): AsyncState<T> {
  const [data, setData] = useState<T | undefined>(undefined);
  const [error, setError] = useState<Error | undefined>(undefined);
  const [loading, setLoading] = useState(true);
  const [nonce, setNonce] = useState(0);
  const fetcherRef = useRef(fetcher);
  fetcherRef.current = fetcher;
  const depsKey = JSON.stringify(deps);
  const lastKey = useRef(depsKey);

  useEffect(() => {
    const controller = new AbortController();
    if (lastKey.current !== depsKey) {
      lastKey.current = depsKey;
      setData(undefined);
    }
    setLoading(true);
    setError(undefined);
    fetcherRef.current(controller.signal).then(
      (result) => {
        if (controller.signal.aborted) return;
        setData(result);
        setLoading(false);
      },
      (reason: unknown) => {
        if (controller.signal.aborted) return;
        setError(toError(reason));
        setLoading(false);
      },
    );
    return () => controller.abort();
  }, [depsKey, nonce]);

  const reload = useCallback(() => setNonce((n) => n + 1), []);
  return { data, error, loading, reload, setData };
}

// Runs a user-triggered async action and tracks its pending/error state.
export function useAction<A extends unknown[], R>(action: (...args: A) => Promise<R>) {
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<Error | undefined>(undefined);
  const actionRef = useRef(action);
  actionRef.current = action;

  const run = useCallback(async (...args: A): Promise<R | undefined> => {
    setPending(true);
    setError(undefined);
    try {
      return await actionRef.current(...args);
    } catch (reason) {
      setError(toError(reason));
      return undefined;
    } finally {
      setPending(false);
    }
  }, []);

  const clearError = useCallback(() => setError(undefined), []);
  return { run, pending, error, clearError };
}
