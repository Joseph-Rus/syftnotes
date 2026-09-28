import { useEffect, useState } from 'preact/hooks';

/** A value the whole app shares, and a way to be told when it changes. */
export interface Store<T> {
  get(): T;
  set(value: T | ((was: T) => T)): void;
  subscribe(listener: (value: T) => void): () => void;
}

export function createStore<T>(initial: T): Store<T> {
  let value = initial;
  const listeners = new Set<(value: T) => void>();
  return {
    get: () => value,
    set(next) {
      const v = typeof next === 'function' ? (next as (was: T) => T)(value) : next;
      if (Object.is(v, value)) return;
      value = v;
      for (const l of listeners) l(v);
    },
    subscribe(listener) {
      listeners.add(listener);
      return () => {
        listeners.delete(listener);
      };
    },
  };
}

/** A store's value in a component, re-rendering when it changes. */
export function useStore<T>(store: Store<T>): T {
  const [value, setValue] = useState(store.get);
  useEffect(() => {
    setValue(store.get());
    return store.subscribe(setValue);
  }, [store]);
  return value;
}

/** A store kept on this phone between visits (localStorage), falling back to `initial` when it can't be read. */
export function persistedStore<T>(key: string, initial: T): Store<T> {
  let start = initial;
  try {
    const raw = localStorage.getItem(key);
    if (raw !== null) start = JSON.parse(raw) as T;
  } catch {
    // private mode or bad JSON: the default
  }
  const store = createStore(start);
  store.subscribe((v) => {
    try {
      localStorage.setItem(key, JSON.stringify(v));
    } catch {
      // full or blocked: it lasts this visit only
    }
  });
  return store;
}
