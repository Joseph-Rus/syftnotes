import { useEffect, useRef, useState } from 'preact/hooks';
import type { Api } from '../api/client';
import { ApiError } from '../api/client';
import type { Me } from '../api/types';
import { cleanCode, takeCodeFromAddress } from '../boot';
import { currentDeviceName } from '../platform';
import { Button } from '../ui/kit';
import { SyftHello } from '../ui/Syft';

/**
 * The 6-digit code from Settings → Add a phone. Reachable-but-not-paired (`GET /me` said `paired: false`) means
 * there's a real library on the other end; typing the wrong or an expired code, or too many tries, comes back in
 * the library's own words (POST /api/v2/devices/pair).
 */
export function Pair({ api, library, onPaired }: { api: Api; library: Me['library'] | null; onPaired: () => void }) {
  const [code, setCode] = useState('');
  const [name, setName] = useState(() => currentDeviceName());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);
  // Opened from the QR code (or the Camera app scanning it): the address carries the code, so pair at once. On a
  // failure the box is there as always, with the library's words above it.
  const [scanned] = useState(takeCodeFromAddress);

  const submit = async (typed = code) => {
    if (typed.length !== 6 || busy) return;
    setBusy(true);
    setError(null);
    try {
      await api.pair(typed, name.trim() || currentDeviceName());
      onPaired();
    } catch (e) {
      setError(e instanceof ApiError ? e.detail : "Couldn't reach your library. Try again.");
      setCode('');
      input.current?.focus();
    } finally {
      setBusy(false);
    }
  };

  useEffect(() => {
    if (scanned) void submit(scanned);
  }, []);

  return (
    <div class="pair">
      <div class="pair-body">
        <SyftHello size={120} />
        <h1>{library ? `Add this phone to ${library.name}` : 'Add this phone'}</h1>
        <p class="lede">
          In Syft Notes on your computer, open Settings → Phone → <strong>Add a phone</strong>, then type the 6-digit
          code it shows.
        </p>
        <input
          ref={input}
          class="code-input"
          inputMode="numeric"
          autocomplete="one-time-code"
          pattern="\d*"
          placeholder="000000"
          value={code}
          disabled={busy}
          aria-label="6-digit code"
          onInput={(e) => {
            // No maxlength on the box: the browser would cut "123 456" (as Settings prints it) to "123 45" before
            // this hears of it. The six are counted here, after spaces and anything else that isn't a digit are gone.
            const next = cleanCode(e.currentTarget.value);
            // The box shows what will be sent, even when that's what it showed already (a letter typed after "12").
            if (e.currentTarget.value !== next) e.currentTarget.value = next;
            setCode(next);
            if (next.length === 6) void submit(next);
          }}
        />
        {error ? (
          <p class="pair-error" role="alert">
            {error}
          </p>
        ) : null}
        <label class="field">
          <span>What should we call this phone?</span>
          <input
            type="text"
            value={name}
            maxLength={60}
            disabled={busy}
            onInput={(e) => setName(e.currentTarget.value)}
            placeholder={currentDeviceName()}
          />
        </label>
        <Button kind="filled" wide disabled={code.length !== 6 || busy} onClick={() => void submit()}>
          {busy ? 'Pairing…' : 'Pair'}
        </Button>
      </div>
    </div>
  );
}
