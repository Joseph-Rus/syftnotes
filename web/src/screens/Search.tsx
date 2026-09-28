import { useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { useResource } from '../data/resource';
import { lectureMeta } from '../format';
import { href, navigate } from '../router';
import { classColor } from '../theme/themes';
import { Doc, Search as SearchIcon } from '../ui/icons';
import { Dot, Empty, Problem, Row, Screen, SkeletonRows } from '../ui/kit';

/** The notes and lectures a word or phrase turns up, across every class (GET /api/v2/search). A nice-to-have this
 * round: no filters yet, just a field and a plain list. */
export function SearchScreen({ api, q }: { api: Api; q: string }) {
  const [typed, setTyped] = useState(q);
  const key = q.trim() ? `search:${q.trim()}` : null;
  const results = useResource(key, (signal) => api.search(q.trim(), {}, signal));

  const go = (value: string) => navigate({ name: 'search', q: value }, { replace: true });

  return (
    <Screen
      title="Search"
      large
      accessory={
        <input
          class="search-field"
          type="search"
          value={typed}
          placeholder="Search your notes"
          enterkeyhint="search"
          onInput={(e) => setTyped(e.currentTarget.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter') go(typed);
          }}
          onBlur={() => go(typed)}
        />
      }
    >
      {!key ? (
        <Empty icon={<SearchIcon size={36} />} title="Search your library">
          Words from your notes, transcripts and classes.
        </Empty>
      ) : results.error ? (
        <Problem message="That search didn't go through." onRetry={results.refresh} />
      ) : results.loading && !results.data ? (
        <SkeletonRows count={5} />
      ) : !results.data || (results.data.lectures.length === 0 && results.data.passages.length === 0) ? (
        <Empty icon={<SearchIcon size={36} />} title="Nothing found">
          Try another word, or check the spelling.
        </Empty>
      ) : (
        <>
          {results.data.lectures.map((l) => (
            <Row
              key={l.id}
              href={href({ name: 'lecture', id: l.id })}
              lead={<Doc size={20} />}
              title={l.title || 'Untitled lecture'}
              subtitle={[l.class, lectureMeta(l.date, l.seconds)].filter(Boolean).join(' · ')}
              chevron
            />
          ))}
          {results.data.passages.map((p, i) => (
            <Row
              key={`${p.id}-${i}`}
              href={p.id ? href({ name: 'lecture', id: p.id }) : undefined}
              lead={p.class ? <Dot color={classColor(0)} /> : undefined}
              title={p.text}
              subtitle={[p.title, p.class].filter(Boolean).join(' · ')}
              lines={2}
              chevron={!!p.id}
            />
          ))}
        </>
      )}
    </Screen>
  );
}
