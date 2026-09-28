import { readLines } from '../src/api/ndjson';

function streamOf(...chunks: string[]): ReadableStream<Uint8Array> {
  const bytes = chunks.map((c) => new TextEncoder().encode(c));
  return new ReadableStream({
    start(controller) {
      for (const b of bytes) controller.enqueue(b);
      controller.close();
    },
  });
}

async function all<T>(gen: AsyncGenerator<T>): Promise<T[]> {
  const out: T[] = [];
  for await (const x of gen) out.push(x);
  return out;
}

test('each line comes out as it is, however the chunks split it', async () => {
  const lines = await all(readLines(streamOf('{"kind":"te', 'xt","text":"Hel"}\n{"kind":"text",', '"text":"lo"}\n')));
  expect(lines).toEqual([
    { kind: 'text', text: 'Hel' },
    { kind: 'text', text: 'lo' },
  ]);
});

test('a last line without a newline still counts', async () => {
  expect(await all(readLines(streamOf('{"a":1}\n{"b":2}')))).toEqual([{ a: 1 }, { b: 2 }]);
});

test('blank lines and lines that are not JSON are skipped', async () => {
  expect(await all(readLines(streamOf('\n{"a":1}\nnot json\n\n{"b":2}\n')))).toEqual([{ a: 1 }, { b: 2 }]);
});

test('a character split across two chunks arrives whole', async () => {
  const bytes = new TextEncoder().encode('{"t":"é"}\n');
  const stream = new ReadableStream<Uint8Array>({
    start(c) {
      c.enqueue(bytes.slice(0, 8));
      c.enqueue(bytes.slice(8));
      c.close();
    },
  });
  expect(await all(readLines(stream))).toEqual([{ t: 'é' }]);
});
