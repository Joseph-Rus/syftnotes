import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { VoiceMemo } from '../api/types';
import { forget, useResource } from '../data/resource';
import { ago, fileSize } from '../format';
import { href } from '../router';
import { Check, Mic, Refresh, Trash } from '../ui/icons';
import { Button, Dot, IconButton, Problem, Screen, Section, Spinner } from '../ui/kit';
import { useClassColors } from './classColors';

/** A recording's name without its ending, as a first guess at the lecture's title: "Bio lab.m4a" → "Bio lab". */
export function titleFrom(name: string): string {
  const plain = name.replace(/\.[a-z0-9]{2,4}$/i, '').trim();
  return /^new recording( \d+)?$/i.test(plain) ? '' : plain;
}

/**
 * Import a voice memo: a recording made with the phone's own Voice Memos (Share → Save to Files, then chosen here)
 * goes to the library, and a computer that records writes it down as a lecture with notes, the way it does its own
 * recordings. Below, every memo sent and how it's getting on, asked again every few seconds while any is on its way.
 */
export function VoiceMemoScreen({ api }: { api: Api }) {
  const memos = useResource('voice-memos', (signal) => api.voiceMemos(signal));
  const library = useResource('library', (signal) => api.library(signal));
  const colorOf = useClassColors(api);
  const picker = useRef<HTMLInputElement>(null);
  const [file, setFile] = useState<File | null>(null);
  const [title, setTitle] = useState('');
  const [cls, setCls] = useState<string | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const moving =
    memos.data?.some((m) => m.state === 'waiting' || m.state === 'transcribing' || (m.state === 'done' && !m.ready)) ??
    false;
  useEffect(() => {
    if (!moving) return;
    const t = setInterval(() => void memos.refresh(), 5000);
    return () => clearInterval(t);
  }, [moving]);

  const send = async () => {
    if (!file || progress !== null) return;
    setProblem(null);
    setProgress(0);
    try {
      await api.sendVoiceMemo(file, { class: cls ?? undefined, title: title.trim() || undefined }, (p) =>
        setProgress(p.total ? p.loaded / p.total : 0),
      );
      setFile(null);
      setTitle('');
      await memos.refresh();
    } catch (e) {
      setProblem(e instanceof ApiError ? e.detail : "Couldn't send it. Check your connection and try again.");
    } finally {
      setProgress(null);
    }
  };

  const classes = library.data?.classes ?? [];
  return (
    <Screen title="Voice memo" quiet back={{ label: 'Library', to: { name: 'library' } }} onRefresh={memos.refresh}>
      <header class="detail-head">
        <h1 class="detail-title">Import a voice memo</h1>
        <div class="detail-meta">
          Record a lecture with Voice Memos, send it here, and it comes back as a lecture with notes.
        </div>
      </header>

      {!file ? (
        <>
          <Section title="From Voice Memos">
            <ol class="how-to">
              <li>
                In Voice Memos, tap the recording, then <strong>•••</strong>
              </li>
              <li>
                Tap <strong>Save to Files</strong> and save it
              </li>
              <li>Come back here and choose it</li>
            </ol>
          </Section>
          <div class="section">
            <Button kind="filled" wide onClick={() => picker.current?.click()}>
              <Mic size={20} /> Choose a recording
            </Button>
          </div>
        </>
      ) : (
        <>
          <Section title="Recording">
            <div class="memo-file">
              <span class="memo-icon">
                <Mic size={22} />
              </span>
              <span class="row-text">
                <span class="row-title">{file.name}</span>
                <span class="row-subtitle">{fileSize(file.size)}</span>
              </span>
              <button type="button" class="button plain" onClick={() => setFile(null)} disabled={progress !== null}>
                Change
              </button>
            </div>
          </Section>
          <Section title="Title">
            <input
              class="text-field"
              type="text"
              value={title}
              maxLength={120}
              placeholder="What the lecture was about"
              onInput={(e) => setTitle(e.currentTarget.value)}
            />
          </Section>
          <Section
            title="Class"
            footer={cls ? undefined : 'Leave it on Sort it for me and your library files it by what was said.'}
          >
            <div class="chip-wrap">
              <button type="button" class={cls === null ? 'chip on' : 'chip'} onClick={() => setCls(null)}>
                Sort it for me
              </button>
              {classes.map((c) => (
                <button type="button" class={cls === c.name ? 'chip on' : 'chip'} onClick={() => setCls(c.name)}>
                  <Dot color={colorOf(c.name)} size={7} /> {c.name}
                </button>
              ))}
            </div>
          </Section>
          <div class="section">
            {progress !== null ? (
              <div class="send-progress">
                <div class="upload-progress">
                  <div class="upload-progress-bar" style={{ width: `${Math.round(progress * 100)}%` }} />
                </div>
                <span>Sending… {Math.round(progress * 100)}%</span>
              </div>
            ) : (
              <Button kind="filled" wide onClick={() => void send()}>
                Send to my library
              </Button>
            )}
            {problem ? <p class="pair-error centred">{problem}</p> : null}
          </div>
        </>
      )}

      <input
        ref={picker}
        type="file"
        accept="audio/*,.m4a,.mp3,.wav,.aac,.caf"
        class="visually-hidden"
        onChange={(e) => {
          const f = e.currentTarget.files?.[0];
          e.currentTarget.value = '';
          if (!f) return;
          setFile(f);
          setTitle(titleFrom(f.name));
          setProblem(null);
        }}
      />

      {memos.error && !memos.data ? (
        memos.error.status === 404 ? null : (
          <Problem message="Couldn't load your voice memos." onRetry={memos.refresh} />
        )
      ) : memos.data && memos.data.length > 0 ? (
        <Section title="Sent">
          {memos.data.map((m) => (
            <MemoRow key={m.id} memo={m} api={api} color={colorOf(m.class)} onChange={memos.refresh} />
          ))}
        </Section>
      ) : null}
    </Screen>
  );
}

function MemoRow({
  memo,
  api,
  color,
  onChange,
}: {
  memo: VoiceMemo;
  api: Api;
  color: string;
  onChange: () => Promise<void>;
}) {
  const state =
    memo.state === 'waiting'
      ? 'Waiting for a computer that records to be on'
      : memo.state === 'transcribing' || (memo.state === 'done' && !memo.ready)
        ? `Being written down${memo.computer ? ` on ${memo.computer}` : ''}…`
        : memo.state === 'done'
          ? 'A lecture now'
          : (memo.error ?? "Couldn't be written down");
  const ready = memo.state === 'done' && memo.ready;
  const body = (
    <>
      <span class="memo-state-icon">
        {ready ? <Check size={18} /> : memo.state === 'failed' ? <span class="bang">!</span> : <Spinner size={16} />}
      </span>
      <span class="row-text">
        <span class="row-title">{memo.title || memo.name}</span>
        <span class={`row-subtitle${memo.state === 'failed' ? ' failed' : ''}`}>
          {memo.class ? (
            <>
              <Dot color={color} size={7} /> {memo.class} ·{' '}
            </>
          ) : null}
          {state} · {ago(memo.added)}
        </span>
      </span>
    </>
  );
  if (ready && memo.lecture)
    return (
      <a
        class="row tappable"
        href={href({ name: 'lecture', id: memo.lecture })}
        onClick={() => forget('recent-lectures')}
      >
        {body}
      </a>
    );
  return (
    <div class="row">
      {body}
      {memo.state === 'failed' ? (
        <>
          <IconButton label="Try again" onClick={() => void api.retryVoiceMemo(memo.id).then(onChange)}>
            <Refresh size={18} />
          </IconButton>
          <IconButton label="Remove" onClick={() => void api.deleteVoiceMemo(memo.id).then(onChange)}>
            <Trash size={18} />
          </IconButton>
        </>
      ) : null}
    </div>
  );
}
