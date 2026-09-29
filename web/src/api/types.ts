// The library's /api/v2 as the phone reads it. Each shape is typed from the C# that writes it (named beside it), or
// from the round-0.9 contract for the endpoints being built alongside this app.

/** GET /api/v2/me (no sign-in needed): whether this phone is paired, and which library it reached. */
export interface Me {
  paired: boolean;
  library: { name: string; version: string };
  /** Which phone this is, once paired (a library from before it says nothing). */
  device?: Device | null;
}

/** POST /api/v2/devices/pair → the device this phone now is. */
export interface Device {
  id: string;
  name: string;
  added: string;
  lastSeen?: string | null;
}

/** One class, from LibraryReader.Overview(). `color` is the class's place in the library: its dot's colour. */
export interface ClassInfo {
  name: string;
  lectures: number;
  color: number;
  description?: string;
  /** The linked Canvas course's short code ("CSCI 321"), "" when there's none. */
  code?: string;
  aliases?: string[];
}

/** GET /api/v2/library (LibraryWeb.App.cs → LibraryReader.Overview). */
export interface LibraryOverview {
  name: string;
  version: string;
  classes: ClassInfo[];
  unsorted: number;
  /** Lectures queued or being written. */
  writing: number;
  ask: boolean;
  notes_model: string | null;
  laptops?: unknown;
  gone?: string[];
}

export type LectureStatus = 'queued' | 'working' | 'done' | 'failed' | (string & {});

/** One lecture in a list (LibraryReader.Summary). */
export interface LectureSummary {
  id: string;
  title: string | null;
  class: string | null;
  date: string | null;
  seconds: number | null;
  owner: string | null;
  status: LectureStatus;
  summary: string;
  topics: string[];
  has_transcript: boolean;
}

/** GET /api/v2/lectures/{id} (LibraryReader.Lecture): the summary plus the notes and transcript. */
export interface Lecture extends LectureSummary {
  notes: string;
  transcript: string;
  classified_by: string | null;
  notes_model: string | null;
  error: string | null;
}

/** GET /api/v2/lectures/{id}/rendered (contract): the notes as safe HTML, diagrams inline, no scripts. */
export interface RenderedLecture {
  id: string;
  title: string;
  class: string | null;
  date: string | null;
  html: string;
}

/** A passage the search found (LibraryReader.PassageJson). */
export interface Passage {
  id: string;
  title: string | null;
  class: string | null;
  date: string | null;
  /** "notes" or "transcript". */
  kind: string;
  section: string;
  /** Seconds into the recording, for a transcript passage. */
  at: number | null;
  text: string;
}

/** GET /api/v2/search (LibraryReader.Search). */
export interface SearchResults {
  query: string;
  lectures: LectureSummary[];
  passages: Passage[];
  classes: ClassInfo[];
}

/** One source an answer used (AskSource, snake_case). */
export interface AskSource {
  id: string | null;
  title: string;
  class: string | null;
  date: string | null;
  at: number | null;
  section: string;
}

/** POST /api/v2/ai/ask (AskReply, snake_case). */
export interface AskReply {
  answer: string;
  sources: AskSource[];
  engine: string;
  engine_name: string;
  asked: string;
  asked_name: string;
  fell_back: boolean;
  why: string;
}

/** What POST /api/v2/ai/ask with stream: true sends, one per line (LibraryWeb.Ai.cs StreamAnswerAsync). */
export type AskEvent =
  | { kind: 'text'; text: string }
  | { kind: 'answer'; text: string }
  | { kind: 'done'; reply: AskReply }
  | { kind: 'error'; status: number; detail: string };

/** A chat in the list (LibraryWeb.Chat.cs ChatJson, full = false). */
export interface ChatSummary {
  id: string;
  title: string;
  class: string;
  lecture: string;
  updated: string;
  provider: string;
  messages: null;
}

export interface ChatMessage {
  role: 'user' | 'assistant' | (string & {});
  text: string;
  when: string;
  tools: string[];
  changed: string[];
  change: string;
  failed: boolean;
}

/** GET /api/v2/chats/{id}. */
export interface Chat extends Omit<ChatSummary, 'messages'> {
  messages: ChatMessage[];
}

/** What POST /api/v2/chat sends, one per line: first the chat's id, then its events, then done. */
export type ChatEvent =
  | { chat: string }
  | { kind: 'text' | 'tool' | 'error' | (string & {}); text: string; name: string; path: string }
  | { kind: 'done'; text: string };

/** One assignment on the Due list (CanvasView.ItemJson). `due` is local time without an offset. */
export interface DueItem {
  class: string;
  id: number;
  name: string;
  /** "assignment", "quiz", "discussion" or "todo". */
  kind: string;
  due: string | null;
  due_at: string | null;
  points: number | null;
  status: string;
  label: string;
  score: number | null;
  grade: string;
  score_text: string;
  late: boolean;
  missing: boolean;
  excused: boolean;
  submitted: string | null;
  graded_at: string | null;
  marked_done: string | null;
  url: string;
  folder: string | null;
}

export type DueGroupKey = 'overdue' | 'week' | 'later' | 'undated' | 'handed_in';

/** GET /api/v2/canvas/due (CanvasView.Due). */
export interface DueList {
  synced: string | null;
  to_hand_in: number;
  next: DueItem | null;
  groups: { key: DueGroupKey; label: string; items: DueItem[] }[];
}

/** One event in GET /api/v2/calendar/upcoming (contract). `class` is the matched class, or null. */
export interface UpcomingEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  allDay: boolean;
  location: string | null;
  class: string | null;
}

export interface Upcoming {
  events: UpcomingEvent[];
  updated: string | null;
}

/** A file added to a class or lecture (Attachment.ToJson in engine/src/StudyStash.Core/Attachments.cs). */
export interface Attachment {
  id: string;
  name: string;
  /** Always a real class name (or "Unsorted"), never empty. */
  class: string;
  lecture: string | null;
  size: number;
  type: string;
  added: string;
  hasText: boolean;
  /** Still being read: show "Reading your handwriting…" and keep asking. */
  reading: boolean;
}

/** GET /api/v2/canvas/assignment (CanvasView.Assignment): one assignment's page. `instructions_html` is the
 * instructions drawn as safe HTML the way a lecture's notes are (PhoneNotes.Render); a library from before it
 * sends only the Markdown. */
export interface AssignmentDetail extends DueItem {
  instructions: string | null;
  instructions_html?: string;
  unlock_at: string | null;
  lock_at: string | null;
  submission_types: string[];
  allowed_attempts: number | null;
  grading_type: string;
  rubric: { criterion: string; points: number | null; description?: string; mark?: { points: number | null; comment?: string } | null }[];
  comments: { author: string; text: string; when?: string; mine?: boolean }[];
  submission: { state: string; submitted_at: string | null; graded_at: string | null; score: number | null; grade: string | null; late: boolean } | null;
}

/** A voice memo sent from the phone (VoiceMemo.ToJson in LibraryWeb's VoiceMemos.cs): waiting for a computer that
 * records, being written down there, done (it's `lecture` now), or failed (`error`). */
export interface VoiceMemo {
  id: string;
  name: string;
  class: string | null;
  title: string;
  size: number;
  added: string;
  by: string;
  state: 'waiting' | 'transcribing' | 'done' | 'failed' | (string & {});
  computer: string | null;
  lecture: string | null;
  error: string | null;
  /** The lecture it became has reached the library (it's written down on the computer first, then sent). */
  ready?: boolean;
}
