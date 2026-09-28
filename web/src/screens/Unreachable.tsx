import { Offline } from '../ui/icons';
import { Button } from '../ui/kit';

/**
 * Tailscale is off, the computer's asleep, or it's off the network: the phone can't reach the library at all.
 * `outdated` is the one other dead end: a library too old to know about phones (no /api/v2/me), reached with no
 * password to fall back on.
 */
export function Unreachable({ onRetry, outdated = false }: { onRetry: () => void; outdated?: boolean }) {
  return (
    <div class="unreachable">
      <div class="empty">
        <div class="empty-icon">
          <Offline size={48} />
        </div>
        <h2>{outdated ? "Your library doesn't know phones yet" : "Can't reach your library"}</h2>
        <p>
          {outdated
            ? 'Update Study Stash on the library computer, then try again.'
            : "Make sure Tailscale is on here and on the library's computer, and that computer is awake."}
        </p>
        <div class="empty-action">
          <Button kind="filled" onClick={onRetry}>
            Try again
          </Button>
        </div>
      </div>
    </div>
  );
}
