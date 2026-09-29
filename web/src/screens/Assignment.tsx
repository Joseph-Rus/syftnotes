import { useEffect, useRef } from 'preact/hooks';
import type { Api } from '../api/client';
import type { AssignmentDetail } from '../api/types';
import { dueWhen, relative } from '../format';
import { useResource } from '../data/resource';
import { renderMath } from '../ui/math';
import { External } from '../ui/icons';
import { Button, Dot, Problem, Screen, Section, SkeletonRows } from '../ui/kit';
import { useClassColors } from './classColors';

/** One assignment, as the Mac's assignment page has it (GET /api/v2/canvas/assignment): when it's due, what it's
 * worth and how it stands, the instructions, the rubric, what the teacher said, and a way into Canvas to hand it in. */
export function AssignmentScreen({ api, className, id }: { api: Api; className: string; id: string }) {
  const found = useResource(`assignment:${className}:${id}`, (signal) => api.assignment(className, id, signal));
  const colorOf = useClassColors(api);
  const a = found.data;

  return (
    <Screen
      title={a?.name ?? 'Assignment'}
      quiet
      back={{ label: 'Due', to: { name: 'due' } }}
      onRefresh={found.refresh}
    >
      {found.error && !a ? (
        <Problem
          message={
            found.error.status === 404 ? "This isn't in your library any more." : "Couldn't load this assignment."
          }
          onRetry={found.refresh}
        />
      ) : !a ? (
        <SkeletonRows count={4} />
      ) : (
        <Page a={a} color={colorOf(a.class)} />
      )}
    </Screen>
  );
}

function Page({ a, color }: { a: AssignmentDetail; color: string }) {
  const body = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (body.current) renderMath(body.current);
  }, [a.instructions_html]);

  const status = a.score_text || a.label;
  return (
    <>
      <header class="detail-head">
        <div class="detail-eyebrow">
          <Dot color={color} size={8} /> {a.class} · {kindWord(a.kind)}
        </div>
        <h1 class="detail-title selectable">{a.name}</h1>
      </header>

      <div class="section">
        <div class="tiles">
          <div class="tile">
            <span class="tile-label">Due</span>
            <span class="tile-value">{dueWhen(a.due)}</span>
            {a.due ? <span class="tile-sub">{relative(a.due)}</span> : null}
          </div>
          <div class="tile">
            <span class="tile-label">Points</span>
            <span class="tile-value">{a.points ?? '—'}</span>
          </div>
          <div class="tile">
            <span class="tile-label">{a.graded_at ? 'Score' : 'Status'}</span>
            <span class={`tile-value${a.missing ? ' missing' : ''}`}>{status}</span>
          </div>
        </div>
      </div>

      {a.instructions_html ? (
        <Section title="Instructions">
          <div ref={body} class="prose in-group selectable" dangerouslySetInnerHTML={{ __html: a.instructions_html }} />
        </Section>
      ) : a.instructions ? (
        <Section title="Instructions">
          <div class="prose in-group plain selectable">{a.instructions}</div>
        </Section>
      ) : null}

      {a.rubric.length > 0 ? (
        <Section title="Rubric">
          {a.rubric.map((r) => (
            <div class="rubric-row">
              <div class="rubric-top">
                <span class="rubric-criterion">{r.criterion}</span>
                <span class="rubric-points">
                  {r.mark?.points != null ? `${r.mark.points} / ${r.points ?? '—'}` : `${r.points ?? '—'} pts`}
                </span>
              </div>
              {r.mark?.comment ? <p class="rubric-comment">“{r.mark.comment}”</p> : null}
            </div>
          ))}
        </Section>
      ) : null}

      {a.comments.length > 0 ? (
        <Section title="Comments">
          {a.comments.map((c) => (
            <div class="comment">
              <p class="selectable">{c.text}</p>
              <span>{c.author}</span>
            </div>
          ))}
        </Section>
      ) : null}

      <div class="section">
        <Button kind="filled" wide href={a.url} external>
          <External size={18} /> {a.submission?.submitted_at || a.graded_at ? 'Open in Canvas' : 'Hand it in on Canvas'}
        </Button>
      </div>
    </>
  );
}

function kindWord(kind: string): string {
  if (kind === 'quiz') return 'Quiz';
  if (kind === 'discussion') return 'Discussion';
  if (kind === 'todo' || kind === 'to-do') return 'To-do';
  return 'Assignment';
}
