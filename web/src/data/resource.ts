import { useCallback, useEffect, useRef, useState } from 'preact/hooks';
import { ApiError } from '../api/client';
import { createStore } from './store';

/** Whether the library answered the last time the app asked: false shows the offline banner. */
export const reachable = createStore(true);

interface Entry {
  data: unknown;
  at: number;
}

/** What the app has already read this visit, by key, so a screen it comes back to shows at once. */
const memory = new Map<string, Entry>();

export function cached<T>(key: string): T | undefined {
  return memory.get(key)?.data as T | undefined;
}

export function remember<T>(key: string, data: T) {
  memory.set(key, { data, at: Date.now() });
}

export function forget(prefix = '') {
  for (const k of [...memory.keys()]) if (k.startsWith(prefix)) memory.delete(k);
}

/** Note how a request went, for the offline banner. */
export function noteOutcome(error: unknown) {
  if (error instanceof ApiError && error.unreachable) reachable.set(false);
  else if (!error || error instanceof ApiError) reachable.set(true);
}

export interface Resource<T> {
  data: T | undefined;
  error: ApiError | undefined;
  loading: boolean;
  /** Ask again; resolves when the answer is in (pull to refresh waits on it). */
  refresh: () => Promise<void>;
}

/**
 * Something read from the library: what was read before shows straight away, and it's asked for again each time
 * the screen opens. A null key reads nothing.
 */
export function useResource<T>(key: string | null, load: (signal: AbortSignal) => Promise<T>): Resource<T> {
  const [data, setData] = useState<T | undefined>(() => (key ? cached<T>(key) : undefined));
  const [error, setError] = useState<ApiError | undefined>();
  const [loading, setLoading] = useState(key !== null);
  const loader = useRef(load);
  loader.current = load;
  const inflight = useRef<AbortController | null>(null);

  const run = useCallback(async () => {
    if (!key) return;
    inflight.current?.abort();
    const controller = new AbortController();
    inflight.current = controller;
    setLoading(true);
    try {
      const value = await loader.current(controller.signal);
      if (controller.signal.aborted) return;
      remember(key, value);
      setData(value);
      setError(undefined);
      noteOutcome(undefined);
    } catch (e) {
      if (controller.signal.aborted || (e instanceof DOMException && e.name === 'AbortError')) return;
      noteOutcome(e);
      setError(e instanceof ApiError ? e : new ApiError(-1, 'Something went wrong.'));
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }, [key]);

  useEffect(() => {
    setData(key ? cached<T>(key) : undefined);
    setError(undefined);
    void run();
    return () => inflight.current?.abort();
  }, [key, run]);

  return { data, error, loading, refresh: run };
}
