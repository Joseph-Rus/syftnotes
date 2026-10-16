import { useEffect, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import { useResource } from '../data/resource';
import { Refresh } from '../ui/icons';
import { Button, Empty, IconButton, Screen, Segmented, Spinner } from '../ui/kit';

/** Flashcards and a quiz the library's AI makes from one lecture (as the iPhone app's Study does), kept on the phone. */
export interface StudyDeck {
  cards: { front: string; back: string }[];
  quiz: { question: string; choices: string[]; answer: number; why?: string }[];
}

/** The deck in an answer: its first JSON object, read leniently (the AI may wrap it in prose or a code fence). */
export function parseDeck(text: string): StudyDeck | null {
  const start = text.indexOf('{'),
    end = text.lastIndexOf('}');
  if (start < 0 || end <= start) return null;
  try {
    const d = JSON.parse(text.slice(start, end + 1)) as Partial<StudyDeck>;
    const cards = (d.cards ?? []).filter((c) => c && c.front && c.back);
    const quiz = (d.quiz ?? []).filter(
      (q) => q && Array.isArray(q.choices) && q.choices.length >= 2 && q.answer >= 0 && q.answer < q.choices.length,
    );
    return cards.length || quiz.length ? { cards, quiz } : null;
  } catch {
    return null;
  }
}

export function studyPrompt(title: string): string {
  return (
    `Make study material from the lecture "${title}": 12 flashcards on its key ideas, terms and formulas, and 8 ` +
    'multiple-choice questions that test understanding, not trivia. Reply with ONLY this JSON, nothing before or after:\n' +
    '{"cards":[{"front":"question or term","back":"short answer"}],"quiz":[{"question":"...","choices":["A","B","C","D"],"answer":0,"why":"one sentence"}]}\n' +
    '"answer" is the index of the right choice.'
  );
}

const key = (id: string) => `ss.study.${id}`;

function saved(id: string): StudyDeck | null {
  try {
    const raw = localStorage.getItem(key(id));
    return raw ? (JSON.parse(raw) as StudyDeck) : null;
  } catch {
    return null;
  }
}

export function StudyScreen({ api, id }: { api: Api; id: string }) {
  const lecture = useResource(`lecture:${id}:full`, (signal) => api.lecture(id, signal));
  const title = lecture.data?.title || 'Lecture';
  const [deck, setDeck] = useState<StudyDeck | null>(() => saved(id));
  const [making, setMaking] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const [mode, setMode] = useState<'cards' | 'quiz'>('cards');

  const make = async () => {
    if (making || !lecture.data) return;
    setMaking(true);
    setProblem(null);
    setDeck(null);
    let text = '';
    try {
      for await (const e of api.ask(studyPrompt(title), { lecture: id })) {
        if (e.kind === 'text') text += e.text;
        else if (e.kind === 'answer') text = e.text;
        else if (e.kind === 'done') text = e.reply.answer || text;
        else if (e.kind === 'error') setProblem(e.detail);
      }
      const d = parseDeck(text);
      if (d) {
        setDeck(d);
        try {
          localStorage.setItem(key(id), JSON.stringify(d));
        } catch {
          // a private window: the deck is made again next time
        }
      } else setProblem((p) => p ?? "The AI's answer wasn't flashcards. Try again.");
    } catch (e) {
      setProblem(e instanceof ApiError ? e.detail : "Couldn't reach your library.");
    } finally {
      setMaking(false);
    }
  };

  useEffect(() => {
    if (!deck && lecture.data && !making && !problem) void make();
  }, [lecture.data]);

  return (
    <Screen
      title="Study"
      quiet
      back={{ label: title, to: { name: 'lecture', id } }}
      actions={
        deck ? (
          <IconButton label="Make new ones" onClick={() => void make()}>
            <Refresh size={20} />
          </IconButton>
        ) : undefined
      }
    >
      {deck ? (
        <div class="study">
          <Segmented
            label="Study"
            value={mode}
            options={[
              { value: 'cards', label: 'Flashcards' },
              { value: 'quiz', label: 'Quiz' },
            ]}
            onChange={setMode}
          />
          {mode === 'cards' ? <Cards cards={deck.cards} /> : <Quiz questions={deck.quiz} />}
        </div>
      ) : making || !lecture.data ? (
        <div class="study-making">
          <Spinner />
          <p>Making flashcards and a quiz from your notes…</p>
        </div>
      ) : (
        <Empty title="Couldn't make them" action={<Button onClick={() => void make()}>Try again</Button>}>
          {problem ?? ''}
        </Empty>
      )}
    </Screen>
  );
}

const shuffle = <T,>(a: T[]) => [...a].sort(() => Math.random() - 0.5);

function Cards({ cards }: { cards: StudyDeck['cards'] }) {
  const [queue, setQueue] = useState(() => shuffle(cards));
  const [flipped, setFlipped] = useState(false);
  const [known, setKnown] = useState(0);
  const card = queue[0];
  const next = (knew: boolean) => {
    if (!card) return;
    setQueue((q) => (knew ? q.slice(1) : [...q.slice(1), card]));
    if (knew) setKnown((k) => k + 1);
    setFlipped(false);
  };
  if (!card)
    return (
      <Empty
        title={`You went through all ${cards.length}`}
        action={
          <Button
            onClick={() => {
              setQueue(shuffle(cards));
              setKnown(0);
            }}
          >
            Go again
          </Button>
        }
      />
    );
  return (
    <div class="cards">
      <p class="cards-count">
        {known} known · {queue.length} to go
      </p>
      <button
        type="button"
        class={flipped ? 'flashcard flipped' : 'flashcard'}
        onClick={() => setFlipped((f) => !f)}
        aria-live="polite"
      >
        <span class="flashcard-label">{flipped ? 'Answer' : 'Question'}</span>
        <span class="flashcard-text">{flipped ? card.back : card.front}</span>
      </button>
      <div class="cards-actions">
        <Button kind="tinted" onClick={() => next(false)}>
          Again
        </Button>
        <Button onClick={() => next(true)}>Knew it</Button>
      </div>
    </div>
  );
}

function Quiz({ questions }: { questions: StudyDeck['quiz'] }) {
  const [index, setIndex] = useState(0);
  const [picked, setPicked] = useState<number | null>(null);
  const [score, setScore] = useState(0);
  const q = questions[index];
  if (!q)
    return (
      <div class="quiz-score">
        <strong>
          {score} / {questions.length}
        </strong>
        <p>
          {score === questions.length
            ? 'Perfect.'
            : score * 2 >= questions.length
              ? 'Nice work.'
              : 'Worth another look at the notes.'}
        </p>
        <Button
          kind="tinted"
          onClick={() => {
            setIndex(0);
            setScore(0);
            setPicked(null);
          }}
        >
          Try again
        </Button>
      </div>
    );
  return (
    <div class="quiz">
      <p class="cards-count">
        Question {index + 1} of {questions.length}
      </p>
      <h2 class="quiz-question">{q.question}</h2>
      {q.choices.map((c, i) => (
        <button
          type="button"
          class={`quiz-choice${picked !== null && i === q.answer ? ' right' : ''}${picked === i && i !== q.answer ? ' wrong' : ''}`}
          disabled={picked !== null}
          onClick={() => {
            setPicked(i);
            if (i === q.answer) setScore((s) => s + 1);
          }}
        >
          {c}
        </button>
      ))}
      {picked !== null ? (
        <>
          {q.why ? <p class="quiz-why">{q.why}</p> : null}
          <Button
            wide
            onClick={() => {
              setIndex((n) => n + 1);
              setPicked(null);
            }}
          >
            {index + 1 < questions.length ? 'Next question' : 'See my score'}
          </Button>
        </>
      ) : null}
    </div>
  );
}
