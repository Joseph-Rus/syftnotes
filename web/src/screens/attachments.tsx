import type { Attachment } from '../api/types';
import { fileSize } from '../format';
import { Doc } from '../ui/icons';
import { Row } from '../ui/kit';

/** The kind of file, in a word, from its MIME type: "PDF", "Photo", "Slides", "Document". */
export function kindWord(type: string): string {
  if (type === 'application/pdf') return 'PDF';
  if (type.startsWith('image/')) return 'Photo';
  if (type.includes('presentation')) return 'Slides';
  if (type.includes('word')) return 'Document';
  return 'File';
}

/** One attachment in a list: its name, kind and size, and "Reading your handwriting…" while its words are still
 * being read (LibraryWeb.Attachments.ReadingWords) — the app is told to keep asking until that clears. */
export function AttachmentRow({ attachment, url }: { attachment: Attachment; url: string | undefined }) {
  const state = attachment.reading ? 'Reading your handwriting…' : attachment.hasText ? '' : 'No words found in it';
  const subtitle = [kindWord(attachment.type), fileSize(attachment.size), state].filter(Boolean).join(' · ');
  return <Row href={url} lead={<Doc size={20} />} title={attachment.name} subtitle={subtitle} external={!!url} />;
}
