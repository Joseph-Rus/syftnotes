import { describe, expect, it } from 'vitest';
import { parseDeck, studyPrompt } from '../src/screens/Study';
import { href, parse } from '../src/router';

describe('study', () => {
  it('reads a deck out of an answer wrapped in prose, dropping broken items', () => {
    const d = parseDeck(
      'Sure!\n```json\n{"cards":[{"front":"LR(1)?","back":"one token"},{"front":"","back":"x"}],' +
        '"quiz":[{"question":"Shift does?","choices":["push","pop"],"answer":0,"why":"It pushes."},{"question":"bad","choices":["a"],"answer":3}]}\n```',
    );
    expect(d?.cards).toHaveLength(1);
    expect(d?.quiz).toHaveLength(1);
    expect(parseDeck('no json')).toBeNull();
  });

  it('asks for JSON about the lecture', () => {
    expect(studyPrompt('LR parsing')).toContain('"LR parsing"');
    expect(studyPrompt('x')).toContain('ONLY this JSON');
  });

  it('has its own address', () => {
    expect(href({ name: 'study', id: 'a b' })).toBe('/app/study/a%20b');
    expect(parse('/app/study/a%20b')).toEqual({ name: 'study', id: 'a b' });
  });
});
