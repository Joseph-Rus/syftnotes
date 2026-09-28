import { Api, ApiError, type Uploader } from '../src/api/client';

type Call = { url: string; init: RequestInit | undefined };

function fakeFetch(respond: (url: string, init?: RequestInit) => Response | Promise<Response>) {
  const calls: Call[] = [];
  const f = (async (input: RequestInfo | URL, init?: RequestInit) => {
    calls.push({ url: String(input), init });
    return respond(String(input), init);
  }) as typeof fetch;
  return { f, calls };
}

const ok = (body: unknown, headers: Record<string, string> = { 'Content-Type': 'application/json' }) =>
  new Response(JSON.stringify(body), { status: 200, headers });

function ndjson(lines: unknown[]): Response {
  return new Response(lines.map((l) => JSON.stringify(l)).join('\n') + '\n', {
    status: 200,
    headers: { 'Content-Type': 'application/x-ndjson; charset=utf-8' },
  });
}

describe('addresses', () => {
  test('paths sit under /api/v2, with empty query values left out', () => {
    const api = new Api();
    expect(api.url('/lectures', { class: 'CS 101', limit: 20, before: undefined })).toBe(
      '/api/v2/lectures?class=CS+101&limit=20',
    );
    expect(api.url('/library')).toBe('/api/v2/library');
  });

  test('another library address can be given, without a trailing slash doubling up', () => {
    expect(new Api({ base: 'https://lib.example.ts.net/' }).url('/me')).toBe('https://lib.example.ts.net/api/v2/me');
  });

  test('ids are escaped in paths', async () => {
    const { f, calls } = fakeFetch(() => ok({ id: 'a/b' }));
    await new Api({ fetch: f }).rendered('a/b');
    expect(calls[0]!.url).toBe('/api/v2/lectures/a%2Fb/rendered');
  });
});

describe('reading', () => {
  test('the library overview comes back typed, with the cookie sent along', async () => {
    const { f, calls } = fakeFetch(() => ok({ name: 'My library', version: '0.9.0', classes: [], unsorted: 2 }));
    const lib = await new Api({ fetch: f }).library();
    expect(lib.name).toBe('My library');
    expect(lib.unsorted).toBe(2);
    expect(calls[0]!.init?.credentials).toBe('same-origin');
  });

  test('lectures in a class ask with the class and a page size', async () => {
    const { f, calls } = fakeFetch(() => ok([]));
    await new Api({ fetch: f }).lectures({ class: 'Unsorted', limit: 100 });
    expect(calls[0]!.url).toBe('/api/v2/lectures?class=Unsorted&limit=100');
  });

  test('search sends the words', async () => {
    const { f, calls } = fakeFetch(() => ok({ query: 'krebs', lectures: [], passages: [], classes: [] }));
    const r = await new Api({ fetch: f }).search('krebs cycle');
    expect(calls[0]!.url).toBe('/api/v2/search?q=krebs+cycle');
    expect(r.query).toBe('krebs');
  });

  test('attachments unwrap their list', async () => {
    const { f, calls } = fakeFetch(() => ok({ attachments: [{ id: '1', name: 'a.pdf' }] }));
    const list = await new Api({ fetch: f }).attachments({ lecture: 'L1' });
    expect(list).toHaveLength(1);
    expect(calls[0]!.url).toBe('/api/v2/attachments?lecture=L1');
  });

  test('the due list and the week ahead come from their own addresses', async () => {
    const { f, calls } = fakeFetch((url) =>
      url.includes('calendar') ? ok({ events: [], updated: null }) : ok({ groups: [], to_hand_in: 0 }),
    );
    const api = new Api({ fetch: f });
    await api.due();
    await api.upcoming();
    expect(calls.map((c) => c.url)).toEqual(['/api/v2/canvas/due', '/api/v2/calendar/upcoming']);
  });
});

describe('when things go wrong', () => {
  test("the library's own words come through", async () => {
    const { f } = fakeFetch(() => new Response(JSON.stringify({ detail: 'no such lecture' }), { status: 404 }));
    const e = await new Api({ fetch: f }).lecture('x').catch((x: unknown) => x);
    expect(e).toBeInstanceOf(ApiError);
    expect((e as ApiError).status).toBe(404);
    expect((e as ApiError).detail).toBe('no such lecture');
  });

  test('pairing says why in "error"', async () => {
    const { f } = fakeFetch(() => new Response(JSON.stringify({ error: "That code didn't work." }), { status: 401 }));
    const e = (await new Api({ fetch: f }).pair('123456', 'iPhone').catch((x: unknown) => x)) as ApiError;
    expect(e.unauthorized).toBe(true);
    expect(e.detail).toBe("That code didn't work.");
  });

  test('a phone that is no longer paired is told so, once per refusal', async () => {
    const seen = vi.fn();
    const { f } = fakeFetch(() => new Response('', { status: 401 }));
    const api = new Api({ fetch: f, onUnauthorized: seen });
    await api.library().catch(() => undefined);
    expect(seen).toHaveBeenCalledTimes(1);
  });

  test('no network is status 0: unreachable', async () => {
    const f = (async () => {
      throw new TypeError('Load failed');
    }) as typeof fetch;
    const e = (await new Api({ fetch: f }).me().catch((x: unknown) => x)) as ApiError;
    expect(e.unreachable).toBe(true);
    expect(e.message).toMatch(/reach/);
  });

  test('stopping a request is not mistaken for being offline', async () => {
    const f = (async () => {
      throw new DOMException('stopped', 'AbortError');
    }) as typeof fetch;
    const e = await new Api({ fetch: f }).me().catch((x: unknown) => x);
    expect(e).toBeInstanceOf(DOMException);
  });

  test('a server error without words gets plain ones', async () => {
    const { f } = fakeFetch(() => new Response('oops', { status: 500 }));
    const e = (await new Api({ fetch: f }).library().catch((x: unknown) => x)) as ApiError;
    expect(e.detail).toMatch(/problem/);
  });
});

describe('pairing', () => {
  test('the code and the phone name go up; the device comes back', async () => {
    const { f, calls } = fakeFetch(() => ok({ device: { id: 'd1', name: 'iPhone', added: '2026-09-28' } }));
    const d = await new Api({ fetch: f }).pair('123456', 'iPhone');
    expect(d.id).toBe('d1');
    expect(calls[0]!.url).toBe('/api/v2/devices/pair');
    expect(calls[0]!.init?.method).toBe('POST');
    expect(JSON.parse(String(calls[0]!.init?.body))).toEqual({ code: '123456', name: 'iPhone' });
  });

  test('removing a device deletes it by id', async () => {
    const { f, calls } = fakeFetch(() => ok({}));
    await new Api({ fetch: f }).removeDevice('d1');
    expect(calls[0]!.init?.method).toBe('DELETE');
    expect(calls[0]!.url).toBe('/api/v2/devices/d1');
  });
});

describe('asking', () => {
  test('an answer streams in, then arrives whole', async () => {
    const reply = { answer: 'Hello there', sources: [], engine: 'ollama', engine_name: 'Ollama' };
    const { f, calls } = fakeFetch(() =>
      ndjson([
        { kind: 'text', text: 'Hello' },
        { kind: 'text', text: ' there' },
        { kind: 'done', reply },
      ]),
    );
    const events = [];
    for await (const e of new Api({ fetch: f }).ask('hi?', { class: 'CS 101' })) events.push(e);
    expect(events.map((e) => e.kind)).toEqual(['text', 'text', 'done']);
    expect(JSON.parse(String(calls[0]!.init?.body))).toMatchObject({ question: 'hi?', class: 'CS 101', stream: true });
  });

  test('a library too old to stream still gives its answer', async () => {
    const { f } = fakeFetch(() => ok({ answer: 'All at once', sources: [] }));
    const events = [];
    for await (const e of new Api({ fetch: f }).ask('q')) events.push(e);
    expect(events).toEqual([{ kind: 'done', reply: { answer: 'All at once', sources: [] } }]);
  });

  test('a chat turn gives its id first, then what the AI writes', async () => {
    const { f, calls } = fakeFetch(() =>
      ndjson([{ chat: 'c1' }, { kind: 'text', text: 'Hi', name: '', path: '' }, { kind: 'done', text: 'Hi' }]),
    );
    const events = [];
    for await (const e of new Api({ fetch: f }).sendChat('hello', { chat: 'c1' })) events.push(e);
    expect(events[0]).toEqual({ chat: 'c1' });
    expect(events).toHaveLength(3);
    expect(JSON.parse(String(calls[0]!.init?.body))).toMatchObject({ message: 'hello', chat: 'c1', edit: false });
  });
});

describe('uploading', () => {
  test('files, class and lecture go up as one form, with progress', async () => {
    let sent: FormData | undefined;
    const upload: Uploader = async (url, body, onProgress) => {
      expect(url).toBe('/api/v2/attachments');
      sent = body;
      onProgress({ loaded: 50, total: 100 });
      onProgress({ loaded: 100, total: 100 });
      return { status: 200, text: JSON.stringify({ attachments: [{ id: 'a1', name: 'board.jpg' }] }) };
    };
    const progress: number[] = [];
    const got = await new Api({ upload }).upload(
      [new File(['x'], 'board.jpg', { type: 'image/jpeg' }), new File(['y'], 'sheet.pdf')],
      { class: 'BIO 110', lecture: 'L9' },
      (p) => progress.push(p.loaded / p.total),
    );
    expect(got[0]!.id).toBe('a1');
    expect(progress).toEqual([0.5, 1]);
    expect(sent!.getAll('file')).toHaveLength(2);
    expect(sent!.get('class')).toBe('BIO 110');
    expect(sent!.get('lecture')).toBe('L9');
  });

  test('a refused upload says why', async () => {
    const upload: Uploader = async () => ({ status: 413, text: '' });
    const e = (await new Api({ upload }).upload([new File(['x'], 'big.mov')], {}).catch((x: unknown) => x)) as ApiError;
    expect(e.status).toBe(413);
    expect(e.detail).toMatch(/200 MB/);
  });

  test('an upload that never reached the library is unreachable', async () => {
    const upload: Uploader = async () => {
      throw new TypeError('network');
    };
    const e = (await new Api({ upload }).upload([new File(['x'], 'a.txt')], {}).catch((x: unknown) => x)) as ApiError;
    expect(e.unreachable).toBe(true);
  });
});
