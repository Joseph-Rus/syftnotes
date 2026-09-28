import { render, screen, waitFor, act } from '@testing-library/preact';
import { ApiError } from '../src/api/client';
import { forget, reachable, remember, useResource } from '../src/data/resource';
import { createStore, persistedStore } from '../src/data/store';

function Show({ k, load }: { k: string | null; load: (s: AbortSignal) => Promise<string> }) {
  const r = useResource(k, load);
  return (
    <p>
      {r.loading ? 'loading ' : ''}
      {r.data ?? 'nothing'}
      {r.error ? ` error:${r.error.status}` : ''}
    </p>
  );
}

beforeEach(() => {
  forget();
  reachable.set(true);
});

test('what was read shows, and the library is marked reachable', async () => {
  reachable.set(false);
  render(<Show k="a" load={async () => 'hello'} />);
  await screen.findByText('hello');
  expect(reachable.get()).toBe(true);
});

test('what was read before shows at once while it is asked for again', async () => {
  remember('b', 'old');
  let resolve!: (v: string) => void;
  render(<Show k="b" load={() => new Promise((r) => (resolve = r))} />);
  expect(screen.getByText('loading old')).toBeTruthy();
  await act(async () => resolve('new'));
  await screen.findByText('new');
});

test('an unreachable library marks the app offline and keeps what it had', async () => {
  remember('c', 'kept');
  render(
    <Show
      k="c"
      load={async () => {
        throw new ApiError(0, "Can't reach your library.");
      }}
    />,
  );
  await screen.findByText('kept error:0');
  expect(reachable.get()).toBe(false);
});

test('a null key reads nothing', async () => {
  const load = vi.fn(async () => 'x');
  render(<Show k={null} load={load} />);
  await waitFor(() => expect(screen.getByText('nothing')).toBeTruthy());
  expect(load).not.toHaveBeenCalled();
});

test('a store tells its listeners, and only on a change', () => {
  const s = createStore(1);
  const seen: number[] = [];
  s.subscribe((v) => seen.push(v));
  s.set(2);
  s.set(2);
  s.set((v) => v + 1);
  expect(seen).toEqual([2, 3]);
});

test('a kept store is there next visit', () => {
  const a = persistedStore('test.pref', 'light');
  a.set('dark');
  expect(persistedStore('test.pref', 'light').get()).toBe('dark');
});

test('a kept store with nothing saved starts at its default', () => {
  localStorage.setItem('test.bad', '{nope');
  expect(persistedStore('test.bad', 7).get()).toBe(7);
});
