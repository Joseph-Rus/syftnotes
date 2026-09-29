import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { AskSource } from '../api/types';
import { persistedStore, useStore } from '../data/store';
import { useResource } from '../data/resource';
import { clock, shortDate, parseWhen } from '../format';
import { href } from '../router';
import { ArrowUp, Sparkle, Trash } from '../ui/icons';
import { Dot, Empty, IconButton, Screen, Spinner } from '../ui/kit';
import { useClassColors } from './classColors';

/** One question and what came back: the answer as it was written, the lectures it drew on, or why there's none. */
interface Turn {
  id: string;
  question: string;
  scope: string | null;
  answer: string;
  sources: AskSource[];
  state: 'asking' | 'done' | 'failed';
  problem?: string;
  engine?: string;
}

/** The questions asked on this phone, newest last, kept between visits (the answers are the library's own words,
 * so there's nothing here the library doesn't already have). */
const history = persistedStore<Turn[]>('ss.ask', []);

const SUGGESTIONS = [
  'What did we cover this week?',
  'Explain the hardest idea from my last lecture',
  'Quiz me on my most recent notes',
];

/** Ask about your notes (POST /api/v2/ai/ask, streamed): the answer arrives as it's written, from the library's AI,
 * with the lectures it used underneath, each a way into that lecture. Everything, or one class. */
export function AskScreen({ api }: { api: Api }) {
  const turns = useStore(history);
  const library = useResource('library', (signal) => api.library(signal));
  const colorOf = useClassColors(api);
  const [question, setQuestion] = useState('');
  const [scope, setScope] = useState<string | null>(null);
  const busy = turns.some((t) => t.state === 'asking');
  const end = useRef<HTMLDivElement>(null);
  const warmed = useRef(false);

  useEffect(() => {
    end.current?.scrollIntoView({ block: 'end', behavior: 'smooth' });
  }, [turns.length, turns[turns.length - 1]?.answer.length]);

  // A question stopped by leaving the screen is marked as such, not left asking forever.
  useEffect(() => {
    history.set((ts) =>
      ts.map((t) => (t.state === 'asking' ? { ...t, state: 'failed', problem: 'Stopped before it was answered.' } : t)),
    );
  }, []);

  const noAi = library.data && library.data.ask === false;

  const ask = async (text: string) => {
    const q = text.trim();
    if (!q || busy) return;
    setQuestion('');
    const id = `${Date.now()}`;
    const update = (change: Partial<Turn>) =>
      history.set((ts) => ts.map((t) => (t.id === id ? { ...t, ...change } : t)));
    history.set((ts) => [...ts.slice(-30), { id, question: q, scope, answer: '', sources: [], state: 'asking' }]);
    let answer = '';
    try {
      for await (const e of api.ask(q, scope ? { class: scope } : {})) {
        if (e.kind === 'text') {
          answer += e.text;
          update({ answer });
        } else if (e.kind === 'answer') {
          answer = e.text;
          update({ answer });
        } else if (e.kind === 'done') {
          update({
            answer: e.reply.answer || answer,
            sources: e.reply.sources,
            state: 'done',
            engine: e.reply.engine_name,
          });
          return;
        } else if (e.kind === 'error') {
          update({ state: 'failed', problem: e.detail || 'The library could not answer that.' });
          return;
        }
      }
      update({ state: answer ? 'done' : 'failed', problem: answer ? undefined : 'No answer came back.' });
    } catch (e) {
      update({ state: 'failed', problem: e instanceof ApiError ? e.detail : "Couldn't reach your library." });
    }
  };

  return (
    <Screen
      title="Ask"
      large
      subtitle="Answers from your own notes and lectures"
      class="ask-screen"
      actions={
        turns.length > 0 ? (
          <IconButton label="Clear these questions" onClick={() => history.set([])}>
            <Trash size={20} />
          </IconButton>
        ) : undefined
      }
    >
      {noAi ? (
        <Empty icon={<Sparkle size={40} />} title="Asking isn't set up yet">
          Choose an AI in Study Stash's Settings → AI engines on your library's computer, then ask here.
        </Empty>
      ) : turns.length === 0 ? (
        <div class="ask-start">
          <Empty icon={<Sparkle size={40} />} title="Ask about your notes">
            Questions are answered from what's in your library, with the lectures each answer used.
          </Empty>
          <div class="suggestions">
            {SUGGESTIONS.map((s) => (
              <button type="button" class="suggestion" onClick={() => void ask(s)}>
                {s}
              </button>
            ))}
          </div>
        </div>
      ) : (
        <div class="thread">
          {turns.map((t) => (
            <TurnView key={t.id} turn={t} colorOf={colorOf} />
          ))}
        </div>
      )}
      <div ref={end} class="thread-end" />
      {noAi ? null : (
        <form
          class="composer"
          onSubmit={(e) => {
            e.preventDefault();
            void ask(question);
          }}
        >
          <div class="scope-chips" role="radiogroup" aria-label="Ask about">
            <button
              type="button"
              role="radio"
              aria-checked={scope === null}
              class={scope === null ? 'chip on' : 'chip'}
              onClick={() => setScope(null)}
            >
              All classes
            </button>
            {(library.data?.classes ?? []).map((c) => (
              <button
                type="button"
                role="radio"
                aria-checked={scope === c.name}
                class={scope === c.name ? 'chip on' : 'chip'}
                onClick={() => setScope(scope === c.name ? null : c.name)}
              >
                <Dot color={colorOf(c.name)} size={7} /> {c.name}
              </button>
            ))}
          </div>
          <div class="composer-row">
            <textarea
              rows={1}
              value={question}
              placeholder={scope ? `Ask about ${scope}` : 'Ask about your notes'}
              enterkeyhint="send"
              onFocus={() => {
                if (!warmed.current) {
                  warmed.current = true;
                  void api.warm();
                }
              }}
              onInput={(e) => {
                const el = e.currentTarget;
                setQuestion(el.value);
                el.style.height = 'auto';
                el.style.height = `${Math.min(el.scrollHeight, 120)}px`;
              }}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  void ask(question);
                }
              }}
            />
            <button type="submit" class="send" disabled={!question.trim() || busy} aria-label="Ask">
              {busy ? <Spinner size={18} /> : <ArrowUp size={20} />}
            </button>
          </div>
        </form>
      )}
    </Screen>
  );
}

function TurnView({ turn, colorOf }: { turn: Turn; colorOf: (c: string | null) => string }) {
  return (
    <div class="turn">
      <div class="bubble mine selectable">
        {turn.scope ? <span class="bubble-scope">{turn.scope}</span> : null}
        {turn.question}
      </div>
      <div class="bubble theirs">
        {turn.answer ? (
          <div class="answer selectable">{turn.answer}</div>
        ) : turn.state === 'asking' ? (
          <div class="answer thinking">
            <Spinner size={16} /> Reading your notes…
          </div>
        ) : null}
        {turn.state === 'failed' ? <p class="answer-problem">{turn.problem}</p> : null}
        {turn.sources.length > 0 ? (
          <div class="sources">
            {dedupe(turn.sources).map((s) =>
              s.id ? (
                <a class="source" href={href({ name: 'lecture', id: s.id })}>
                  <Dot color={colorOf(s.class)} size={7} />
                  <span class="source-title">{s.title}</span>
                  <span class="source-meta">{sourceMeta(s)}</span>
                </a>
              ) : null,
            )}
          </div>
        ) : null}
      </div>
    </div>
  );
}

/** One chip per lecture, however many passages of it the answer used. */
function dedupe(sources: AskSource[]): AskSource[] {
  const seen = new Set<string>();
  return sources.filter((s) => {
    const k = s.id ?? s.title;
    if (seen.has(k)) return false;
    seen.add(k);
    return true;
  });
}

function sourceMeta(s: AskSource): string {
  const d = parseWhen(s.date);
  return [d ? shortDate(d) : null, s.at != null ? clock(s.at) : null].filter(Boolean).join(' · ');
}
