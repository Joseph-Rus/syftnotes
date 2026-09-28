import { useEffect, useRef } from 'preact/hooks';
import type { Api } from '../api/client';
import type { Attachment } from '../api/types';
import { useResource } from '../data/resource';
import { lectureMeta } from '../format';
import { href } from '../router';
import { renderMath } from '../ui/math';
import { Paperclip } from '../ui/icons';
import { Empty, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { AttachmentRow } from './attachments';

/** A lecture: its notes, rendered as safe HTML by the library (GET /lectures/{id}/rendered) with KaTeX run over
 * the formulas, and what's attached to it. */
export function LectureScreen({ api, id }: { api: Api; id: string }) {
  const notes = useResource(`lecture:${id}`, (signal) => api.rendered(id, signal));
  const attachments = useResource(`lecture:${id}:files`, (signal) => api.attachments({ lecture: id }, signal));
  const noteBody = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (noteBody.current && notes.data) renderMath(noteBody.current);
  }, [notes.data]);

  const refresh = async () => {
    await Promise.all([notes.refresh(), attachments.refresh()]);
  };

  const backTo = notes.data?.class ? { name: 'class' as const, class: notes.data.class } : { name: 'library' as const };

  return (
    <Screen
      title={notes.data?.title || 'Lecture'}
      back={{ label: notes.data?.class || 'Library', to: backTo }}
      onRefresh={refresh}
      actions={
        <a class="icon-button" href={href({ name: 'upload', class: null, lecture: id })} aria-label="Add files" title="Add files">
          <Paperclip size={22} />
        </a>
      }
    >
      {notes.error ? (
        <Problem message="Couldn't load these notes." onRetry={notes.refresh} />
      ) : !notes.data && notes.loading ? (
        <SkeletonRows count={3} />
      ) : notes.data ? (
        <>
          <div class="lecture-meta">{lectureMeta(notes.data.date, null)}</div>
          <div ref={noteBody} class="prose selectable" dangerouslySetInnerHTML={{ __html: notes.data.html }} />
        </>
      ) : null}
      <Attachments id={id} files={attachments.data} loading={attachments.loading} />
    </Screen>
  );
}

function Attachments({ id, files, loading }: { id: string; files: Attachment[] | undefined; loading: boolean }) {
  if (loading && !files) return null;
  return (
    <Section title="Attachments">
      {files && files.length > 0 ? (
        files.map((a) => <AttachmentRow key={a.id} attachment={a} url={undefined} />)
      ) : (
        <Empty icon={<Paperclip size={32} />} title="Nothing attached yet">
          Add handwritten notes, slides or photos from this lecture.
        </Empty>
      )}
      <Row href={href({ name: 'upload', class: null, lecture: id })} lead={<Paperclip size={20} />} title="Add files" chevron />
    </Section>
  );
}
