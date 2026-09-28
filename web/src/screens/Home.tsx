import type { Api } from '../api/client';
import type { ClassInfo, LectureSummary, Upcoming } from '../api/types';
import { lectureMeta, timeRange, dayLabel, parseWhen } from '../format';
import { useResource } from '../data/resource';
import { href } from '../router';
import { classColor } from '../theme/themes';
import { Doc, Folder } from '../ui/icons';
import { Dot, Empty, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';

/** The library's front screen: what's coming up today and tomorrow (when the library has any), the newest
 * lectures, and every class with its dot. */
export function Home({ api, offline }: { api: Api; offline: boolean }) {
  const library = useResource('library', (signal) => api.library(signal));
  const recent = useResource('recent-lectures', (signal) => api.lectures({ limit: 8 }, signal));
  const upcoming = useResource('upcoming', (signal) => api.upcoming(signal));

  const refresh = async () => {
    await Promise.all([library.refresh(), recent.refresh(), upcoming.refresh()]);
  };

  return (
    <Screen title="Library" large subtitle={offline ? 'Showing what was saved before — offline' : undefined} onRefresh={refresh}>
      <ComingUp data={upcoming.data} />
      <RecentLectures lectures={recent.data} loading={recent.loading} error={recent.error} onRetry={recent.refresh} />
      <Classes overview={library.data} loading={library.loading} error={library.error} onRetry={library.refresh} />
    </Screen>
  );
}

function ComingUp({ data }: { data: Upcoming | undefined }) {
  const events = (data?.events ?? []).slice(0, 4);
  if (events.length === 0) return null;
  return (
    <Section title="Coming up">
      {events.map((e) => (
        <Row
          key={e.id}
          title={e.title}
          subtitle={dayLabel(parseWhen(e.start) ?? new Date()) + (e.location ? ` · ${e.location}` : '')}
          detail={timeRange(e.start, e.end, e.allDay)}
          lead={e.class ? <Dot color={classColorOf(e.class)} /> : undefined}
        />
      ))}
    </Section>
  );
}

// Classes are coloured by their place in the library's list; Coming up only has a name, so it can't line up
// exactly until /calendar/upcoming carries the class's own colour. A plain dot stands in for now.
function classColorOf(_name: string): string {
  return 'var(--accent)';
}

function RecentLectures({
  lectures,
  loading,
  error,
  onRetry,
}: {
  lectures: LectureSummary[] | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  if (error) return <Problem message="Couldn't load your recent lectures." onRetry={onRetry} />;
  if (loading && !lectures) return <SkeletonRows count={4} />;
  if (!lectures || lectures.length === 0) return null;
  return (
    <Section title="Recent lectures">
      {lectures.map((l) => (
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
  );
}

function Classes({
  overview,
  loading,
  error,
  onRetry,
}: {
  overview: { classes: ClassInfo[]; unsorted: number } | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  if (error) return <Problem message="Couldn't load your classes." onRetry={onRetry} />;
  if (loading && !overview) return <SkeletonRows count={5} withSubtitle={false} />;
  if (!overview) return null;
  if (overview.classes.length === 0 && overview.unsorted === 0)
    return (
      <Empty icon={<Folder size={40} />} title="Nothing here yet">
        Record a lecture on your library computer, and it'll show up here.
      </Empty>
    );
  return (
    <Section title="Classes">
      {overview.classes.map((c) => (
        <Row
          key={c.name}
          href={href({ name: 'class', class: c.name })}
          lead={<Dot color={classColor(c.color)} size={12} />}
          title={c.name}
          subtitle={c.code || undefined}
          detail={`${c.lectures}`}
          chevron
        />
      ))}
      {overview.unsorted > 0 ? (
        <Row
          href={href({ name: 'class', class: 'Unsorted' })}
          lead={<Dot color="var(--fg3)" size={12} />}
          title="Unsorted"
          detail={`${overview.unsorted}`}
          chevron
        />
      ) : null}
    </Section>
  );
}
