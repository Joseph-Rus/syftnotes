import { back, depthOf, href, navigate, parse, tabOf, type Route } from '../src/router';

describe('addresses', () => {
  const routes: Route[] = [
    { name: 'library' },
    { name: 'class', class: 'CS 101' },
    { name: 'class', class: 'Unsorted' },
    { name: 'lecture', id: '2026-09-23-recursion' },
    { name: 'search', q: 'krebs cycle' },
    { name: 'search', q: '' },
    { name: 'ask', chat: null },
    { name: 'ask', chat: 'c-42' },
    { name: 'due' },
    { name: 'coming-up' },
    { name: 'assignment', class: 'CS 101', id: '9001' },
    { name: 'memo' },
    { name: 'upload', class: 'BIO 110', lecture: 'L1' },
    { name: 'upload', class: null, lecture: null },
    { name: 'settings' },
  ];

  test.each(routes)('%o goes to an address and back unchanged', (route) => {
    const u = new URL(href(route), 'https://lib.example.ts.net');
    expect(u.pathname.startsWith('/app/')).toBe(true);
    expect(parse(u.pathname, u.search)).toEqual(route);
  });

  test('a class name with a slash or a hash stays whole', () => {
    const u = new URL(href({ name: 'class', class: 'A/B #1' }), 'https://x');
    expect(parse(u.pathname, u.search)).toEqual({ name: 'class', class: 'A/B #1' });
  });

  test('/app and /app/index.html are the library', () => {
    expect(parse('/app')).toEqual({ name: 'library' });
    expect(parse('/app/index.html')).toEqual({ name: 'library' });
    expect(parse('/app/class/')).toEqual({ name: 'library' });
  });

  test('an address the app does not know is not found', () => {
    expect(parse('/app/nowhere')).toEqual({ name: 'not-found' });
    expect(parse('/app/lecture/a/b')).toEqual({ name: 'not-found' });
  });
});

test('screens belong to their tabs', () => {
  expect(tabOf({ name: 'lecture', id: 'x' })).toBe('library');
  expect(tabOf({ name: 'coming-up' })).toBe('due');
  expect(tabOf({ name: 'due' })).toBe('due');
  expect(tabOf({ name: 'assignment', class: 'CS 101', id: '1' })).toBe('due');
  expect(tabOf({ name: 'upload', class: null, lecture: null })).toBe('library');
  expect(tabOf({ name: 'memo' })).toBe('library');
  expect(tabOf({ name: 'ask', chat: 'c' })).toBe('ask');
});

test('a lecture sits deeper than its class, and its class deeper than the library', () => {
  expect(depthOf({ name: 'library' })).toBeLessThan(depthOf({ name: 'class', class: 'x' }));
  expect(depthOf({ name: 'class', class: 'x' })).toBeLessThan(depthOf({ name: 'lecture', id: 'x' }));
});

describe('moving about', () => {
  test('going to a screen changes the address, and back returns to the one before', async () => {
    navigate({ name: 'library' }, { replace: true });
    navigate({ name: 'class', class: 'CS 101' });
    expect(location.pathname).toBe('/app/class/CS%20101');
    const popped = new Promise((r) => window.addEventListener('popstate', r, { once: true }));
    back({ name: 'library' });
    await popped;
    expect(location.pathname).toBe('/app/');
  });

  test('back when the app opened on this screen goes up to the given one, in place', async () => {
    vi.resetModules();
    history.replaceState(null, '', '/app/lecture/x');
    const fresh = await import('../src/router');
    const before = history.length;
    fresh.back({ name: 'class', class: 'CS 101' });
    expect(location.pathname).toBe('/app/class/CS%20101');
    expect(history.length).toBe(before);
  });
});
