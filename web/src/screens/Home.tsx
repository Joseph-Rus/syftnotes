import { useState } from 'preact/hooks';
import type { Api } from '../api/client';
import type { ClassInfo, DueList, LectureSummary } from '../api/types';
import { dueWhen, lectureMeta } from '../format';
import { useResource } from '../data/resource';
import { href } from '../router';
import { classColor } from '../theme/themes';
import { ChevronRight, Folder, Mic, Paperclip, PlusCircle } from '../ui/icons';
import { Dot, Empty, IconButton, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { useClassColors } from './classColors';

/** The library's front screen: the next few things due (when Canvas is linked), the newest lectures with their
 * classes and topics, and every class with its dot. + adds files or a voice memo. */
export function Home({ api, offline }: { api: Api; offline: boolean }) {
  const library = useResource('library', (signal) => api.library(signal));
  const recent = useResource('recent-lectures', (signal) => api.lectures({ limit: 6 }, signal));
  const due = useResource('due', (signal) => api.due(signal));
  const colorOf = useClassColors(api);
  const [adding, setAdding] = useState(false);

  const refresh = async () => {
    await Promise.all([library.refresh(), recent.refresh(), due.refresh()]);
  };

  const writing = library.data?.writing ?? 0;
  const subtitle = offline
    ? 'Offline — showing what was saved before'
    : writing > 0
      ? `Writing notes for ${writing} lecture${writing === 1 ? '' : 's'}…`
      : library.data?.name;

  return (
    <Screen
      title="Library"
      large
      subtitle={subtitle}
      onRefresh={refresh}
      actions={
        <IconButton label="Add" onClick={() => setAdding(true)}>
          <PlusCircle size={26} />
        </IconButton>
      }
    >
      <DueSoon list={due.data} colorOf={colorOf} />
      <RecentLectures lectures={recent.data} loading={recent.loading} error={recent.error} onRetry={recent.refresh} colorOf={colorOf} />
      <Classes overview={library.data} loading={library.loading} error={library.error} onRetry={library.refresh} />
      {adding ? <AddSheet onClose={() => setAdding(false)} /> : null}
    </Screen>
  );
}

/** + : what can be added from here. */
function AddSheet({ onClose }: { onClose: () => void }) {
  return (
    <div class="sheet-backdrop" onClick={onClose}>
      <div class="sheet" role="dialog" aria-label="Add" onClick={(e) => e.stopPropagation()}>
        <div class="sheet-grabber" />
        <a class="sheet-option" href={href({ name: 'memo' })} onClick={onClose}>
          <span class="sheet-icon">
            <Mic size={22} />
          </span>
          <span>
            <strong>Import a voice memo</strong>
            <small>A recording from Voice Memos becomes a lecture, with notes.</small>
          </span>
        </a>
        <a class="sheet-option" href={href({ name: 'upload', class: null, lecture: null })} onClick={onClose}>
          <span class="sheet-icon">
            <Paperclip size={22} />
          </span>
          <span>
            <strong>Add files</strong>
            <small>Photos of handwritten notes, slides, PDFs.</small>
          </span>
        </a>
        <button type="button" class="sheet-cancel" onClick={onClose}>
          Cancel
        </button>
      </div>
    </div>
  );
}

/** The next three things to hand in, with a way into the Due tab. Hidden when Canvas isn't linked. */
function DueSoon({ list, colorOf }: { list: DueList | undefined; colorOf: (c: string | null) => string }) {
  if (!list) return null;
  const items = list.groups
    .filter((g) => g.key === 'overdue' || g.key === 'week' || g.key === 'later')
    .flatMap((g) => g.items.map((i) => ({ ...i, overdue: g.key === 'overdue' })))
    .slice(0, 3);
  if (items.length === 0) return null;
  return (
    <Section title="Due soon">
      {items.map((i) => (
        <Row
          key={`${i.class}-${i.id}`}
          href={href({ name: 'assignment', class: i.class, id: String(i.id) })}
          lead={<Dot color={colorOf(i.class)} />}
          title={i.name}
          subtitle={i.class}
          detail={<span class={i.overdue ? 'due-state missing' : 'due-state'}>{dueWhen(i.due)}</span>}
          chevron
        />
      ))}
      <a class="row tappable see-all" href={href({ name: 'due' })}>
        <span class="row-text">
          <span class="row-title">All {list.to_hand_in} to hand in</span>
        </span>
        <ChevronRight size={16} class="row-chevron" />
      </a>
    </Section>
  );
}

function RecentLectures({
  lectures,
  loading,
  error,
  onRetry,
  colorOf,
}: {
  lectures: LectureSummary[] | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
  colorOf: (c: string | null) => string;
}) {
  if (error && !lectures) return <Problem message="Couldn't load your recent lectures." onRetry={onRetry} />;
  if (loading && !lectures) return <SkeletonRows count={3} />;
  if (!lectures || lectures.length === 0) return null;
  return (
    <Section title="Recent lectures">
      {lectures.map((l) => (
        <a key={l.id} class="row tappable lecture-row" href={href({ name: 'lecture', id: l.id })}>
          <span class="lecture-bar" style={{ background: colorOf(l.class) }} />
          <span class="row-text">
            <span class="row-title">{l.title || 'Untitled lecture'}</span>
            <span class="row-subtitle">{[l.class, lectureMeta(l.date, l.seconds)].filter(Boolean).join(' · ')}</span>
            {l.status !== 'done' ? (
              <span class="lecture-status">{statusWord(l.status)}</span>
            ) : l.topics.length > 0 ? (
              <span class="lecture-topics">{l.topics.slice(0, 3).join(' · ')}</span>
            ) : null}
          </span>
          <ChevronRight size={16} class="row-chevron" />
        </a>
      ))}
    </Section>
  );
}

export function statusWord(status: string): string {
  if (status === 'queued') return 'Waiting to be written…';
  if (status === 'working') return 'Writing notes…';
  if (status === 'failed') return "Notes couldn't be written";
  return status;
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
  if (error && !overview) return <Problem message="Couldn't load your classes." onRetry={onRetry} />;
  if (loading && !overview) return <SkeletonRows count={5} withSubtitle={false} />;
  if (!overview) return null;
  if (overview.classes.length === 0 && overview.unsorted === 0)
    return (
      <Empty icon={<Folder size={40} />} title="Nothing here yet">
        Record a lecture with Study Stash on your computer, or import a voice memo with +.
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
          detail={c.lectures === 0 ? '' : `${c.lectures}`}
          chevron
        />
      ))}
      {overview.unsorted > 0 ? (
        <Row href={href({ name: 'class', class: 'Unsorted' })} lead={<Dot color="var(--fg3)" size={12} />} title="Unsorted" detail={`${overview.unsorted}`} chevron />
      ) : null}
    </Section>
  );
}
