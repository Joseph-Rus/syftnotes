import { useState } from 'preact/hooks';
import type { Api } from '../api/client';
import type { DueItem, DueList, Upcoming } from '../api/types';
import { ago, dayLabel, dueWhen, parseWhen, relative, timeRange } from '../format';
import { useResource } from '../data/resource';
import { href } from '../router';
import { Calendar, Checklist, ChevronRight } from '../ui/icons';
import { Dot, Empty, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { useClassColors } from './classColors';

/** What's due, the way the Mac's Due list has it (GET /api/v2/canvas/due): the next thing to hand in first, today's
 * and tomorrow's classes from the calendar, then everything still to do by when it's due, and what was handed in
 * this week folded away at the bottom. */
export function DueScreen({ api }: { api: Api }) {
  const due = useResource('due', (signal) => api.due(signal));
  const upcoming = useResource('upcoming', (signal) => api.upcoming(signal));
  const colorOf = useClassColors(api);

  const refresh = async () => {
    await Promise.all([due.refresh(), upcoming.refresh()]);
  };

  const list = due.data;
  const subtitle = list ? dueSubtitle(list) : undefined;

  return (
    <Screen title="Due" large subtitle={subtitle} onRefresh={refresh}>
      {due.error && !list ? (
        <Problem message="Couldn't load what's due." onRetry={due.refresh} />
      ) : due.loading && !list ? (
        <SkeletonRows count={6} />
      ) : list ? (
        <>
          {list.next ? <NextUp item={list.next} color={colorOf(list.next.class)} /> : null}
          <Today data={upcoming.data} colorOf={colorOf} />
          <Groups list={list} colorOf={colorOf} />
        </>
      ) : null}
    </Screen>
  );
}

function dueSubtitle(list: DueList): string {
  const left = list.to_hand_in === 0 ? 'Nothing to hand in' : `${list.to_hand_in} to hand in`;
  return list.synced ? `${left} · synced ${ago(list.synced)}` : left;
}

/** The next thing to hand in, large: what, which class, when, and how long that is from now. */
function NextUp({ item, color }: { item: DueItem; color: string }) {
  return (
    <div class="section">
      <a class="next-up" href={href({ name: 'assignment', class: item.class, id: String(item.id) })}>
        <span class="next-up-eyebrow">Next up</span>
        <span class="next-up-title">{item.name}</span>
        <span class="next-up-class">
          <Dot color={color} size={8} /> {item.class}
        </span>
        <span class="next-up-when">
          <strong>{dueWhen(item.due)}</strong>
          {item.due ? <span> · {relative(item.due)}</span> : null}
          {item.points ? <span> · {item.points} pts</span> : null}
        </span>
        <ChevronRight size={18} class="next-up-chevron" />
      </a>
    </div>
  );
}

/** Today's and tomorrow's events from the calendar the library keeps, when it has any. */
function Today({ data, colorOf }: { data: Upcoming | undefined; colorOf: (c: string | null) => string }) {
  const now = new Date();
  const soon = (data?.events ?? []).filter((e) => {
    const d = parseWhen(e.start);
    if (!d) return false;
    const days = Math.round(
      (new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime() -
        new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime()) /
        86_400_000,
    );
    return days === 0 || days === 1;
  });
  if (soon.length === 0) return null;
  return (
    <Section title="Classes today and tomorrow">
      {soon.map((e) => (
        <Row
          key={e.id}
          lead={e.class ? <Dot color={colorOf(e.class)} /> : <Calendar size={18} />}
          title={e.title}
          subtitle={[dayLabel(parseWhen(e.start) ?? now), e.location].filter(Boolean).join(' · ')}
          detail={timeRange(e.start, e.end, e.allDay)}
        />
      ))}
    </Section>
  );
}

function Groups({ list, colorOf }: { list: DueList; colorOf: (c: string | null) => string }) {
  const [showDone, setShowDone] = useState(false);
  const todo = list.groups.filter((g) => g.key !== 'handed_in' && g.items.length > 0);
  const done = list.groups.find((g) => g.key === 'handed_in')?.items ?? [];
  if (todo.length === 0 && done.length === 0)
    return (
      <Empty icon={<Checklist size={40} />} title="Nothing due">
        Link your classes to Canvas in Study Stash on your computer, and what's due shows up here.
      </Empty>
    );
  return (
    <>
      {todo.length === 0 ? (
        <Empty icon={<Checklist size={40} />} title="All caught up">
          Nothing left to hand in.
        </Empty>
      ) : null}
      {todo.map((g) => (
        <Section key={g.key} title={g.label} class={g.key === 'overdue' ? 'overdue' : undefined}>
          {g.items.map((item) => (
            <DueRow key={`${item.class}-${item.id}`} item={item} color={colorOf(item.class)} />
          ))}
        </Section>
      ))}
      {done.length > 0 ? (
        <Section title="Handed in this week">
          {showDone ? (
            done.map((item) => <DueRow key={`${item.class}-${item.id}`} item={item} color={colorOf(item.class)} />)
          ) : (
            <Row title={`Show ${done.length} handed in`} onClick={() => setShowDone(true)} chevron />
          )}
        </Section>
      ) : null}
    </>
  );
}

/** One piece of work: its name, class and when it's due, and on the right how it stands ("To do", "Missing", a score). */
export function DueRow({ item, color }: { item: DueItem; color: string }) {
  const right = item.score_text || item.label;
  return (
    <Row
      href={href({ name: 'assignment', class: item.class, id: String(item.id) })}
      lead={<Dot color={color} />}
      title={item.name}
      subtitle={`${item.class} · ${dueWhen(item.due)}`}
      detail={<span class={item.missing ? 'due-state missing' : 'due-state'}>{right}</span>}
      chevron
    />
  );
}
