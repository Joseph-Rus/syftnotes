import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { useResource } from '../data/resource';
import { clock, lectureMeta } from '../format';
import { href, navigate } from '../router';
import { Doc, Search as SearchIcon } from '../ui/icons';
import { Dot, Empty, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { useClassColors } from './classColors';

/** The notes and lectures a word or phrase turns up, across every class (GET /api/v2/search): the lectures it's
 * about first, then the passages it's in, each opening its lecture. It looks as the student types. */
export function SearchScreen({ api, q }: { api: Api; q: string }) {
  const [typed, setTyped] = useState(q);
  const field = useRef<HTMLInputElement>(null);
  const colorOf = useClassColors(api);
  const key = q.trim() ? `search:${q.trim()}` : null;
  const results = useResource(key, (signal) => api.search(q.trim(), { limit: 30 }, signal));

  useEffect(() => setTyped(q), [q]);
  // Look once the typing pauses, not on every letter.
  useEffect(() => {
    if (typed === q) return;
    const t = setTimeout(() => navigate({ name: 'search', q: typed }, { replace: true }), 300);
    return () => clearTimeout(t);
  }, [typed]);
  useEffect(() => {
    if (!q) field.current?.focus();
  }, []);

  const data = results.data;
  return (
    <Screen title="Search" large>
      <div class="search-bar">
        <SearchIcon size={18} />
        <input
          ref={field}
          class="search-field"
          type="search"
          value={typed}
          placeholder="Notes, transcripts, classes"
          enterkeyhint="search"
          autocomplete="off"
          onInput={(e) => setTyped(e.currentTarget.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              navigate({ name: 'search', q: typed }, { replace: true });
              e.currentTarget.blur();
            }
          }}
        />
      </div>
      {!key ? (
        <Empty icon={<SearchIcon size={36} />} title="Search your library">
          Any word from your notes and transcripts, or a class's name.
        </Empty>
      ) : results.error && !data ? (
        <Problem message="That search didn't go through." onRetry={results.refresh} />
      ) : !data ? (
        <SkeletonRows count={5} />
      ) : data.lectures.length === 0 && data.passages.length === 0 && data.classes.length === 0 ? (
        <Empty icon={<SearchIcon size={36} />} title="Nothing found">
          Nothing in your library says “{q.trim()}”. Try another word.
        </Empty>
      ) : (
        <>
          {data.classes.length > 0 ? (
            <Section title="Classes">
              {data.classes.map((c) => (
                <Row key={c.name} href={href({ name: 'class', class: c.name })} lead={<Dot color={colorOf(c.name)} size={12} />} title={c.name} subtitle={c.code || undefined} chevron />
              ))}
            </Section>
          ) : null}
          {data.lectures.length > 0 ? (
            <Section title="Lectures">
              {data.lectures.map((l) => (
                <Row
                  key={l.id}
                  href={href({ name: 'lecture', id: l.id })}
                  lead={<Doc size={20} />}
                  title={l.title || 'Untitled lecture'}
                  subtitle={[l.class, lectureMeta(l.date, l.seconds)].filter(Boolean).join(' · ')}
                  chevron
                />
              ))}
            </Section>
          ) : null}
          {data.passages.length > 0 ? (
            <Section title="Passages">
              {data.passages.map((p, i) => (
                <Row
                  key={`${p.id}-${i}`}
                  href={p.id ? href({ name: 'lecture', id: p.id }) : undefined}
                  lead={<Dot color={colorOf(p.class)} />}
                  title={p.text}
                  subtitle={[p.title, p.kind === 'transcript' && p.at != null ? `said at ${clock(p.at)}` : p.section].filter(Boolean).join(' · ')}
                  lines={2}
                  class="passage"
                  chevron={!!p.id}
                />
              ))}
            </Section>
          ) : null}
        </>
      )}
    </Screen>
  );
}
