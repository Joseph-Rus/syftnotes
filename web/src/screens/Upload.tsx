import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { Attachment } from '../api/types';
import { fileSize } from '../format';
import { Camera, Folder, Photo } from '../ui/icons';
import { Button, Screen, Section } from '../ui/kit';
import { AttachmentRow } from './attachments';

const MAX_BYTES = 200 * 1024 * 1024;

interface Job {
  id: string;
  file: File;
  status: 'uploading' | 'done' | 'error';
  loaded: number;
  error?: string;
}

let seq = 0;

/** "Add files": from a lecture or a class, camera photos, Files (which offers PDFs and everything else) and
 * multiple at once, each with its own progress and a way to try again, the 200 MB limit said plainly, and the
 * list of what's already there refreshing with "Reading your handwriting…" until each has words. */
export function UploadScreen({ api, className, lecture }: { api: Api; className: string | null; lecture: string | null }) {
  const to = { class: className ?? undefined, lecture: lecture ?? undefined };
  const [jobs, setJobs] = useState<Job[]>([]);
  const [existing, setExisting] = useState<Attachment[] | undefined>();
  const filePicker = useRef<HTMLInputElement>(null);
  const cameraPicker = useRef<HTMLInputElement>(null);

  const loadExisting = async () => {
    try {
      setExisting(await api.attachments(lecture ? { lecture } : className ? { class: className } : {}));
    } catch {
      // shown fresh next time; the queue above still matters more right now
    }
  };

  useEffect(() => {
    void loadExisting();
  }, []);

  // Keep asking while anything is still being read, so "Reading your handwriting…" clears on its own.
  useEffect(() => {
    if (!existing?.some((a) => a.reading)) return;
    const t = setInterval(() => void loadExisting(), 4000);
    return () => clearInterval(t);
  }, [existing]);

  const upload = (job: Job) => {
    setJobs((js) => js.map((j) => (j.id === job.id ? { ...j, status: 'uploading', error: undefined, loaded: 0 } : j)));
    const controller = new AbortController();
    api
      .upload([job.file], to, (p) => setJobs((js) => js.map((j) => (j.id === job.id ? { ...j, loaded: p.loaded } : j))), controller.signal)
      .then(() => {
        setJobs((js) => js.map((j) => (j.id === job.id ? { ...j, status: 'done', loaded: job.file.size } : j)));
        void loadExisting();
      })
      .catch((e: unknown) => {
        const message = e instanceof ApiError ? e.detail : "Couldn't send that. Check your connection and try again.";
        setJobs((js) => js.map((j) => (j.id === job.id ? { ...j, status: 'error', error: message } : j)));
      });
  };

  const addFiles = (files: FileList | null) => {
    if (!files || files.length === 0) return;
    const added: Job[] = [];
    for (const file of Array.from(files)) {
      if (file.size > MAX_BYTES) {
        added.push({ id: `${seq++}`, file, status: 'error', loaded: 0, error: `That's more than 200 MB — pick something smaller.` });
        continue;
      }
      added.push({ id: `${seq++}`, file, status: 'uploading', loaded: 0 });
    }
    setJobs((js) => [...added, ...js]);
    for (const job of added) if (job.status === 'uploading') upload(job);
  };

  const where = lecture ? 'this lecture' : className ? className : 'Unsorted';
  const backTo = lecture ? { name: 'lecture' as const, id: lecture } : className ? { name: 'class' as const, class: className } : { name: 'library' as const };

  return (
    <Screen title="Add files" quiet back={{ label: lecture ? 'Lecture' : className ?? 'Library', to: backTo }}>
      <header class="detail-head">
        <h1 class="detail-title">Add files</h1>
        <div class="detail-meta">To {where}. Photos of handwritten notes are read, so they turn up in search and answers.</div>
      </header>
      <Section footer="Up to 200 MB at once: photos, PDFs, slides, documents.">
        <div class="upload-buttons">
          <button type="button" class="big-choice" onClick={() => cameraPicker.current?.click()}>
            <Camera size={28} />
            <span>Take a photo</span>
          </button>
          <button type="button" class="big-choice" onClick={() => filePicker.current?.click()}>
            <Folder size={28} />
            <span>Choose files</span>
          </button>
        </div>
        <input
          ref={cameraPicker}
          type="file"
          accept="image/*"
          capture="environment"
          class="visually-hidden"
          onChange={(e) => {
            addFiles(e.currentTarget.files);
            e.currentTarget.value = '';
          }}
        />
        <input
          ref={filePicker}
          type="file"
          multiple
          accept="image/*,application/pdf,.doc,.docx,.ppt,.pptx,.heic"
          class="visually-hidden"
          onChange={(e) => {
            addFiles(e.currentTarget.files);
            e.currentTarget.value = '';
          }}
        />
      </Section>

      {jobs.length > 0 ? (
        <Section title="Sending">
          {jobs.map((j) => (
            <JobRow key={j.id} job={j} onRetry={() => upload(j)} />
          ))}
        </Section>
      ) : null}

      {existing && existing.length > 0 ? (
        <Section title="Already here">
          {existing.map((a) => (
            <AttachmentRow key={a.id} attachment={a} url={undefined} />
          ))}
        </Section>
      ) : null}

    </Screen>
  );
}

function JobRow({ job, onRetry }: { job: Job; onRetry: () => void }) {
  const pct = job.file.size > 0 ? Math.min(100, Math.round((job.loaded / job.file.size) * 100)) : 0;
  return (
    <div class="upload-row">
      <div class="upload-row-icon">
        <Photo size={20} />
      </div>
      <div class="upload-row-body">
        <div class="upload-row-title">{job.file.name}</div>
        {job.status === 'uploading' ? (
          <div class="upload-progress">
            <div class="upload-progress-bar" style={{ width: `${pct}%` }} />
          </div>
        ) : job.status === 'error' ? (
          <div class="upload-row-error">{job.error}</div>
        ) : (
          <div class="upload-row-done">{fileSize(job.file.size)} · Sent</div>
        )}
      </div>
      {job.status === 'error' ? (
        <Button kind="tinted" small onClick={onRetry}>
          Retry
        </Button>
      ) : null}
    </div>
  );
}
