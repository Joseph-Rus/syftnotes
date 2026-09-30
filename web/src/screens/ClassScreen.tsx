import type { Api } from '../api/client';
import type { Attachment, DueList, LectureSummary } from '../api/types';
import { useResource } from '../data/resource';
import { dueWhen, lectureMeta, groupBy, lectureGroup } from '../format';
import { href } from '../router';
import { Doc, Paperclip, PlusCircle } from '../ui/icons';
import { Empty, IconButton, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { AttachmentRow } from './attachments';
import { statusWord } from './Home';

/** A class's home: what's still to hand in for it (when Canvas is linked), its lectures (newest first, grouped by
 * when), and anything attached to the class itself rather than one lecture (a syllabus, a handout). "Add files"
 * sends them here as a class attachment. */
export function ClassScreen({ api, className }: { api: Api; className: string }) {
  const key = `class:${className}`;
  const lectures = useResource(key, (signal) => api.lectures({ class: className, limit: 200 }, signal));
  const attachments = useResource(`${key}:files`, (signal) => api.attachments({ class: className }, signal));
  const due = useResource('due', (signal) => api.due(signal));
  const classAttachments = (attachments.data ?? []).filter((a) => !a.lecture);

  const refresh = async () => {
    await Promise.all([lectures.refresh(), attachments.refresh(), due.refresh()]);
  };

  return (
    <Screen
      title={className}
      large
      back={{ label: 'Library', to: { name: 'library' } }}
      onRefresh={refresh}
      actions={
        <IconButton label="Add files" href={href({ name: 'upload', class: className, lecture: null })}>
          <PlusCircle size={24} />
        </IconButton>
      }
    >
      <ToHandIn className={className} list={due.data} />
      <Lectures className={className} lectures={lectures.data} loading={lectures.loading} error={lectures.error} onRetry={lectures.refresh} />
      <ClassFiles className={className} files={classAttachments} loading={attachments.loading} />
    </Screen>
  );
}

/** What's still to hand in for this class, soonest first. Nothing when Canvas isn't linked or nothing's due. */
function ToHandIn({ className, list }: { className: string; list: DueList | undefined }) {
  if (!list) return null;
  const items = list.groups
    .filter((g) => g.key === 'overdue' || g.key === 'week' || g.key === 'later' || g.key === 'undated')
    .flatMap((g) => g.items.map((i) => ({ ...i, overdue: g.key === 'overdue' })))
    .filter((i) => i.class === className);
  if (items.length === 0) return null;
  return (
    <Section title="To hand in">
      {items.slice(0, 5).map((i) => (
        <Row
          key={i.id}
          href={href({ name: 'assignment', class: i.class, id: String(i.id) })}
          title={i.name}
          subtitle={i.points ? `${i.points} pts` : undefined}
          detail={<span class={i.overdue || i.missing ? 'due-state missing' : 'due-state'}>{i.due ? dueWhen(i.due) : 'No due date'}</span>}
          chevron
        />
      ))}
    </Section>
  );
}

function Lectures({
  className,
  lectures,
  loading,
  error,
  onRetry,
}: {
  className: string;
  lectures: LectureSummary[] | undefined;
  loading: boolean;
  error: unknown;
  onRetry: () => void;
}) {
  if (error) return <Problem message="Couldn't load these lectures." onRetry={onRetry} />;
  if (loading && !lectures) return <SkeletonRows count={6} />;
  if (!lectures || lectures.length === 0)
    return (
      <Empty icon={<Doc size={40} />} title="No lectures yet">
        Record a lecture in {className} on your library computer.
      </Empty>
    );
  const groups = groupBy(lectures, (l) => lectureGroup(l.date));
  return (
    <>
      {groups.map((g) => (
        <Section key={g.label} title={g.label}>
          {g.items.map((l) => (
            <Row
              key={l.id}
              href={href({ name: 'lecture', id: l.id })}
              title={l.title || 'Untitled lecture'}
              subtitle={[lectureMeta(l.date, l.seconds), ...l.topics.slice(0, 2)].filter(Boolean).join(' · ')}
              detail={l.status !== 'done' ? statusWord(l.status) : undefined}
              chevron
            />
          ))}
        </Section>
      ))}
    </>
  );
}

function ClassFiles({ className, files, loading }: { className: string; files: Attachment[]; loading: boolean }) {
  if (loading && files.length === 0) return null;
  return (
    <Section
      title="Files"
      footer={
        files.length === 0 ? `Add slides, handouts or scans to ${className} — up to 200 MB at once.` : undefined
      }
    >
      {files.length === 0 ? (
        <Row href={href({ name: 'upload', class: className, lecture: null })} lead={<Paperclip size={20} />} title="Add files" chevron />
      ) : (
        <>
          {files.map((a) => (
            <AttachmentRow key={a.id} attachment={a} url={undefined} />
          ))}
          <Row href={href({ name: 'upload', class: className, lecture: null })} lead={<Paperclip size={20} />} title="Add more files" chevron />
        </>
      )}
    </Section>
  );
}
