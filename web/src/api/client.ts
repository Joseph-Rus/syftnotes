import { readLines } from './ndjson';
import type {
  AskEvent,
  AskReply,
  AssignmentDetail,
  Attachment,
  Chat,
  ChatEvent,
  ChatSummary,
  Device,
  DueList,
  Lecture,
  LectureSummary,
  LibraryOverview,
  Me,
  RenderedLecture,
  SearchResults,
  Upcoming,
  VoiceMemo,
} from './types';

/**
 * Something the library said no to, or couldn't be asked at all. `status` 0 means the phone never reached it (no
 * network, or Tailscale off); `detail` is the library's own words when it gave some.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly detail: string,
  ) {
    super(detail);
    this.name = 'ApiError';
  }

  /** The library couldn't be reached. */
  get unreachable(): boolean {
    return this.status === 0;
  }

  /** This phone isn't (or is no longer) paired. */
  get unauthorized(): boolean {
    return this.status === 401;
  }
}

/** The progress of an upload: bytes sent of the whole. */
export interface UploadProgress {
  loaded: number;
  total: number;
}

/** A way to send a multipart upload with progress (XMLHttpRequest in the browser; a fake in tests). */
export type Uploader = (
  url: string,
  body: FormData,
  onProgress: (p: UploadProgress) => void,
  signal?: AbortSignal,
) => Promise<{ status: number; text: string }>;

export interface ApiOptions {
  /** Where the library is; '' (the default) means this page's own origin. */
  base?: string;
  fetch?: typeof fetch;
  upload?: Uploader;
  /** Called whenever the library says this phone isn't paired, so the app can show pairing again. */
  onUnauthorized?: () => void;
}

const json = { 'Content-Type': 'application/json' };

/** The library's /api/v2, typed. Every call carries the `device` cookie the pairing set. */
export class Api {
  private readonly base: string;
  private readonly fetcher: typeof fetch;
  private readonly uploader: Uploader;
  private readonly onUnauthorized: (() => void) | undefined;

  constructor(options: ApiOptions = {}) {
    this.base = (options.base ?? '').replace(/\/$/, '');
    this.fetcher = options.fetch ?? ((input, init) => fetch(input, init));
    this.uploader = options.upload ?? xhrUpload;
    this.onUnauthorized = options.onUnauthorized;
  }

  /** A path under /api/v2 with its query (empty and missing values left out). */
  url(path: string, query: Record<string, string | number | undefined | null> = {}): string {
    const params = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) if (v !== undefined && v !== null && v !== '') params.set(k, String(v));
    const q = params.toString();
    return `${this.base}/api/v2${path}${q ? `?${q}` : ''}`;
  }

  private async send(path: string, init: RequestInit = {}, query?: Record<string, string | number | undefined | null>) {
    let response: Response;
    try {
      response = await this.fetcher(this.url(path, query), { credentials: 'same-origin', ...init });
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') throw e;
      throw new ApiError(0, "Can't reach your library.");
    }
    if (!response.ok) throw await this.failure(response);
    return response;
  }

  private async failure(response: Response): Promise<ApiError> {
    let detail = '';
    try {
      const body = (await response.json()) as { detail?: string; error?: string };
      detail = body.detail ?? body.error ?? '';
    } catch {
      // not JSON: the status says enough
    }
    if (response.status === 401) this.onUnauthorized?.();
    return new ApiError(response.status, detail || defaultWords(response.status));
  }

  private async get<T>(path: string, query?: Record<string, string | number | undefined | null>, signal?: AbortSignal) {
    return (await (await this.send(path, { signal: signal ?? null }, query)).json()) as T;
  }

  private async post<T>(path: string, body: unknown, signal?: AbortSignal) {
    const response = await this.send(path, { method: 'POST', headers: json, body: JSON.stringify(body), signal: signal ?? null });
    return (await response.json()) as T;
  }

  private async del<T>(path: string) {
    return (await (await this.send(path, { method: 'DELETE' })).json()) as T;
  }

  // --- this phone ---------------------------------------------------------------------------------------------

  me(signal?: AbortSignal) {
    return this.get<Me>('/me', undefined, signal);
  }

  /** Pair this phone with the 6-digit code the library showed; the library sets the `device` cookie. */
  async pair(code: string, name: string): Promise<Device> {
    return (await this.post<{ device: Device }>('/devices/pair', { code, name })).device;
  }

  async devices(): Promise<Device[]> {
    return (await this.get<{ devices: Device[] }>('/devices')).devices;
  }

  removeDevice(id: string) {
    return this.del<unknown>(`/devices/${encodeURIComponent(id)}`);
  }

  // --- the library --------------------------------------------------------------------------------------------

  library(signal?: AbortSignal) {
    return this.get<LibraryOverview>('/library', undefined, signal);
  }

  /** Lectures newest first: in one class (or "Unsorted") or all, `before` a date to page back. */
  lectures(options: { class?: string; limit?: number; before?: string } = {}, signal?: AbortSignal) {
    return this.get<LectureSummary[]>('/lectures', { class: options.class, limit: options.limit, before: options.before }, signal);
  }

  lecture(id: string, signal?: AbortSignal) {
    return this.get<Lecture>(`/lectures/${encodeURIComponent(id)}`, undefined, signal);
  }

  rendered(id: string, signal?: AbortSignal) {
    return this.get<RenderedLecture>(`/lectures/${encodeURIComponent(id)}/rendered`, undefined, signal);
  }

  search(q: string, options: { class?: string; limit?: number } = {}, signal?: AbortSignal) {
    return this.get<SearchResults>('/search', { q, class: options.class, limit: options.limit }, signal);
  }

  // --- asking -------------------------------------------------------------------------------------------------

  /** A question answered from the notes, as it's written. Yields each event; the last is `done` or `error`. */
  async *ask(
    question: string,
    scope: { lecture?: string; class?: string } = {},
    signal?: AbortSignal,
  ): AsyncGenerator<AskEvent> {
    const response = await this.send('/ai/ask', {
      method: 'POST',
      headers: json,
      body: JSON.stringify({ question, lecture: scope.lecture, class: scope.class, stream: true }),
      signal: signal ?? null,
    });
    if (!response.body || !(response.headers.get('Content-Type') ?? '').includes('ndjson')) {
      // An older library answers all at once.
      const reply = (await response.json()) as AskReply;
      yield { kind: 'done', reply };
      return;
    }
    yield* readLines<AskEvent>(response.body);
  }

  /** Let the engine that will answer get ready while the question is typed. */
  warm() {
    return this.post<unknown>('/ai/warm', {}).catch(() => undefined);
  }

  chats(signal?: AbortSignal) {
    return this.get<ChatSummary[]>('/chats', undefined, signal);
  }

  chat(id: string, signal?: AbortSignal) {
    return this.get<Chat>(`/chats/${encodeURIComponent(id)}`, undefined, signal);
  }

  deleteChat(id: string) {
    return this.del<{ deleted: string }>(`/chats/${encodeURIComponent(id)}`);
  }

  /** One turn of a chat, as it's written: first the chat's id, then its events, then `done`. */
  async *sendChat(
    message: string,
    options: { chat?: string; class?: string; lecture?: string } = {},
    signal?: AbortSignal,
  ): AsyncGenerator<ChatEvent> {
    const response = await this.send('/chat', {
      method: 'POST',
      headers: json,
      body: JSON.stringify({ message, chat: options.chat, class: options.class, lecture: options.lecture, edit: false }),
      signal: signal ?? null,
    });
    if (!response.body) return;
    yield* readLines<ChatEvent>(response.body);
  }

  // --- what's coming --------------------------------------------------------------------------------------------

  due(signal?: AbortSignal) {
    return this.get<DueList>('/canvas/due', undefined, signal);
  }

  assignment(cls: string, id: string | number, signal?: AbortSignal) {
    return this.get<AssignmentDetail>('/canvas/assignment', { class: cls, id: String(id) }, signal);
  }

  upcoming(signal?: AbortSignal) {
    return this.get<Upcoming>('/calendar/upcoming', undefined, signal);
  }

  // --- files --------------------------------------------------------------------------------------------------

  async attachments(scope: { class?: string; lecture?: string } = {}, signal?: AbortSignal): Promise<Attachment[]> {
    return (await this.get<{ attachments: Attachment[] }>('/attachments', scope, signal)).attachments;
  }

  attachmentUrl(id: string) {
    return this.url(`/attachments/${encodeURIComponent(id)}/raw`);
  }

  deleteAttachment(id: string) {
    return this.del<unknown>(`/attachments/${encodeURIComponent(id)}`);
  }

  // --- voice memos -------------------------------------------------------------------------------------------

  async voiceMemos(signal?: AbortSignal): Promise<VoiceMemo[]> {
    return (await this.get<{ memos: VoiceMemo[] }>('/voice-memos', undefined, signal)).memos;
  }

  retryVoiceMemo(id: string) {
    return this.post<VoiceMemo>(`/voice-memos/${encodeURIComponent(id)}/retry`, {});
  }

  deleteVoiceMemo(id: string) {
    return this.del<{ memos: VoiceMemo[] }>(`/voice-memos/${encodeURIComponent(id)}`);
  }

  /** Send a recording from Voice Memos, to become a lecture in `class` (or sorted by the library) titled `title`. */
  async sendVoiceMemo(
    file: File,
    to: { class?: string; title?: string },
    onProgress: (p: UploadProgress) => void = () => {},
    signal?: AbortSignal,
  ): Promise<VoiceMemo> {
    const form = new FormData();
    form.append('file', file, file.name || 'Voice memo.m4a');
    if (to.class) form.append('class', to.class);
    if (to.title) form.append('title', to.title);
    let result: { status: number; text: string };
    try {
      result = await this.uploader(this.url('/voice-memos'), form, onProgress, signal);
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') throw e;
      throw new ApiError(0, "Can't reach your library.");
    }
    let body: Partial<VoiceMemo> & { detail?: string } = {};
    try {
      body = JSON.parse(result.text) as typeof body;
    } catch {
      // not JSON
    }
    if (result.status < 200 || result.status >= 300) {
      if (result.status === 401) this.onUnauthorized?.();
      if (result.status === 404) throw new ApiError(404, 'Your library is older than this: update Study Stash on your computer to send voice memos.');
      throw new ApiError(result.status, body.detail ?? defaultWords(result.status));
    }
    return body as VoiceMemo;
  }

  /** Send files to a class (and a lecture, when one is chosen), saying how far along it is. */
  async upload(
    files: File[],
    to: { class?: string; lecture?: string },
    onProgress: (p: UploadProgress) => void = () => {},
    signal?: AbortSignal,
  ): Promise<Attachment[]> {
    const form = new FormData();
    for (const f of files) form.append('file', f, f.name);
    if (to.class) form.append('class', to.class);
    if (to.lecture) form.append('lecture', to.lecture);
    let result: { status: number; text: string };
    try {
      result = await this.uploader(this.url('/attachments'), form, onProgress, signal);
    } catch (e) {
      if (e instanceof DOMException && e.name === 'AbortError') throw e;
      throw new ApiError(0, "Can't reach your library.");
    }
    let body: { attachments?: Attachment[]; detail?: string; error?: string } = {};
    try {
      body = JSON.parse(result.text) as typeof body;
    } catch {
      // not JSON
    }
    if (result.status < 200 || result.status >= 300) {
      if (result.status === 401) this.onUnauthorized?.();
      throw new ApiError(result.status, body.detail ?? body.error ?? defaultWords(result.status));
    }
    return body.attachments ?? [];
  }
}

function defaultWords(status: number): string {
  if (status === 401) return 'This phone needs pairing with your library again.';
  if (status === 404) return "That isn't in your library any more.";
  if (status === 413) return "That's too big to send in one go (up to 200 MB).";
  if (status === 429) return 'Too many tries. Wait a minute, then try again.';
  if (status >= 500) return 'Your library had a problem. Try again in a moment.';
  return 'Something went wrong.';
}

/** The browser's upload, the one way to see progress while a file goes up. */
export const xhrUpload: Uploader = (url, body, onProgress, signal) =>
  new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest();
    xhr.open('POST', url);
    xhr.withCredentials = true;
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable) onProgress({ loaded: e.loaded, total: e.total });
    };
    xhr.onload = () => resolve({ status: xhr.status, text: xhr.responseText });
    xhr.onerror = () => reject(new TypeError('network'));
    xhr.onabort = () => reject(new DOMException('stopped', 'AbortError'));
    signal?.addEventListener('abort', () => xhr.abort());
    xhr.send(body);
  });
