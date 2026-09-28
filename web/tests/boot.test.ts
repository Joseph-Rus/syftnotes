import { Api } from '../src/api/client';
import { cleanCode, decide, type BootFacts } from '../src/boot';
import { deviceName, isTablet, platformOf } from '../src/platform';

const IPHONE = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Version/18.0 Mobile/15E148 Safari/604.1';
const IPAD = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15';
const PIXEL = 'Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 Chrome/130 Mobile Safari/537.36';
const TAB = 'Mozilla/5.0 (Linux; Android 15; SM-X710) AppleWebKit/537.36 Chrome/130 Safari/537.36';

describe('which phone', () => {
  test('iPhone, iPad (which says Mac but has touch), Android and a desktop', () => {
    expect(platformOf(IPHONE)).toBe('ios');
    expect(platformOf(IPAD, 5)).toBe('ios');
    expect(platformOf(IPAD, 0)).toBe('other');
    expect(platformOf(PIXEL)).toBe('android');
  });

  test('tablets are told apart from phones', () => {
    expect(isTablet(IPAD, 5)).toBe(true);
    expect(isTablet(IPHONE)).toBe(false);
    expect(isTablet(TAB)).toBe(true);
    expect(isTablet(PIXEL)).toBe(false);
  });

  test('a device gets a plain name to start with', () => {
    expect(deviceName(IPHONE)).toBe('iPhone');
    expect(deviceName(IPAD, 5)).toBe('iPad');
    expect(deviceName(PIXEL)).toBe('Android phone');
    expect(deviceName(TAB)).toBe('Android tablet');
  });
});

function api(respond: (url: string) => Response | 'offline') {
  return new Api({
    fetch: (async (input: RequestInfo | URL) => {
      const r = respond(String(input));
      if (r === 'offline') throw new TypeError('Load failed');
      return r;
    }) as typeof fetch,
  });
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status });
const installed: BootFacts = { standalone: true, platform: 'ios', skippedInstall: false, pairedBefore: false };
const library = { name: 'Home Mac', version: '0.9.0' };

describe('the first screen', () => {
  test("in iPhone Safari, it's how to add it to the Home Screen", async () => {
    const gate = await decide(api(() => json({})), { ...installed, standalone: false });
    expect(gate).toEqual({ kind: 'install', platform: 'ios' });
  });

  test('on a computer, or after choosing the browser, it goes straight on', async () => {
    const a = api(() => json({ paired: true, library }));
    expect((await decide(a, { ...installed, standalone: false, platform: 'other' })).kind).toBe('ready');
    expect((await decide(a, { ...installed, standalone: false, skippedInstall: true })).kind).toBe('ready');
  });

  test('a paired phone opens the app', async () => {
    expect(await decide(api(() => json({ paired: true, library })), installed)).toEqual({
      kind: 'ready',
      library,
      offline: false,
    });
  });

  test('a new phone is asked for its code', async () => {
    expect(await decide(api(() => json({ paired: false, library })), installed)).toEqual({ kind: 'pair', library });
  });

  test("can't reach the library the first time: explain Tailscale", async () => {
    expect(await decide(api(() => 'offline'), installed)).toEqual({ kind: 'unreachable' });
  });

  test("can't reach it after pairing before: open with what's saved, offline", async () => {
    expect(await decide(api(() => 'offline'), { ...installed, pairedBefore: true })).toEqual({
      kind: 'ready',
      library: null,
      offline: true,
    });
  });

  test('a library from before the phone app says it needs updating', async () => {
    const a = api(() => json({ detail: 'nope' }, 404));
    expect(await decide(a, installed)).toEqual({ kind: 'outdated' });
  });

  test('an older library that lets us read anyway (a password in development) opens', async () => {
    const a = api((url) => (url.endsWith('/me') ? json({}, 404) : json({ name: 'Dev', version: '0.8.0', classes: [] })));
    expect(await decide(a, installed)).toEqual({ kind: 'ready', library: { name: 'Dev', version: '0.8.0' }, offline: false });
  });
});

test('a code is its six digits, however it was typed or pasted', () => {
  expect(cleanCode('123 456')).toBe('123456');
  expect(cleanCode('Code: 123-456 (10 minutes)')).toBe('123456');
  expect(cleanCode('12a')).toBe('12');
  expect(cleanCode('12345678')).toBe('123456');
});
