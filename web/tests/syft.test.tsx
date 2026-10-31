import { act, render } from '@testing-library/preact';
import { SyftLoader, SyftSplash } from '../src/ui/Syft';

// The splash covers the whole app: what matters is that it always goes, and never before it's been seen.

describe('the splash', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  test('stays at least 1.2 s, then fades for 250 ms and says it has finished', () => {
    const done = vi.fn();
    render(<SyftSplash variant="sieveDrop" ready onFinish={done} />);
    act(() => void vi.advanceTimersByTime(1100));
    expect(document.querySelector('.syft-splash.leaving')).toBeNull();
    act(() => void vi.advanceTimersByTime(150));
    expect(document.querySelector('.syft-splash.leaving')).not.toBeNull();
    expect(done).not.toHaveBeenCalled();
    act(() => void vi.advanceTimersByTime(260));
    expect(done).toHaveBeenCalledTimes(1);
  });

  test('goes after 4 s even if the app never says it is ready', () => {
    const done = vi.fn();
    render(<SyftSplash variant="sortIn" ready={false} onFinish={done} />);
    act(() => void vi.advanceTimersByTime(3900));
    expect(document.querySelector('.syft-splash.leaving')).toBeNull();
    act(() => void vi.advanceTimersByTime(150));
    expect(document.querySelector('.syft-splash.leaving')).not.toBeNull();
    act(() => void vi.advanceTimersByTime(260));
    expect(done).toHaveBeenCalledTimes(1);
  });

  test('is the app name to a screen reader', () => {
    const { getByRole } = render(<SyftSplash variant="popBack" />);
    expect(getByRole('img').getAttribute('aria-label')).toBe('Syft Notes');
  });
});

test('a loader says Loading, and Pixel Progress lights the cells it has reached', () => {
  const { getByRole, container } = render(<SyftLoader variant="pixelProgress" progress={0.42} />);
  expect(getByRole('status').textContent).toContain('Loading');
  expect(container.querySelectorAll('.syft-pg-step.on')).toHaveLength(5);
});
