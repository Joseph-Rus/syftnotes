import type { Api } from '../api/client';
import { useResource } from '../data/resource';
import { dayLabel, groupBy, parseWhen, timeRange } from '../format';
import { Calendar } from '../ui/icons';
import { Empty, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';

/** Everything on the student's calendars for the next few days, grouped by day, each event with the class it
 * matched. Empty (rather than an error) when the library has no calendar sending events yet. */
export function ComingUpScreen({ api }: { api: Api }) {
  const upcoming = useResource('upcoming-full', (signal) => api.upcoming(signal));

  if (upcoming.error)
    return (
      <Screen title="Coming up" large back={{ label: 'Library', to: { name: 'library' } }}>
        <Problem message="Couldn't load what's coming up." onRetry={upcoming.refresh} />
      </Screen>
    );
  if (upcoming.loading && !upcoming.data)
    return (
      <Screen title="Coming up" large back={{ label: 'Library', to: { name: 'library' } }}>
        <SkeletonRows count={5} />
      </Screen>
    );
  const events = upcoming.data?.events ?? [];
  return (
    <Screen title="Coming up" large back={{ label: 'Library', to: { name: 'library' } }} onRefresh={upcoming.refresh}>
      {events.length === 0 ? (
        <Empty icon={<Calendar size={40} />} title="Nothing on your calendar">
          Once a laptop is sending your calendar, what's coming up shows up here.
        </Empty>
      ) : (
        groupBy(events, (e) => dayLabel(parseWhen(e.start) ?? new Date())).map((g) => (
          <Section key={g.label} title={g.label}>
            {g.items.map((e) => (
              <Row key={e.id} title={e.title} subtitle={e.class ?? undefined} detail={timeRange(e.start, e.end, e.allDay)} />
            ))}
          </Section>
        ))
      )}
    </Screen>
  );
}
