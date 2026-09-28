import {
  ago,
  clock,
  dayLabel,
  dueWhen,
  duration,
  fileSize,
  groupBy,
  lectureGroup,
  lectureMeta,
  parseWhen,
  relative,
  timeRange,
} from '../src/format';

// Wednesday 24 September 2026, 10:30 in the morning, here.
const now = new Date(2026, 8, 24, 10, 30);

describe('reading dates', () => {
  test('a bare day is that day here', () => {
    const d = parseWhen('2026-09-23')!;
    expect([d.getFullYear(), d.getMonth(), d.getDate(), d.getHours()]).toEqual([2026, 8, 23, 0]);
  });

  test('a time with no offset is that time here', () => {
    const d = parseWhen('2026-10-02T23:59:00')!;
    expect([d.getDate(), d.getHours(), d.getMinutes()]).toEqual([2, 23, 59]);
  });

  test('a moment with Z is that moment', () => {
    expect(parseWhen('2026-09-10T09:00:00Z')!.getTime()).toBe(Date.UTC(2026, 8, 10, 9));
  });

  test('nothing, or nonsense, is null', () => {
    expect(parseWhen(null)).toBeNull();
    expect(parseWhen('soon')).toBeNull();
  });
});

describe('lectures', () => {
  test('a length reads as the Mac shows it', () => {
    expect(duration(72 * 60)).toBe('1 h 12 min');
    expect(duration(68 * 60)).toBe('1 h 08 min');
    expect(duration(48 * 60)).toBe('48 min');
    expect(duration(3600)).toBe('1 h');
    expect(duration(20)).toBe('under a minute');
    expect(duration(null)).toBe('');
  });

  test("a lecture's line is its day and length", () => {
    expect(lectureMeta('2026-09-23', 72 * 60, now)).toBe('Wed 23 Sep · 1 h 12 min');
    expect(lectureMeta('2025-05-02', null, now)).toBe('Fri 2 May 2025');
    expect(lectureMeta(null, null, now)).toBe('');
  });

  test('lectures sit under this week, last week, then their month', () => {
    expect(lectureGroup('2026-09-22', now)).toBe('This week'); // Monday
    expect(lectureGroup('2026-09-21', now)).toBe('This week');
    expect(lectureGroup('2026-09-20', now)).toBe('Last week'); // Sunday before
    expect(lectureGroup('2026-09-14', now)).toBe('Last week');
    expect(lectureGroup('2026-09-13', now)).toBe('September');
    expect(lectureGroup('2025-12-01', now)).toBe('December 2025');
    expect(lectureGroup(null, now)).toBe('Undated');
  });

  test('grouping keeps order and joins neighbours', () => {
    expect(groupBy([1, 2, 3, 5, 7, 8], (n) => (n % 2 ? 'odd' : 'even'))).toEqual([
      { label: 'odd', items: [1] },
      { label: 'even', items: [2] },
      { label: 'odd', items: [3, 5, 7] },
      { label: 'even', items: [8] },
    ]);
  });
});

describe('what is due', () => {
  test('today, tomorrow and later days read plainly, with the time', () => {
    expect(dueWhen('2026-09-24T23:59:00', now)).toMatch(/^Today, 11:59\sPM$|^Today, 23:59$/);
    expect(dueWhen('2026-09-25T09:00:00', now)).toMatch(/^Tomorrow, /);
    expect(dueWhen('2026-10-02T17:00:00', now)).toMatch(/^Fri 2 Oct, /);
    expect(dueWhen('2026-09-23T12:00:00', now)).toMatch(/^Yesterday, /);
  });

  test('due at midnight, or on a bare day, is just the day', () => {
    expect(dueWhen('2026-09-26T00:00:00', now)).toBe('Sat 26 Sep');
    expect(dueWhen('2026-09-26', now)).toBe('Sat 26 Sep');
    expect(dueWhen(null, now)).toBe('No due date');
  });

  test('how long until reads in minutes, hours or days', () => {
    expect(relative('2026-09-24T10:50:00', now)).toBe('in 20 minutes');
    expect(relative('2026-09-24T13:30:00', now)).toBe('in 3 hours');
    expect(relative('2026-09-26T10:30:00', now)).toBe('in 2 days');
    expect(relative('2026-09-23T10:30:00', now)).toBe('1 day ago');
  });
});

describe('the week ahead', () => {
  test('days read as today, tomorrow, or the weekday', () => {
    expect(dayLabel(new Date(2026, 8, 24, 18), now)).toBe('Today');
    expect(dayLabel(new Date(2026, 8, 25), now)).toBe('Tomorrow');
    expect(dayLabel(new Date(2026, 9, 1), now)).toBe('Thursday 1 Oct');
  });

  test('an event is all day, or from and to', () => {
    expect(timeRange('2026-09-24', '2026-09-25', true)).toBe('All day');
    expect(timeRange('2026-09-24T09:00:00', '2026-09-24T10:15:00', false)).toMatch(/9:00.*–.*10:15/);
  });
});

test('sizes read in bytes, KB, MB and GB', () => {
  expect(fileSize(12)).toBe('12 bytes');
  expect(fileSize(1)).toBe('1 byte');
  expect(fileSize(830 * 1024)).toBe('830 KB');
  expect(fileSize(2.4 * 1024 * 1024)).toBe('2.4 MB');
  expect(fileSize(150 * 1024 * 1024)).toBe('150 MB');
});

test('a moment in a recording reads like a clock', () => {
  expect(clock(245)).toBe('4:05');
  expect(clock(3723)).toBe('1:02:03');
});

test('how long ago', () => {
  expect(ago('2026-09-24T10:29:40', now)).toBe('just now');
  expect(ago('2026-09-24T10:00:00', now)).toBe('30 min ago');
  expect(ago('2026-09-24T07:00:00', now)).toBe('3 h ago');
  expect(ago('2026-09-23T20:00:00', now)).toBe('yesterday');
  expect(ago('2026-09-01T20:00:00', now)).toBe('Tue 1 Sep');
});
