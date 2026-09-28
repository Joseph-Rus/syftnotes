// Dates, times and sizes in the app's words: "Tue 23 Sep · 1 h 12 min", "This week", "Tomorrow, 5:00 PM".

const DAY = 86_400_000;

/**
 * A date the library sent: "2026-09-23" is that day here; "2026-10-02T23:59:00" (no offset) is that time here;
 * anything with an offset or Z is that moment.
 */
export function parseWhen(s: string | null | undefined): Date | null {
  if (!s) return null;
  const day = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
  if (day) return new Date(Number(day[1]), Number(day[2]) - 1, Number(day[3]));
  const local = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?$/.exec(s);
  if (local)
    return new Date(
      Number(local[1]),
      Number(local[2]) - 1,
      Number(local[3]),
      Number(local[4]),
      Number(local[5]),
      Number(local[6] ?? 0),
    );
  const t = Date.parse(s);
  return Number.isNaN(t) ? null : new Date(t);
}

const startOfDay = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
/** Whole days from `now`'s day to `d`'s day (0 today, 1 tomorrow, -1 yesterday). */
export const daysBetween = (now: Date, d: Date) =>
  Math.round((startOfDay(d).getTime() - startOfDay(now).getTime()) / DAY);

const WEEKDAYS = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const MONTHS = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];
const weekday = (d: Date) => WEEKDAYS[d.getDay()]!.slice(0, 3);
const weekdayLong = (d: Date) => WEEKDAYS[d.getDay()]!;
const month = (d: Date) => MONTHS[d.getMonth()]!.slice(0, 3);
const monthLong = (d: Date) => MONTHS[d.getMonth()]!;

/** "Tue 23 Sep", with the year when it isn't this one. */
export function shortDate(d: Date, now = new Date()): string {
  const s = `${weekday(d)} ${d.getDate()} ${month(d)}`;
  return d.getFullYear() === now.getFullYear() ? s : `${s} ${d.getFullYear()}`;
}

/** "5:00 PM" or "17:00", as the phone tells time. */
export function time(d: Date): string {
  return d.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' });
}

/** "1 h 12 min", "48 min", "under a minute". */
export function duration(seconds: number | null | undefined): string {
  if (!seconds || seconds < 60) return seconds ? 'under a minute' : '';
  const m = Math.round(seconds / 60);
  if (m < 60) return `${m} min`;
  const h = Math.floor(m / 60);
  const rest = m % 60;
  return rest ? `${h} h ${rest.toString().padStart(2, '0')} min` : `${h} h`;
}

/** A lecture's line under its title: "Tue 23 Sep · 1 h 12 min". */
export function lectureMeta(date: string | null, seconds: number | null, now = new Date()): string {
  const d = parseWhen(date);
  return [d ? shortDate(d, now) : '', duration(seconds)].filter(Boolean).join(' · ');
}

/** Monday of `d`'s week. */
function weekStart(d: Date): Date {
  const s = startOfDay(d);
  const back = (s.getDay() + 6) % 7;
  return new Date(s.getFullYear(), s.getMonth(), s.getDate() - back);
}

/** The heading a lecture sits under in a class: "This week", "Last week", then the month ("August", "May 2025"). */
export function lectureGroup(date: string | null, now = new Date()): string {
  const d = parseWhen(date);
  if (!d) return 'Undated';
  const thisWeek = weekStart(now).getTime();
  const t = d.getTime();
  if (t >= thisWeek) return 'This week';
  if (t >= thisWeek - 7 * DAY) return 'Last week';
  return d.getFullYear() === now.getFullYear() ? monthLong(d) : `${monthLong(d)} ${d.getFullYear()}`;
}

/** Items under their headings, in the order they came (newest first). */
export function groupBy<T>(items: readonly T[], label: (item: T) => string): { label: string; items: T[] }[] {
  const groups: { label: string; items: T[] }[] = [];
  for (const item of items) {
    const l = label(item);
    const last = groups[groups.length - 1];
    if (last && last.label === l) last.items.push(item);
    else groups.push({ label: l, items: [item] });
  }
  return groups;
}

/** A day as a heading: "Today", "Tomorrow", "Yesterday", "Wednesday 1 Oct". */
export function dayLabel(d: Date, now = new Date()): string {
  const n = daysBetween(now, d);
  if (n === 0) return 'Today';
  if (n === 1) return 'Tomorrow';
  if (n === -1) return 'Yesterday';
  const s = `${weekdayLong(d)} ${d.getDate()} ${month(d)}`;
  return d.getFullYear() === now.getFullYear() ? s : `${s} ${d.getFullYear()}`;
}

/** When something is due: "Today, 11:59 PM", "Tomorrow, 9:00 AM", "Fri 3 Oct, 5:00 PM"; a due of midnight is the day. */
export function dueWhen(due: string | null, now = new Date()): string {
  const d = parseWhen(due);
  if (!d) return 'No due date';
  const n = daysBetween(now, d);
  const day = n === 0 ? 'Today' : n === 1 ? 'Tomorrow' : n === -1 ? 'Yesterday' : shortDate(d, now);
  const midnight = d.getHours() === 0 && d.getMinutes() === 0;
  return midnight || due?.length === 10 ? day : `${day}, ${time(d)}`;
}

/** How long until (or since) it's due, for the next piece of work: "in 3 hours", "in 2 days", "2 days ago". */
export function relative(due: string | null, now = new Date()): string {
  const d = parseWhen(due);
  if (!d) return '';
  const ms = d.getTime() - now.getTime();
  const abs = Math.abs(ms);
  const words = (n: number, unit: string) => `${n} ${unit}${n === 1 ? '' : 's'}`;
  const amount =
    abs < 3_600_000
      ? words(Math.max(1, Math.round(abs / 60_000)), 'minute')
      : abs < DAY
        ? words(Math.round(abs / 3_600_000), 'hour')
        : words(Math.round(abs / DAY), 'day');
  return ms >= 0 ? `in ${amount}` : `${amount} ago`;
}

/** An event's time: "All day", "9:00 – 10:15 AM" style, as the phone tells time. */
export function timeRange(start: string, end: string, allDay: boolean): string {
  if (allDay) return 'All day';
  const s = parseWhen(start);
  const e = parseWhen(end);
  if (!s) return '';
  return e ? `${time(s)} – ${time(e)}` : time(s);
}

/** "2.4 MB", "830 KB", "12 bytes". */
export function fileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} byte${bytes === 1 ? '' : 's'}`;
  const units = ['KB', 'MB', 'GB'];
  let v = bytes / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v >= 10 || i === 0 ? Math.round(v) : v.toFixed(1)} ${units[i]}`;
}

/** A moment in a recording: "4:05", "1:02:03". */
export function clock(seconds: number): string {
  const s = Math.max(0, Math.floor(seconds));
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  const ss = (s % 60).toString().padStart(2, '0');
  return h ? `${h}:${m.toString().padStart(2, '0')}:${ss}` : `${m}:${ss}`;
}

/** "just now", "5 min ago", "3 h ago", "yesterday", or the date. */
export function ago(when: string | null | undefined, now = new Date()): string {
  const d = parseWhen(when);
  if (!d) return '';
  const ms = now.getTime() - d.getTime();
  if (ms < 60_000) return 'just now';
  if (ms < 3_600_000) return `${Math.floor(ms / 60_000)} min ago`;
  if (daysBetween(now, d) === 0) return `${Math.floor(ms / 3_600_000)} h ago`;
  if (daysBetween(now, d) === -1) return 'yesterday';
  return shortDate(d, now);
}
