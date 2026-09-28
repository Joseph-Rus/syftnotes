/**
 * Newline-delimited JSON as it arrives: each whole line parsed the moment it's in, however the bytes were split up
 * on the way. A line that isn't JSON is skipped rather than ending the answer.
 */
export async function* readLines<T = unknown>(body: ReadableStream<Uint8Array>): AsyncGenerator<T> {
  const reader = body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  try {
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split('\n');
      buffer = lines.pop() ?? '';
      for (const line of lines) {
        const parsed = parseLine<T>(line);
        if (parsed !== undefined) yield parsed;
      }
    }
    buffer += decoder.decode();
    const last = parseLine<T>(buffer);
    if (last !== undefined) yield last;
  } finally {
    reader.releaseLock();
  }
}

function parseLine<T>(line: string): T | undefined {
  const text = line.trim();
  if (!text) return undefined;
  try {
    return JSON.parse(text) as T;
  } catch {
    return undefined;
  }
}
