import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import type { Attachment, Lecture } from '../api/types';
import { useResource } from '../data/resource';
import { lectureMeta } from '../format';
import { href } from '../router';
import { renderMath } from '../ui/math';
import { Paperclip, Sparkle } from '../ui/icons';
import { Dot, IconButton, Problem, Row, Screen, Section, SkeletonRows } from '../ui/kit';
import { AttachmentRow } from './attachments';
import { useClassColors } from './classColors';

/** A lecture: which class and when, its topics, its notes (safe HTML from GET /lectures/{id}/rendered, formulas run
 * through KaTeX), the transcript it was written from, and what's attached to it. A lecture whose notes haven't
 * been written yet still shows its topics and transcript, and says the notes are coming. */
export function LectureScreen({ api, id }: { api: Api; id: string }) {
  const lecture = useResource(`lecture:${id}:full`, (signal) => api.lecture(id, signal));
  const notes = useResource(`lecture:${id}`, (signal) => api.rendered(id, signal));
  const attachments = useResource(`lecture:${id}:files`, (signal) => api.attachments({ lecture: id }, signal));
  const colorOf = useClassColors(api);
  const noteBody = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (noteBody.current && notes.data?.html) renderMath(noteBody.current);
  }, [notes.data?.html]);

  const refresh = async () => {
    await Promise.all([lecture.refresh(), notes.refresh(), attachments.refresh()]);
  };

  const l = lecture.data;
  const cls = l?.class ?? notes.data?.class ?? null;
  const backTo = cls ? { name: 'class' as const, class: cls } : { name: 'library' as const };
  const html = notes.data?.html ?? '';

  return (
    <Screen
      title={l?.title || notes.data?.title || 'Lecture'}
      quiet back={{ label: cls || 'Library', to: backTo }}
      onRefresh={refresh}
      actions={
        <IconButton label="Add files" href={href({ name: 'upload', class: null, lecture: id })}>
          <Paperclip size={22} />
        </IconButton>
      }
    >
      {lecture.error && !l ? (
        <Problem message={lecture.error.status === 404 ? "This lecture isn't in your library any more." : "Couldn't load this lecture."} onRetry={refresh} />
      ) : !l ? (
        <SkeletonRows count={3} />
      ) : (
        <>
          <header class="detail-head">
            {cls ? (
              <a class="detail-eyebrow" href={href({ name: 'class', class: cls })}>
                <Dot color={colorOf(cls)} size={8} /> {cls}
              </a>
            ) : null}
            <h1 class="detail-title selectable">{l.title || 'Untitled lecture'}</h1>
            <div class="detail-meta">{[lectureMeta(l.date, l.seconds), l.owner].filter(Boolean).join(' · ')}</div>
            {l.topics.length > 0 ? (
              <div class="topics">
                {l.topics.map((t) => (
                  <a class="topic" href={href({ name: 'search', q: t })}>
                    {t}
                  </a>
                ))}
              </div>
            ) : null}
          </header>

          {html ? (
            <div ref={noteBody} class="prose notes selectable" dangerouslySetInnerHTML={{ __html: html }} />
          ) : (
            <NoNotes lecture={l} />
          )}

          {l.transcript ? <Transcript text={l.transcript} open={!html} /> : null}
        </>
      )}
      <Attachments id={id} files={attachments.data} loading={attachments.loading} />
    </Screen>
  );
}

/** Where the notes are when there aren't any yet: being written, failed, or never asked for. */
function NoNotes({ lecture }: { lecture: Lecture }) {
  const words =
    lecture.status === 'queued' || lecture.status === 'working'
      ? 'Notes are being written. Pull down in a minute to see them.'
      : lecture.status === 'failed'
        ? `Notes couldn't be written${lecture.error ? `: ${lecture.error}` : ''}.`
        : lecture.summary
          ? lecture.summary
          : 'No notes were written for this lecture. The transcript is below.';
  return (
    <div class="section">
      <div class="callout">
        <Sparkle size={18} />
        <p>{words}</p>
      </div>
    </div>
  );
}

/** The transcript, "[04:05] words" a line, folded until asked for when there are notes to read instead. */
function Transcript({ text, open: startOpen }: { text: string; open: boolean }) {
  const [open, setOpen] = useState(startOpen);
  const lines = text
    .split('\n')
    .map((line) => /^\[(\d{1,2}:\d{2}(?::\d{2})?)\]\s*(.*)$/.exec(line.trim()))
    .filter((m): m is RegExpExecArray => !!m && !!m[2]);
  const plain = lines.length === 0;
  return (
    <Section title="Transcript">
      {open ? (
        <div class="transcript selectable">
          {plain ? (
            <p>{text}</p>
          ) : (
            lines.map((m) => (
              <p>
                <span class="stamp">{m[1]}</span>
                {m[2]}
              </p>
            ))
          )}
        </div>
      ) : (
        <Row title="Show the transcript" detail={plain ? undefined : `${lines.length} lines`} onClick={() => setOpen(true)} chevron />
      )}
    </Section>
  );
}

function Attachments({ id, files, loading }: { id: string; files: Attachment[] | undefined; loading: boolean }) {
  if (loading && !files) return null;
  return (
    <Section title="Attachments" footer={files && files.length > 0 ? undefined : 'Handwritten notes, slides or photos from this lecture.'}>
      {(files ?? []).map((a) => (
        <AttachmentRow key={a.id} attachment={a} url={undefined} />
      ))}
      <Row href={href({ name: 'upload', class: null, lecture: id })} lead={<Paperclip size={20} />} title={files && files.length > 0 ? 'Add more files' : 'Add files'} chevron />
    </Section>
  );
}
