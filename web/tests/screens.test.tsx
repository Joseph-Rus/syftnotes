import { render, screen, waitFor } from '@testing-library/preact';
import { Api } from '../src/api/client';
import type { DueList, LibraryOverview, VoiceMemo } from '../src/api/types';
import { forget } from '../src/data/resource';
import { DueScreen } from '../src/screens/Due';
import { titleFrom, VoiceMemoScreen } from '../src/screens/VoiceMemo';

/** An Api whose library answers from `routes` (by path under /api/v2), JSON. */
function fakeApi(routes: Record<string, unknown>): Api {
  const fetcher = (async (input: RequestInfo | URL) => {
    const path = new URL(String(input), 'https://lib.example').pathname.replace('/api/v2', '');
    if (!(path in routes))
      return new Response('{"detail":"no"}', { status: 404, headers: { 'Content-Type': 'application/json' } });
    return new Response(JSON.stringify(routes[path]), { status: 200, headers: { 'Content-Type': 'application/json' } });
  }) as typeof fetch;
  return new Api({ fetch: fetcher });
}

const overview: LibraryOverview = {
  name: "Sam's library",
  version: '0.10.0',
  classes: [
    { name: 'CS 101', lectures: 3, color: 0 },
    { name: 'BIO 110', lectures: 1, color: 1 },
  ],
  unsorted: 0,
  writing: 0,
  ask: true,
  notes_model: null,
};

const item = (name: string, cls: string, due: string, extra: Partial<DueList['groups'][0]['items'][0]> = {}) => ({
  class: cls,
  id: name.length,
  name,
  kind: 'assignment',
  due,
  due_at: null,
  points: 10,
  status: 'open',
  label: 'To do',
  score: null,
  grade: '',
  score_text: '',
  late: false,
  missing: false,
  excused: false,
  submitted: null,
  graded_at: null,
  marked_done: null,
  url: 'https://canvas.example/a',
  folder: null,
  ...extra,
});

beforeEach(() => forget());

describe('Due', () => {
  test('the next thing first, then what is to do by when, with an empty group left out', async () => {
    const due: DueList = {
      synced: null,
      to_hand_in: 2,
      next: item('Lab 3', 'CS 101', '2030-01-02T23:59'),
      groups: [
        { key: 'overdue', label: 'Overdue', items: [] },
        { key: 'week', label: 'This week', items: [item('Lab 3', 'CS 101', '2030-01-02T23:59')] },
        {
          key: 'later',
          label: 'Later',
          items: [item('Cell essay', 'BIO 110', '2030-02-01T12:00', { missing: true, label: 'Missing' })],
        },
        { key: 'undated', label: 'No due date', items: [] },
        {
          key: 'handed_in',
          label: 'Handed in',
          items: [item('Lab 2', 'CS 101', '2029-12-20T23:59', { score_text: '9/10' })],
        },
      ],
    };
    render(
      <DueScreen
        api={fakeApi({ '/canvas/due': due, '/calendar/upcoming': { events: [], updated: null }, '/library': overview })}
      />,
    );

    await screen.findByText('Next up');
    expect(screen.getByText('2 to hand in')).toBeTruthy();
    expect(screen.getByText('This week')).toBeTruthy();
    expect(screen.getByText('Later')).toBeTruthy();
    expect(screen.queryByText('Overdue')).toBeNull();
    expect(screen.queryByText('No due date')).toBeNull();
    expect(screen.getByText('Missing').className).toContain('missing');
    // Handed in is folded away until asked for.
    expect(screen.queryByText('Lab 2')).toBeNull();
    expect(screen.getByText('Show 1 handed in')).toBeTruthy();
  });

  test('with Canvas not linked it says how to get it', async () => {
    const due: DueList = { synced: null, to_hand_in: 0, next: null, groups: [] };
    render(
      <DueScreen
        api={fakeApi({ '/canvas/due': due, '/calendar/upcoming': { events: [], updated: null }, '/library': overview })}
      />,
    );
    await screen.findByText('Nothing due');
    expect(screen.getByText(/Link your classes to Canvas/)).toBeTruthy();
  });
});

describe('voice memos', () => {
  test('a recording’s name titles it, but not Voice Memos’ own "New Recording"', () => {
    expect(titleFrom('Bio lab.m4a')).toBe('Bio lab');
    expect(titleFrom('Recursion — part 2.mp3')).toBe('Recursion — part 2');
    expect(titleFrom('New Recording 14.m4a')).toBe('');
    expect(titleFrom('New Recording.m4a')).toBe('');
  });

  test('each memo says how it is getting on, and one whose lecture has arrived opens it', async () => {
    const memo = (id: string, state: VoiceMemo['state'], extra: Partial<VoiceMemo> = {}): VoiceMemo => ({
      id,
      name: `${id}.m4a`,
      class: 'CS 101',
      title: `Memo ${id}`,
      size: 1000,
      added: new Date().toISOString(),
      by: 'iPhone',
      state,
      computer: null,
      lecture: null,
      error: null,
      ...extra,
    });
    const memos = [
      memo('a', 'waiting'),
      memo('b', 'transcribing', { computer: "Sam's MacBook" }),
      memo('c', 'done', { lecture: 'rec-1', ready: false, computer: "Sam's MacBook" }),
      memo('d', 'done', { lecture: 'rec-2', ready: true }),
      memo('e', 'failed', { error: 'The recording has no sound in it.' }),
    ];
    render(<VoiceMemoScreen api={fakeApi({ '/voice-memos': { memos }, '/library': overview })} />);

    await screen.findByText('Memo a');
    await waitFor(() => expect(screen.getByText(/Waiting for a computer that records/)).toBeTruthy());
    expect(screen.getAllByText(/Being written down on Sam's MacBook/)).toHaveLength(2);
    expect(screen.getByText(/A lecture now/)).toBeTruthy();
    expect(screen.getByText('Memo d').closest('a')?.getAttribute('href')).toBe('/app/lecture/rec-2');
    expect(screen.getByText('Memo c').closest('a')).toBeNull();
    expect(screen.getByText(/The recording has no sound in it/)).toBeTruthy();
    expect(screen.getByLabelText('Try again')).toBeTruthy();
  });
});
